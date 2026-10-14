# Compact-WslDisk.ps1

WSL2 の仮想ディスク（`ext4.vhdx`）を圧縮し、削除済み領域を Windows に返却するスクリプト。

WSL2 のディスクは動的拡張のため、**WSL 内でファイルを削除してもディスク上のファイルサイズは縮まない**。
放置すると C: ドライブを一方的に食い潰していくので、定期的にこのスクリプトで回収する。

---

## 使い方

### 1. まず現状確認（何も変更しない・管理者権限も不要）

```powershell
cd $env:USERPROFILE\Desktop
powershell -ExecutionPolicy Bypass -File .\Compact-WslDisk.ps1 -DryRun
```

各ディストロの現在サイズと C: の空き容量が表示されるだけで、WSL は停止しない。

### 2. 本番実行（**管理者 PowerShell** で）

```powershell
cd $env:USERPROFILE\Desktop
powershell -ExecutionPolicy Bypass -File .\Compact-WslDisk.ps1
```

`yes` の入力を求められてから WSL が停止する。誤爆はしない。

サイズが大きいと数十分かかる。時間に余裕のあるときに実行すること。

### オプション

| オプション | 説明 |
|---|---|
| `-DryRun` | 現状を表示するだけ。何も変更しない |
| `-Distro Ubuntu` | 対象を 1 つに限定する |
| `-SkipTrim` | fstrim をスキップ（WSL 内で実行済みの場合） |
| `-SetSparse` | 圧縮後に sparse 属性を有効化する。**現在は必ず失敗する**（後述） |

---

## 実行中の注意

### WSL を起動してはいけない

圧縮中に WSL が `ext4.vhdx` を掴むと、`Optimize-VHD` が「別のプロセスが使用中」で失敗する。
データが壊れることはないが、その回の圧縮が無駄になる。

`wsl` と打たなくても、以下は勝手に WSL を起動するので事前に閉じておく。

- VS Code の WSL リモートウィンドウ
- エクスプローラーで開いている `\wsl$\` / `\wsl.localhost\`
- WSL 上のパスを見ている Windows Terminal のタブ
- Docker Desktop の WSL2 バックエンド（※このマシンには未登録なので現状は無関係）

### Windows 側の作業は問題ない

ブラウザ、エディタ、Windows 側の git 作業などは一切干渉しない。ただし以下は避ける。

- **大量のディスク書き込み**（大きなダウンロード、ビルド、イメージ取得）。圧縮中に空き容量を食い潰すと失敗する
- **スリープ・休止**。長時間放置になるので途中で中断されないよう電源設定を確認する
- 重いビルドやウイルス対策のフルスキャンとの同時実行。ディスク I/O を奪い合って両方遅くなる

---

## スクリプトの動作

1. **fstrim** を各ディストロで実行 — 削除済みブロックを仮想ディスク層に通知する。
   これを飛ばすと次の圧縮がほぼ効かない
2. **`wsl --shutdown`** — 実行直前に `yes` の入力を求める
3. **VHDX を圧縮** — `Optimize-VHD` があればそれを、無ければ `diskpart` の `compact vdisk` に自動フォールバック
4. **sparse 属性の有効化**（`-SetSparse` 指定時）— 現在は WSL 側の制限で失敗する
5. **完了通知** — 空き容量の増加量をダイアログで表示する。OK を押したあと、
   コンソールで **Enter を押すとウィンドウが閉じる**（`-NoExit` 付きで起動していても閉じる）

管理者権限不足・ディストロ未検出などで**起動に失敗した場合は、エラーダイアログを表示**して終了する
（OK を押すまで待つ）。各ディストロの圧縮失敗はダイアログを出さず、コンソールに表示して次へ進む。

圧縮は VHDX のその場で行われるため、圧縮対象と同じだけの空き容量は不要。空きが数 GB しかない状態でも動く。

---

## sparse 属性について（重要）

本来は `--set-sparse true` を一度有効化すれば、以降 WSL が自動で領域を返すようになり、
**このスクリプト自体が不要になる**。

しかし現行の WSL では次のエラーで失敗する。

```
スパース VHD のサポートは、データの破損の可能性があるため、現在無効になっています。
ディストリビューションでスパース VHD を強制的に使用するには、次を実行してください。
wsl.exe --manage <DistributionName> --set-sparse --allow-unsafe
エラー コード: Wsl/Service/E_INVALIDARG
```

Microsoft が**データ破損の可能性を理由に、この機能を既定で無効化している**。

### `--allow-unsafe` は使わないこと

強制的に有効化することは可能だが、**非推奨**。

Microsoft が出荷済みの機能をわざわざ塞いだ理由が「データ破損の可能性」であり、
壊れる対象は WSL 上の作業内容そのもの。
得られるものは「数ヶ月に一度スクリプトを流す手間が消える」だけなので、割に合わない。

### `.wslconfig` でも回避できない

`.wslconfig` の `[experimental] sparseVhd = true` は**新規作成するディストロ**にしか効かず、
既存のディストロには適用されない。しかも同じ安全装置に引っかかる。

---

## 「卒業」の確認方法

WSL の更新で sparse の不具合が解消されれば、また普通に使えるようになる可能性がある。
以下の手順で、たまに様子を見るとよい。

```powershell
# 1. WSL を最新に更新
wsl --update

