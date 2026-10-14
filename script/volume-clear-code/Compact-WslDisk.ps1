<#
.SYNOPSIS
    WSL2 の ext4.vhdx を圧縮し、削除済み領域を Windows に返却する。

.DESCRIPTION
    WSL2 の仮想ディスク(VHDX)は動的拡張のため、WSL 内でファイルを削除しても
    ファイルサイズは縮まない。本スクリプトは以下を順に行う。

      1. 各ディストロで fstrim を実行（未使用ブロックを仮想ディスク層に通知）
      2. wsl --shutdown で全ディストロを停止
      3. VHDX を圧縮（Optimize-VHD があればそれを、無ければ diskpart を使用）
      4. 任意で sparse 属性を有効化（以降は自動で縮むようになる）

.PARAMETER Distro
    対象ディストロ名。省略時は全ディストロ。

.PARAMETER SetSparse
    圧縮後に sparse 属性を有効化する。以降 WSL が自動で領域を返却するようになる。

.PARAMETER SkipTrim
    fstrim をスキップする（既に WSL 内で実行済みの場合）。

.PARAMETER DryRun
    実際には変更せず、対象と現状サイズのみ表示する。

.EXAMPLE
    # まず現状確認（何も変更しない）
    .\Compact-WslDisk.ps1 -DryRun

.EXAMPLE
    # 管理者 PowerShell で実行
    .\Compact-WslDisk.ps1 -SetSparse

.NOTES
    - 管理者権限が必要（diskpart 経由の圧縮のため）。
    - 実行中は WSL が停止する。作業中のシェルは全て閉じられるので注意。
    - 圧縮は VHDX のその場で行われるため、巨大な空き容量は不要。
