# DesktopMascot

Windows 11 / C# / WPF / .NET 8 で動く軽量デスクトップマスコットです。Visual Studio は不要で、VS Code + .NET CLI だけで開発・実行できます。

## 必要環境

- Windows 11
- .NET 8 SDK
- VS Code（任意）

.NET SDK が未導入の場合:

```powershell
winget install Microsoft.DotNet.SDK.8
```

インストール後は VS Code / PowerShell を再起動して確認します。

```powershell
dotnet --version
```

## ビルド / 実行

```powershell
cd C:\Git\DesktopMascot
dotnet restore
dotnet build
dotnet run
```

## 実装済み

- 透明・枠なしウィンドウ
- 常に最前面
- 仮想デスクトップ全体を左右移動
- 画面端で自動反転
- ゆっくりした移動: 25 DIP/秒（80msごとに2 DIP、元の約50%の速度）
- 画像更新は移動から独立した200ms間隔（5FPS）
- 左右各3枚の個別透過PNGによる往復型歩行アニメーション
- 画像が一部欠けた場合は存在するフレームで再生、方向別に全フレームが欠けた場合は静止画へフォールバック
- 左クリックメニュー
  - Edge起動
  - VS Code起動
  - WezTerm起動
  - VS Codeキャッシュ削除
  - Teamsキャッシュ削除
  - DesktopMascot停止（確認あり）
- メニュー表示中は移動・アニメーション停止
- ディスプレイ構成変更追従
- 将来のクリック透過ON/OFF用サービス

## 歩行画像の差し替え

歩行画像は `Assets/Walk/` に配置します。スプライトシートは使用しません。

```text
Assets/Walk/walk1-right.png
Assets/Walk/walk2-right.png
Assets/Walk/walk3-right.png
Assets/Walk/walk1-left.png
Assets/Walk/walk2-left.png
Assets/Walk/walk3-left.png
```

右向き・左向きそれぞれ次の4ステップを1周期としてループします。折り返しで同じ画像を連続表示せず、2枚目はキャッシュ済みの画像を再利用します。

```text
右向き: walk1-right → walk2-right → walk3-right → walk2-right → ...
左向き: walk1-left  → walk2-left  → walk3-left  → walk2-left  → ...
```

起動時に6枚のPNGを一度だけ読み込み、透過を保持した `BitmapImage` として凍結・キャッシュします。実行中は `Image.Source` を切り替えるだけで、ディスクからの再読み込み・切り出し・左右反転は行いません。方向変更時は対応方向の先頭画像を即座に表示します。メニュー表示中は移動・画像更新の両タイマーを停止し、閉じると再開します。

表示幅は `MainWindow.xaml.cs` の `CharacterWidth`（既存と同じ128 DIP）で変更できます。ウィンドウの高さは起動時の先頭画像の縦横比から一度だけ計算します。固定されたImage表示領域に `Stretch="Uniform"` で表示するため、画像ごとの縦横比を保ち、フレーム切り替えでウィンドウサイズは変わりません。現在の6枚はすべて1254×1254pxです。差し替える素材もキャンバスサイズ・足元の位置・余白を揃えると、歩行中の位置ズレを抑えられます。

移動速度は `MovementService.cs` の `MovementSpeed`、1回の移動量は `PixelsPerTick` で変更できます。現在は25 DIP/秒、80msごとに2 DIPです。WPFのDIPは100%表示倍率で1px相当で、画面の表示倍率に応じて実ピクセル数が変わります。アニメーション速度は `SpriteAnimationService.cs` の `AnimationFramesPerSecond` で独立して変更できます。初期値は5FPS（200ms間隔）、4ステップの1周期は約0.8秒です。停止中の時間をまとめて進めず、方向変更後の先頭画像も200ms表示します。

画像を差し替える場合は、同じファイル名で置き換えて再ビルド・再起動してください。ビルド・発行時にPNGは出力先の `Assets/Walk/` へコピーされます。出力先のPNGを直接置き換えた場合は再起動で反映されます。

画像が一部不足している、または破損している場合は、各方向の読み込めたフレームだけを番号順に往復再生します。1枚だけなら静止画になります。その方向の歩行画像がすべて読み込めない場合は次の既存静止画へフォールバックします。

```text
Assets/character-right.png
Assets/character-left.png
```

静止画も読み込めない場合は透明な1ピクセル画像を使用し、画像欠損でアプリを終了させません。

## キャッシュ削除対象

VS Code:

```text
%APPDATA%\Code\Cache
%APPDATA%\Code\CachedData
```

Teams:

```text
%LocalAppData%\Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams
```

削除前に確認ダイアログを表示します。VS Code / Teams が起動中だとファイルロックで一部削除できない場合があります。

## 停止方法

キャラクターを左クリックして `DesktopMascot停止` を選択してください。確認後、WPF の `Application.Current.Shutdown()` で正常終了します。

PowerShellから強制停止する場合:

```powershell
Stop-Process -Name DesktopMascot -Force
```

## 構成

```text
DesktopMascot/
├─ DesktopMascot.csproj
├─ app.manifest
├─ App.xaml
├─ App.xaml.cs
├─ MainWindow.xaml
├─ MainWindow.xaml.cs
├─ Assets/
│  ├─ character-right.png
│  ├─ character-left.png
│  └─ Walk/
│     ├─ walk1-right.png
│     ├─ walk2-right.png
│     ├─ walk3-right.png
│     ├─ walk1-left.png
│     ├─ walk2-left.png
│     └─ walk3-left.png
└─ Services/
   ├─ ApplicationLauncherService.cs
   ├─ CacheCleanupService.cs
   ├─ CharacterSpriteService.cs
   ├─ Direction.cs
   ├─ MonitorService.cs
   ├─ MovementService.cs
   ├─ SpriteAnimationService.cs
   └─ WindowInteractionService.cs
```

## 補足

現在は仮想デスクトップ全体の下端付近を一直線に移動します。左右配置のマルチモニターを主対象にしています。上下に大きくずれた特殊な配置では、モニターが存在しない仮想座標上を通る瞬間があります。