# 2. バージョンを確認（記録しておくと差分が分かる）
wsl --version

# 3. 管理者 PowerShell で -SetSparse 付きで実行
cd $env:USERPROFILE\Desktop
powershell -ExecutionPolicy Bypass -File .\Compact-WslDisk.ps1 -SetSparse
```

### 判定

- **`sparse 有効化に成功` と表示されたら卒業。** 以降 WSL が自動で領域を返すので、
  このスクリプトの定期実行は不要になる
- **今までどおり `sparse 有効化に失敗` なら、まだ塞がれたまま。**
  `-SetSparse` を外した通常運用を続ける

失敗しても圧縮処理自体は正常に完了しているので、実行が無駄になることはない。
次回の圧縮ついでに毎回 `-SetSparse` を付けて様子見する、という運用でも構わない。

---

## 運用の目安

`-SetSparse` は現状必ず失敗するので、通常は付けずに実行する。

```powershell
cd $env:USERPROFILE\Desktop
powershell -ExecutionPolicy Bypass -File .\Compact-WslDisk.ps1
```

頻度は WSL での作業量次第だが、**数ヶ月に一度**、または **C: の空きが 30 GB を切ったら**、
くらいの感覚で十分。

WSL 内で不要なファイルを削除したあとに流すと、より多く回収できる。
`du -sh /*` や `docker system prune` などで当たりを付けてから実行すると効率がよい。

---

## トラブルシューティング

### `用語 '.\Compact-WslDisk.ps1' は認識されません`

カレントディレクトリがスクリプトの場所と違う。`cd $env:USERPROFILE\Desktop` してから実行する。

### `このシステムではスクリプトの実行が無効になっています`

実行ポリシーの制限。`powershell -ExecutionPolicy Bypass -File .\...` の形で実行する。

### `管理者権限が必要です`

エラーダイアログで表示される。OK を押して閉じ、PowerShell を「管理者として実行」で開き直す。

### `wsl: Failed to start the systemd user session for 'root'`

警告であって失敗ではない。fstrim 自体は正常に完了している。無視してよい。

### 圧縮が「別のプロセスが使用中」で失敗する

WSL が起動している。VS Code の WSL ウィンドウやエクスプローラーを閉じてから再実行する。

### 途中で失敗しても

スクリプトはエラーを捕捉して次に進むだけなので、もう一度実行すれば問題ない。

---

# Remove-StaleWorktree.ps1

最終操作から一定日数（既定 7 日）以上たった **git worktree を自動で削除**するスクリプト。
放置された worktree（Claude Code が作る `.claude/worktrees/` 配下のものを含む）がディスクを食うのを防ぐ。

## 使い方

```powershell
# 判定結果を見るだけ（何も変更しない）
powershell -ExecutionPolicy Bypass -File .\Remove-StaleWorktree.ps1 -DryRun

# yes/no を確認してから削除
powershell -ExecutionPolicy Bypass -File .\Remove-StaleWorktree.ps1

# 14 日以上放置されたものを確認なしで削除（タスクスケジューラ向け）
powershell -ExecutionPolicy Bypass -File .\Remove-StaleWorktree.ps1 -Days 14 -Yes
```

管理者権限は不要。

### オプション

| オプション | 説明 |
|---|---|
| `-DryRun` | 判定結果を表示するだけ。何も変更しない |
| `-Days 14` | 削除対象とする経過日数。既定 7 |
| `-Root C:\path` | 走査するフォルダ。既定はこのスクリプトを含むリポジトリの 1 つ上（`...\Git`） |
| `-IncludeDirty` | 未コミットの変更がある worktree も削除する（**変更は失われる**） |
| `-Yes` | 確認プロンプトを省略する |

## 判定方法

git は worktree ごとに管理フォルダ `.git/worktrees/<名前>/` を持つ。その中の
`index`・`HEAD`・`logs/HEAD` は checkout・add・commit などで更新されるため、
**この 3 つの更新日時の最大値を「最終操作日時」**とし、`-Days` 日以上前なら削除対象とする。

以下は削除しない。

- メイン worktree（リポジトリ本体）
- `git worktree lock` されているもの
- 未コミットの変更があるもの（`-IncludeDirty` 指定時を除く）

削除は `git worktree remove --force`、フォルダだけ消えて登録が残っているものは `git worktree prune` で片付ける。
**ブランチは削除しない**ので、コミット済みの内容は失われない。

### 注意

エディタでファイルを編集しただけ（git 操作なし）では最終操作日時は更新されない。
ただしその場合は未コミットの変更が残るので、`-IncludeDirty` を付けない限り削除されない。

## 完了・失敗の通知

完了時は削除件数をダイアログで表示する。OK を押したあと、コンソールで **Enter を押すとウィンドウが閉じる**
（`-Yes` 指定時は Enter を待たずに閉じる）。フォルダ未検出・git 未インストールなどで
起動に失敗した場合はエラーダイアログを表示して終了する（OK を押すまで待つ）。

---

## 実行履歴

| 日付 | C: 空き | Ubuntu | Ubuntu-24.04 | 備考 |
|---|---|---|---|---|
| 2026-09-14 | 13.72 GB → **131.37 GB** | 223.57 → 106.12 GB | 8.09 → 8.01 GB | +117.46 GB 回収。sparse は失敗（WSL 2.6.1.0） |