#>
[CmdletBinding()]
param(
    [string]$Distro,
    [switch]$SetSparse,
    [switch]$SkipTrim,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$env:WSL_UTF8 = 1
# wsl.exe は WSL_UTF8=1 で UTF-8 を出力するが、PowerShell 5.1 の既定は
# OEM コードページ(932)のため、そのままだと日本語が文字化けする
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

function Write-Step { param($m) Write-Host "`n==> $m" -ForegroundColor Cyan }
function Write-Ok   { param($m) Write-Host "    $m" -ForegroundColor Green }
function Write-Warn2{ param($m) Write-Host "    $m" -ForegroundColor Yellow }
function Write-Err2 { param($m) Write-Host "    $m" -ForegroundColor Red }

Add-Type -AssemblyName System.Windows.Forms

# ---------------------------------------------------------------- 起動失敗時のダイアログ
# 管理者権限不足・ディストロ未検出など、終了エラーで中断した場合に
# ダイアログを表示し、OK がクリックされるまで待ってから終了する
trap {
    $msg = $_.Exception.Message
    Write-Err2 "エラーにより中断しました: $msg"
    [System.Windows.Forms.MessageBox]::Show("WSL2 仮想ディスクの圧縮に失敗しました。`n`n$msg", "WSL2 圧縮エラー", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 1
}

function Get-FreeGB {
    $d = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
    return [math]::Round($d.FreeSpace / 1GB, 2)
}

function Get-WslDistroInfo {
    # レジストリから ディストロ名 -> BasePath を取得し、ext4.vhdx を解決する
    $root = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Lxss'
    if (-not (Test-Path $root)) { throw "WSL のレジストリキーが見つかりません: $root" }

    $list = @()
    Get-ChildItem $root -ErrorAction SilentlyContinue | ForEach-Object {
        $p = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
        if ($p.DistributionName -and $p.BasePath) {
            $base = [string]$p.BasePath
            if ($base.StartsWith('\\?\')) { $base = $base.Substring(4) }
            $vhd  = Join-Path $base 'ext4.vhdx'
            if (Test-Path $vhd) {
                $f = Get-Item $vhd
                $list += [PSCustomObject]@{
                    Name    = $p.DistributionName
                    Vhdx    = $vhd
                    SizeGB  = [math]::Round($f.Length / 1GB, 2)
                    Sparse  = [bool]($f.Attributes -band [IO.FileAttributes]::SparseFile)
                }
            }
        }
    }
    return $list
}

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $pr = New-Object Security.Principal.WindowsPrincipal($id)
    return $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# ---------------------------------------------------------------- 現状表示
Write-Step "現状の確認"
$all = Get-WslDistroInfo
if ($Distro) {
    $targets = $all | Where-Object { $_.Name -eq $Distro }
    if (-not $targets) { throw "ディストロが見つかりません: $Distro`n利用可能: $(($all.Name) -join ', ')" }
} else {
    $targets = $all
}

$freeBefore = Get-FreeGB
Write-Host "    C: 空き容量: $freeBefore GB"
$targets | ForEach-Object {
    $sp = if ($_.Sparse) { 'sparse:有効' } else { 'sparse:無効' }
    Write-Host ("    {0,-16} {1,8} GB  {2}  {3}" -f $_.Name, $_.SizeGB, $sp, $_.Vhdx)
}

if ($DryRun) {
    Write-Warn2 "`nDryRun のため、ここで終了します。実際の圧縮は -DryRun を外して実行してください。"
    return
}

if (-not (Test-Admin)) {
    throw "管理者権限が必要です。PowerShell を「管理者として実行」で開き直してください。"
}

# ---------------------------------------------------------------- 確認
Write-Step "確認"
Write-Warn2 "これから WSL を停止します。WSL 上で作業中のシェル・エディタ・プロセスは全て終了します。"
$ans = Read-Host "    続行しますか? (yes/no)"
if ($ans -ne 'yes') { Write-Warn2 "中止しました。"; return }

# ---------------------------------------------------------------- fstrim
if (-not $SkipTrim) {
    Write-Step "fstrim（未使用ブロックの通知）"
    foreach ($t in $targets) {
        Write-Host "    [$($t.Name)] fstrim 実行中..."
        # 停止中のディストロは wsl -d が起動してくれる
        # ネイティブ exe の stderr は 2>&1 で ErrorRecord 化されるため、
        # ここだけ ErrorActionPreference を緩める（警告1行で中断しないように）
        $ErrorActionPreference = 'Continue'
        $out = & wsl.exe -d $t.Name -u root -- fstrim -av 2>&1
        $ec = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        if ($ec -eq 0) {
            ($out | Where-Object { $_ -match '\S' }) | ForEach-Object { Write-Ok $_ }
        } else {
            Write-Warn2 "fstrim が失敗しました（スキップして続行）: $out"
        }
    }
} else {
    Write-Step "fstrim はスキップされました (-SkipTrim)"
}

# ---------------------------------------------------------------- shutdown
Write-Step "WSL を停止"
& wsl.exe --shutdown
Start-Sleep -Seconds 8   # vhdx のハンドルが解放されるまで待つ
Write-Ok "停止しました。"

# ---------------------------------------------------------------- 圧縮
$useOptimize = $null -ne (Get-Command Optimize-VHD -ErrorAction SilentlyContinue)

foreach ($t in $targets) {
    Write-Step "圧縮: $($t.Name)"
    $before = [math]::Round((Get-Item $t.Vhdx).Length / 1GB, 2)
    Write-Host "    圧縮前: $before GB"

    try {
        if ($useOptimize) {
            Write-Host "    Optimize-VHD を使用..."
            Optimize-VHD -Path $t.Vhdx -Mode Full
        } else {
            Write-Host "    diskpart を使用..."
            $dpScript = @"
select vdisk file="$($t.Vhdx)"
attach vdisk readonly
compact vdisk
detach vdisk
exit
"@
            $tmp = Join-Path $env:TEMP ("compact_{0}.txt" -f ([guid]::NewGuid().ToString('N')))
            Set-Content -Path $tmp -Value $dpScript -Encoding ascii
            $ErrorActionPreference = 'Continue'
            $dpOut = & diskpart.exe /s $tmp 2>&1
            $ec = $LASTEXITCODE
            $ErrorActionPreference = 'Stop'
            Remove-Item $tmp -Force -ErrorAction SilentlyContinue
            if ($ec -ne 0) { throw ($dpOut -join "`n") }
            ($dpOut | Where-Object { $_ -match '\S' }) | Select-Object -Last 6 | ForEach-Object { Write-Host "      $_" }
        }

        $after = [math]::Round((Get-Item $t.Vhdx).Length / 1GB, 2)
        $saved = [math]::Round($before - $after, 2)
        Write-Ok "圧縮後: $after GB  (削減 $saved GB)"
    }
    catch {
        Write-Err2 "圧縮に失敗しました: $($_.Exception.Message)"
    }

    # ------------------------------------------------------------ sparse
    if ($SetSparse) {
        Write-Host "    sparse 属性を有効化..."
        $ErrorActionPreference = 'Continue'
        $spOut = & wsl.exe --manage $t.Name --set-sparse true 2>&1
        $ec = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        if ($ec -eq 0) {
            Write-Ok "sparse 有効化に成功（以降は自動で領域が返却されます）"
        } else {
            Write-Warn2 "sparse 有効化に失敗: $spOut"
            Write-Warn2 "（現行の WSL はデータ破損の不具合により sparse を既定で無効化しています。--allow-unsafe での強制は非推奨。定期的に本スクリプトを再実行してください）"
        }
    }
}

# ---------------------------------------------------------------- 結果
Write-Step "結果"
$freeAfter = Get-FreeGB
$increased = [math]::Round($freeAfter - $freeBefore, 2)
Write-Host "    C: 空き容量: $freeBefore GB  ->  $freeAfter GB  (増加 $increased GB)"
Get-WslDistroInfo | Where-Object { $_.Name -in $targets.Name } | ForEach-Object {
    $sp = if ($_.Sparse) { 'sparse:有効' } else { 'sparse:無効' }
    Write-Host ("    {0,-16} {1,8} GB  {2}" -f $_.Name, $_.SizeGB, $sp)
}
Write-Host "`n完了しました。WSL は次回 wsl コマンド実行時に自動で起動します。" -ForegroundColor Cyan

# ---------------------------------------------------------------- 完了通知ダイアログ
$message = @"
WSL2 仮想ディスクの圧縮が完了しました。

C: ドライブ空き容量
  圧縮前: $freeBefore GB
  圧縮後: $freeAfter GB
  増加量: +$increased GB

WSL は次回実行時に自動で起動します。
"@
[System.Windows.Forms.MessageBox]::Show($message, "WSL2 圧縮完了", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null

# ---------------------------------------------------------------- 終了待ち
# Enter キーが押されたらコンソールウィンドウを閉じる。
# -NoExit やショートカット経由で起動した場合、exit ではスクリプトが終わるだけで
# ウィンドウが残るため、[Environment]::Exit でホストプロセスごと終了させる
Read-Host "`nEnter キーを押すとウィンドウを閉じます" | Out-Null
[Environment]::Exit(0)
