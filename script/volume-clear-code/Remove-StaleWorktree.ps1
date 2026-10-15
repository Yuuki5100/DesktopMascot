<#
.SYNOPSIS
    最終操作から一定日数（既定 7 日）以上経過した git worktree を削除する。

.DESCRIPTION
    指定フォルダ配下の git リポジトリを走査し、追加 worktree のうち
    「最終操作日時」が閾値より古いものを git worktree remove --force で削除する。

    最終操作日時は、リポジトリの .git/worktrees/<名前>/ 配下にある
    index / HEAD / logs/HEAD の更新日時の最大値で判定する。
    （checkout・add・commit・reset 等を行うとこれらが更新される）

    以下は削除しない。
      - メイン worktree（リポジトリ本体）
      - lock されている worktree（git worktree lock）
      - 未コミットの変更がある worktree（-IncludeDirty 指定時を除く）

    ブランチ自体は削除しないため、コミット済みの内容は失われない。

.PARAMETER Root
    走査するフォルダ。既定は WSL 上の \\wsl.localhost\Ubuntu\home\goto\Git。
    \\wsl.localhost\<distro>\... （または \\wsl$\<distro>\...）を指定した場合、
    git は wsl.exe 経由で WSL 内の git を使って実行する
    （WSL 側の worktree は Linux パスで登録されているため、Windows の git.exe では正しく扱えない）。

.PARAMETER Days
    この日数以上操作されていない worktree を削除対象とする。既定 7。

.PARAMETER IncludeDirty
    未コミットの変更がある worktree も削除対象にする（変更は失われる）。

.PARAMETER Yes
    確認プロンプトを省略する（タスクスケジューラ等からの自動実行用）。

.PARAMETER DryRun
    実際には削除せず、判定結果のみ表示する。

.EXAMPLE
    # まず判定結果を確認（何も変更しない）
    .\Remove-StaleWorktree.ps1 -DryRun

.EXAMPLE
    # 14 日以上放置された worktree を確認なしで削除
    .\Remove-StaleWorktree.ps1 -Days 14 -Yes
#>
[CmdletBinding()]
param(
    [string]$Root = '\\wsl.localhost\Ubuntu\home\goto\Git',
    [int]$Days = 7,
    [switch]$IncludeDirty,
    [switch]$Yes,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

function Write-Step { param($m) Write-Host "`n==> $m" -ForegroundColor Cyan }
function Write-Ok   { param($m) Write-Host "    $m" -ForegroundColor Green }
function Write-Warn2{ param($m) Write-Host "    $m" -ForegroundColor Yellow }
function Write-Err2 { param($m) Write-Host "    $m" -ForegroundColor Red }

Add-Type -AssemblyName System.Windows.Forms

# ---------------------------------------------------------------- 起動失敗時のダイアログ
trap {
    $msg = $_.Exception.Message
    Write-Err2 "エラーにより中断しました: $msg"
    [System.Windows.Forms.MessageBox]::Show("古い worktree の削除に失敗しました。`n`n$msg", "worktree 削除エラー", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 1
}

# ---------------------------------------------------------------- WSL パス変換
# Root が \\wsl.localhost\<distro>\... の場合に設定される
$script:WslDistro = $null
$script:WslPrefix = $null

function ConvertTo-GitPath {
    # Windows 側パス → git に渡すパス（WSL 時は /home/... 形式）
    param([string]$Path)
    if (-not $script:WslDistro) { return $Path }
    $rest = $Path.Substring($script:WslPrefix.Length) -replace '\\', '/'
    if (-not $rest) { $rest = '/' }
    return $rest
}

function ConvertFrom-GitPath {
    # git が返すパス → Windows 側パス（WSL 時は \\wsl.localhost\<distro>\... 形式）
    param([string]$Path)
    if ($script:WslDistro -and $Path.StartsWith('/')) { return $script:WslPrefix + ($Path -replace '/', '\') }
    return $Path -replace '/', '\'
}

function Invoke-Git {
    # ネイティブ exe の stderr で中断しないよう、呼び出し中だけ ErrorActionPreference を緩める
    param([string]$Repo, [string[]]$GitArgs)
    $ErrorActionPreference = 'Continue'
    if ($script:WslDistro) {
        $out = & wsl.exe -d $script:WslDistro -e git -C (ConvertTo-GitPath $Repo) @GitArgs 2>&1
    } else {
        $out = & git.exe -C $Repo @GitArgs 2>&1
    }
    $ec = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    return [PSCustomObject]@{ ExitCode = $ec; Output = @($out | ForEach-Object { "$_" }) }
}

function Get-GitRepos {
    # .git/worktrees を持つ（= 追加 worktree がある）リポジトリだけを返す
    param([string]$Path)
    Get-ChildItem $Path -Directory -Recurse -Depth 2 -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq '.git' -and (Test-Path (Join-Path $_.FullName 'worktrees')) } |
        ForEach-Object { $_.Parent.FullName }
}

function Get-Worktrees {
    # git worktree list --porcelain を解析し、メイン以外の worktree を返す
    param([string]$Repo)
    $r = Invoke-Git $Repo @('worktree', 'list', '--porcelain')
    if ($r.ExitCode -ne 0) { throw "git worktree list に失敗しました ($Repo): $($r.Output -join ' ')" }

    $items = @()
    $cur = $null
    foreach ($line in $r.Output + '') {
        if ($line -like 'worktree *') {
            $cur = [ordered]@{ Path = $line.Substring(9); Branch = '(detached)'; Locked = $false; Prunable = $false }
        } elseif ($line -like 'branch *') {
            $cur.Branch = $line.Substring(7) -replace '^refs/heads/', ''
        } elseif ($line -like 'locked*') {
            $cur.Locked = $true
        } elseif ($line -like 'prunable*') {
            $cur.Prunable = $true
        } elseif ($line -eq '' -and $cur) {
            $items += [PSCustomObject]$cur
            $cur = $null
        }
    }
    # 先頭はメイン worktree
    return $items | Select-Object -Skip 1
}

function Get-LastActivity {
    # worktree の管理ディレクトリ内ファイルの更新日時から最終操作日時を求める
    param([string]$WorktreePath)
    $gitFile = Join-Path $WorktreePath '.git'
    if (-not (Test-Path $gitFile -PathType Leaf)) { return $null }
    $adminDir = ConvertFrom-GitPath ((Get-Content $gitFile -Raw) -replace '^gitdir:\s*', '').Trim()
    if (-not (Test-Path $adminDir)) { return $null }

    $times = foreach ($f in 'index', 'HEAD', 'logs\HEAD') {
        $p = Join-Path $adminDir $f
        if (Test-Path $p) { (Get-Item $p -Force).LastWriteTime }
    }
    if (-not $times) { return (Get-Item $adminDir -Force).LastWriteTime }
    return ($times | Measure-Object -Maximum).Maximum
}

# ---------------------------------------------------------------- 判定
$Root = $Root.TrimEnd('\')
if ($Root -match '^(\\\\wsl(?:\.localhost|\$)\\([^\\]+))') {
    $script:WslPrefix = $Matches[1]
    $script:WslDistro = $Matches[2]
}
Write-Step "判定（$Root 配下、最終操作から $Days 日以上経過したもの）"
if (-not (Test-Path $Root)) { throw "フォルダが見つかりません: $Root" }
if ($script:WslDistro) {
    if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) { throw "wsl.exe が見つかりません。" }
} elseif (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) {
    throw "git.exe が見つかりません。Git for Windows をインストールしてください。"
}

$threshold = (Get-Date).AddDays(-$Days)
$targets = @()
$repos = @(Get-GitRepos $Root)

foreach ($repo in $repos) {
    foreach ($wt in Get-Worktrees $repo) {
        $wtPath = ConvertFrom-GitPath $wt.Path
        $state = $null
        $last  = $null

        if ($wt.Prunable -or -not (Test-Path $wtPath)) {
            $state = 'フォルダ消失（prune 対象）'
        } else {
            $last = Get-LastActivity $wtPath
            if ($wt.Locked) {
                $state = 'skip: lock 中'
            } elseif ($null -eq $last) {
                $state = 'skip: 最終操作日時を判定できない'
            } elseif ($last -gt $threshold) {
                $state = 'skip: 期限内'
            } else {
                $dirty = Invoke-Git $wtPath @('status', '--porcelain')
                if ($dirty.ExitCode -ne 0) {
                    $state = 'skip: git status に失敗'
                } elseif ($dirty.Output -and -not $IncludeDirty) {
                    $state = 'skip: 未コミットの変更あり'
                } else {
                    $state = '削除対象'
                }
            }
        }

        $lastStr = if ($last) { $last.ToString('yyyy-MM-dd HH:mm') } else { '-' }
        $color = if ($state -like 'skip*') { 'Gray' } else { 'Yellow' }
        Write-Host ("    [{0}] {1}  最終操作:{2}  {3}" -f $state, $wtPath, $lastStr, $wt.Branch) -ForegroundColor $color

        if ($state -notlike 'skip*') {
            $targets += [PSCustomObject]@{ Repo = $repo; Path = $wtPath; Branch = $wt.Branch; Missing = ($state -like 'フォルダ消失*') }
        }
    }
}

if (-not $repos) { Write-Ok "追加 worktree を持つリポジトリはありません。" }

if (-not $targets) {
    Write-Ok "削除対象の worktree はありません。"
    return
}

if ($DryRun) {
    Write-Warn2 "`nDryRun のため、ここで終了します。実際の削除は -DryRun を外して実行してください。"
    return
}

# ---------------------------------------------------------------- 確認
if (-not $Yes) {
    Write-Step "確認"
    Write-Warn2 "上記の削除対象 $($targets.Count) 件を削除します。ブランチは残ります。"
    $ans = Read-Host "    続行しますか? (yes/no)"
    if ($ans -ne 'yes') { Write-Warn2 "中止しました。"; return }
}

# ---------------------------------------------------------------- 削除
Write-Step "削除"
$removed = 0
$failed  = @()
foreach ($t in $targets) {
    if ($t.Missing) { continue }   # フォルダ消失分は後段の prune で片付ける
    $r = Invoke-Git $t.Repo @('worktree', 'remove', '--force', (ConvertTo-GitPath $t.Path))
    if ($r.ExitCode -eq 0) {
        Write-Ok "削除しました: $($t.Path)"
        $removed++
    } else {
        Write-Err2 "削除に失敗しました: $($t.Path)  $($r.Output -join ' ')"
        $failed += $t.Path
    }
}

foreach ($repo in ($targets.Repo | Select-Object -Unique)) {
    $r = Invoke-Git $repo @('worktree', 'prune')
    if ($r.ExitCode -eq 0) { Write-Ok "prune しました: $repo" } else { Write-Err2 "prune に失敗しました: $repo  $($r.Output -join ' ')" }
}

# ---------------------------------------------------------------- 結果
Write-Step "結果"
$pruned = @($targets | Where-Object { $_.Missing }).Count
Write-Host "    削除: $removed 件  prune: $pruned 件  失敗: $($failed.Count) 件"

$icon = if ($failed) { [System.Windows.Forms.MessageBoxIcon]::Warning } else { [System.Windows.Forms.MessageBoxIcon]::Information }
$message = "最終操作から $Days 日以上経過した worktree を削除しました。`n`n  削除: $removed 件`n  prune: $pruned 件`n  失敗: $($failed.Count) 件"
if ($failed) { $message += "`n`n失敗:`n  " + ($failed -join "`n  ") }
[System.Windows.Forms.MessageBox]::Show($message, "worktree 削除完了", [System.Windows.Forms.MessageBoxButtons]::OK, $icon) | Out-Null

# ---------------------------------------------------------------- 終了待ち
# Enter キーが押されたらコンソールウィンドウを閉じる（-Yes 指定時は待たずに閉じる）。
# -NoExit やショートカット経由で起動した場合、exit ではスクリプトが終わるだけで
# ウィンドウが残るため、[Environment]::Exit でホストプロセスごと終了させる
if (-not $Yes) { Read-Host "`nEnter キーを押すとウィンドウを閉じます" | Out-Null }
[Environment]::Exit(0)
