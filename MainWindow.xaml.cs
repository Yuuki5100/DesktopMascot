using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using DesktopMascot.Services;

namespace DesktopMascot;

public partial class MainWindow : Window
{
    private const double CharacterWidth = 128;
    private const double WindowWidth = 320;
    private const double SpeechBubbleAreaHeight = 48;
    private const double StatusOptionsAreaHeight = 38;
    private static readonly TimeSpan StatusOptionsFadeDuration = TimeSpan.FromMilliseconds(200);
    private static readonly string[] StatusMessages =
    {
        "お手洗い中",
        "お昼休み中",
        "予備コメント"
    };
    private readonly MonitorService _monitorService = new();
    private readonly CharacterSpriteService _spriteService = new();
    private readonly CharacterService _characterService;
    private readonly SettingsService _settingsService = new();
    private readonly ApplicationLauncherService _launcherService = new();
    private readonly CacheCleanupService _cacheCleanupService = new();
    private MovementService? _movementService;
    private SpriteAnimationService? _animationService;
    private readonly AiHayatoIdleAnimationService _aiHayatoIdleAnimationService;
    private AppSettings _settings = new();
    private Point _dragStartScreenPosition;
    private double _dragStartWindowLeft;
    private double _dragStartWindowTop;
    private bool _mouseDownForDrag;
    private bool _isDragging;
    private bool _statusOptionsAreOpen;
    private bool _statusOptionsAreaExpanded;
    private double _baseWindowHeight;

    public MainWindow()
    {
        InitializeComponent();
        _characterService = new CharacterService();
        _aiHayatoIdleAnimationService = new AiHayatoIdleAnimationService(
            SetCharacterImage);
        _settings = _settingsService.Load();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var animationMode = ParseAnimationMode(_settings.AnimationMode);
        var characterType = ParseCharacterType(_settings.CharacterType);
        _characterService.SetCharacter(characterType);
        if (_characterService.CurrentCharacter != characterType)
        {
            _settings.CharacterType = _characterService.CurrentCharacter.ToString();
            _settingsService.Save(_settings);
        }

        SetCharacterImage(_characterService.CurrentCharacter == CharacterType.AiHayato
            ? _aiHayatoIdleAnimationService.OpenImage
            : _spriteService.GetCurrentFrame());
        Width = WindowWidth;
        double characterHeight;
        if (CharacterImage.Source is System.Windows.Media.Imaging.BitmapSource frame)
        {
            characterHeight = CharacterWidth * frame.PixelHeight / frame.PixelWidth;
        }
        else
        {
            characterHeight = CharacterWidth;
        }

        CharacterImage.Height = characterHeight;
        Height = characterHeight + SpeechBubbleAreaHeight;
        _baseWindowHeight = Height;
        StatusOptionsList.ItemsSource = StatusMessages;
        ShowSpeechBubble(_settings.SpeechMessage);
        RestoreWindowPosition();

        _animationService = new SpriteAnimationService(
            _spriteService,
            SetCharacterImage);
        _movementService = new MovementService(this, _monitorService, CharacterWidth);
        _movementService.DirectionChanged += _animationService.SetDirection;

        ApplyCharacterMenuState();
        ApplyAnimationMode(animationMode, startMovement: true);
        EnsureAiHayatoIdleAnimationRunning();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _movementService?.Dispose();
        _animationService?.Dispose();
        _aiHayatoIdleAnimationService.Dispose();
        _monitorService.Dispose();
    }

    private void CharacterImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        LogDrag("MouseDown");
        if (_animationService?.CurrentMode != AnimationMode.Idle)
        {
            LogDrag("Idle = false");
            return;
        }

        LogDrag("Idle = true");
        _mouseDownForDrag = true;
        _isDragging = false;
        _dragStartScreenPosition = GetCursorPositionOnDesktop();
        _dragStartWindowLeft = Left;
        _dragStartWindowTop = Top;
        CharacterImage.CaptureMouse();
        System.Diagnostics.Debug.WriteLine("[Drag] Start");
        e.Handled = true;
    }

    private void CharacterImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDownForDrag || _animationService?.CurrentMode != AnimationMode.Idle)
        {
            return;
        }

        var currentScreenPosition = GetCursorPositionOnDesktop();
        var deltaX = currentScreenPosition.X - _dragStartScreenPosition.X;
        var deltaY = currentScreenPosition.Y - _dragStartScreenPosition.Y;
        LogDrag($"MouseMove dx={deltaX:0.0} dy={deltaY:0.0}");
        if (!_isDragging
            && Math.Abs(deltaX) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(deltaY) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (!_isDragging)
        {
            _isDragging = true;
            LogDrag("Start");
            _animationService?.Pause();
            _aiHayatoIdleAnimationService.Pause();
        }

        SetWindowPositionClamped(_dragStartWindowLeft + deltaX, _dragStartWindowTop + deltaY);
        e.Handled = true;
    }

    private void CharacterImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        LogDrag("MouseUp");
        var wasDragging = _isDragging;
        _mouseDownForDrag = false;
        _isDragging = false;
        if (CharacterImage.IsMouseCaptured)
        {
            CharacterImage.ReleaseMouseCapture();
        }

        if (wasDragging)
        {
            SaveWindowPosition();
            _aiHayatoIdleAnimationService.Resume();
            EnsureAiHayatoIdleAnimationRunning();
            System.Diagnostics.Debug.WriteLine("[Drag] End");
            e.Handled = true;
            return;
        }

        if (MascotMenu.IsOpen)
        {
            return;
        }

        MascotMenu.PlacementTarget = CharacterImage;
        MascotMenu.IsOpen = true;
        e.Handled = true;
    }

    private void RestoreWindowPosition()
    {
        if (_settings.WindowLeft is double left && _settings.WindowTop is double top)
        {
            SetWindowPositionClamped(
                left - GetCharacterHorizontalOffset(),
                top - SpeechBubbleAreaHeight);
            return;
        }

        var bounds = _monitorService.GetVirtualDesktopBounds();
        SetWindowPositionClamped(bounds.Left, bounds.Bottom - Height - 20);
    }

    private void SetWindowPositionClamped(double left, double top)
    {
        var bounds = _monitorService.GetVirtualDesktopBounds();
        var visibleMargin = 20.0;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var minLeft = bounds.Left - width + visibleMargin;
        var maxLeft = bounds.Right - visibleMargin;
        var minTop = bounds.Top - height + visibleMargin;
        var maxTop = bounds.Bottom - visibleMargin;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        Left = Math.Round(Math.Clamp(left, minLeft, maxLeft) * dpi.DpiScaleX) / dpi.DpiScaleX;
        Top = Math.Round(Math.Clamp(top, minTop, maxTop) * dpi.DpiScaleY) / dpi.DpiScaleY;
        LogDrag($"Window Left={Left:0.0} Top={Top:0.0}");
    }

    private Point GetCursorPositionOnDesktop()
    {
        if (!GetCursorPos(out var cursorPosition))
        {
            return Mouse.GetPosition(null);
        }

        var positionInWindow = PointFromScreen(new Point(cursorPosition.X, cursorPosition.Y));
        return new Point(Left + positionInWindow.X, Top + positionInWindow.Y);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private void SaveWindowPosition()
    {
        _settings.WindowLeft = Left + GetCharacterHorizontalOffset();
        _settings.WindowTop = Top + SpeechBubbleAreaHeight;
        _settingsService.Save(_settings);
        LogDrag("Saved position");
    }

    private double GetCharacterHorizontalOffset()
        => Math.Max(0, ((ActualWidth > 0 ? ActualWidth : Width) - CharacterWidth) / 2);

    public void ShowSpeechBubble(string message)
    {
        SetSpeechText(message);
        SpeechBubblePanel.Visibility = Visibility.Visible;
    }

    public void HideSpeechBubble()
        => SpeechBubblePanel.Visibility = Visibility.Collapsed;

    public void SetSpeechText(string message)
    {
        SpeechText.Text = message;
        if (!string.Equals(_settings.SpeechMessage, message, StringComparison.Ordinal))
        {
            _settings.SpeechMessage = message;
            _settingsService.Save(_settings);
        }
    }

    private void SpeechBubble_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleStatusOptions();
    }

    private void StatusOption_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is string message)
        {
            SelectStatusMessage(message);
        }
    }

    private void ToggleStatusOptions()
    {
        if (_statusOptionsAreOpen)
        {
            HideStatusOptions();
        }
        else
        {
            ShowStatusOptions();
        }
    }

    private void ShowStatusOptions()
    {
        _statusOptionsAreOpen = true;
        SetStatusOptionsAreaExpanded(true);
        StatusOptionsPanel.Visibility = Visibility.Visible;
        _movementService?.Pause();

        var animation = new DoubleAnimation(
            StatusOptionsPanel.Opacity,
            1,
            StatusOptionsFadeDuration);
        StatusOptionsPanel.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void HideStatusOptions()
    {
        if (!_statusOptionsAreOpen)
        {
            return;
        }

        _statusOptionsAreOpen = false;
        var animation = new DoubleAnimation(
            StatusOptionsPanel.Opacity,
            0,
            StatusOptionsFadeDuration);
        animation.Completed += (_, _) =>
        {
            if (_statusOptionsAreOpen)
            {
                return;
            }

            StatusOptionsPanel.BeginAnimation(UIElement.OpacityProperty, null);
            StatusOptionsPanel.Opacity = 0;
            StatusOptionsPanel.Visibility = Visibility.Collapsed;
            SetStatusOptionsAreaExpanded(false);
            ResumeCurrentAnimationMode();
            EnsureAiHayatoIdleAnimationRunning();
        };
        StatusOptionsPanel.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void SelectStatusMessage(string message)
    {
        SetSpeechText(message);
        HideStatusOptions();
    }

    private void SetStatusOptionsAreaExpanded(bool expanded)
    {
        if (_statusOptionsAreaExpanded == expanded)
        {
            return;
        }

        _statusOptionsAreaExpanded = expanded;
        var height = expanded ? StatusOptionsAreaHeight : 0;
        StatusOptionsRow.Height = new GridLength(height);
        Height = _baseWindowHeight + height;
        UpdateLayout();
        SetWindowPositionClamped(Left, Top + (expanded ? -height : height));
    }

    private static void LogDrag(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[Drag] {message}");
        Console.WriteLine($"[Drag] {message}");
    }

    private void MascotMenu_Opened(object sender, RoutedEventArgs e)
    {
        _movementService?.Pause();
        _animationService?.Pause();
        _aiHayatoIdleAnimationService.Pause();
    }

    private void MascotMenu_Closed(object sender, RoutedEventArgs e)
    {
        ResumeCurrentAnimationMode();
        EnsureAiHayatoIdleAnimationRunning();
    }

    private void LaunchEdge_Click(object sender, RoutedEventArgs e)
        => LaunchApplication(_launcherService.LaunchEdge(), "Microsoft Edge");

    private void LaunchVsCode_Click(object sender, RoutedEventArgs e)
        => LaunchApplication(_launcherService.LaunchVsCode(), "VS Code");

    private void LaunchWezTerm_Click(object sender, RoutedEventArgs e)
        => LaunchApplication(_launcherService.LaunchWezTerm(), "WezTerm");

    private void CompactWslDisk_Click(object sender, RoutedEventArgs e)
        => LaunchApplication(_launcherService.LaunchCompactWslDisk(), "WSLディスク圧縮");

    private void RemoveStaleWorktree_Click(object sender, RoutedEventArgs e)
        => LaunchApplication(_launcherService.LaunchRemoveStaleWorktree(), "worktree削除");

    private static void LaunchApplication(LaunchResult result, string displayName)
    {
        if (!result.Success)
        {
            MessageBox.Show(
                $"{displayName}を起動できませんでした。\n\n{result.Message}",
                "DesktopMascot",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ClearVsCodeCache_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("VS Codeのキャッシュを削除しますか？\n\nVS Codeを終了してから実行することを推奨します。"))
        {
            return;
        }

        ShowCleanupResult(_cacheCleanupService.ClearVsCodeCache(), "VS Code");
    }

    private void ClearTeamsCache_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Teamsのキャッシュを削除しますか？\n\nTeamsを終了してから実行することを推奨します。"))
        {
            return;
        }

        ShowCleanupResult(_cacheCleanupService.ClearTeamsCache(), "Teams");
    }

    private void IdleAnimationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        IdleAnimationMenuItem.IsChecked = true;
        WalkAnimationMenuItem.IsChecked = false;

        ApplyAnimationMode(AnimationMode.Idle, startMovement: false);
        SaveAnimationMode(AnimationMode.Idle);
        _movementService?.Pause();

        // TODO: 後で待機アニメーションを紐づける
    }

    private void WalkAnimationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        IdleAnimationMenuItem.IsChecked = false;
        WalkAnimationMenuItem.IsChecked = true;

        ApplyAnimationMode(AnimationMode.Walk, startMovement: true);
        SaveAnimationMode(AnimationMode.Walk);
    }

    private void AiHayatoCharacterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _characterService.SetCharacter(CharacterType.AiHayato);
        _settings.CharacterType = CharacterType.AiHayato.ToString();
        _settingsService.Save(_settings);
        ApplyCharacterMenuState();
        SetCharacterImage(_aiHayatoIdleAnimationService.OpenImage);
        ApplyAnimationMode(ParseAnimationMode(_settings.AnimationMode), startMovement: false);
    }

    private void PompompurinCharacterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _characterService.SetCharacter(CharacterType.Pompompurin);
        _settings.CharacterType = CharacterType.Pompompurin.ToString();
        _settingsService.Save(_settings);
        ApplyCharacterMenuState();
        SetCharacterImage(_spriteService.GetCurrentFrame());
        ApplyAnimationMode(ParseAnimationMode(_settings.AnimationMode), startMovement: false);

        // Existing sprite assets are the Pompompurin character assets.
        // Keep the current animation mode and timers unchanged.
    }

    private void ApplyCharacterMenuState()
    {
        PompompurinCharacterMenuItem.IsChecked =
            _characterService.CurrentCharacter == CharacterType.Pompompurin;
        AiHayatoCharacterMenuItem.IsChecked =
            _characterService.CurrentCharacter == CharacterType.AiHayato;
    }

    private void ResumeCurrentAnimationMode()
    {
        if (_statusOptionsAreOpen)
        {
            _movementService?.Pause();
            return;
        }

        if (_animationService?.CurrentMode != AnimationMode.Walk)
        {
            if (_characterService.CurrentCharacter == CharacterType.AiHayato)
            {
                EnsureAiHayatoIdleAnimationRunning();
            }

            return;
        }

        if (_characterService.CurrentCharacter == CharacterType.AiHayato)
        {
            // AIハヤトのWalk素材は未実装のため、AIハヤトの静止画を維持する。
            _aiHayatoIdleAnimationService.Stop();
            _animationService.Pause();
            _movementService?.Pause();
            return;
        }

        _aiHayatoIdleAnimationService.Stop();
        _movementService?.Resume();
        _animationService.StartWalk();
    }

    private void ApplyAnimationMode(AnimationMode mode, bool startMovement)
    {
        if (_animationService is null || _movementService is null)
        {
            return;
        }

        if (mode == AnimationMode.Walk)
        {
            _aiHayatoIdleAnimationService.Stop();

            if (_characterService.CurrentCharacter == CharacterType.AiHayato)
            {
                // AIハヤトのWalk素材が追加されるまでは、キャラクターを保ったまま静止画で待機する。
                _animationService.Pause();
                _movementService.Pause();
                SetCharacterImage(_aiHayatoIdleAnimationService.OpenImage);
                WalkAnimationMenuItem.IsChecked = true;
                IdleAnimationMenuItem.IsChecked = false;
                return;
            }

            _animationService.StartWalk();
            if (startMovement && !_statusOptionsAreOpen)
            {
                _movementService.Resume();
            }

            WalkAnimationMenuItem.IsChecked = true;
            IdleAnimationMenuItem.IsChecked = false;
            return;
        }

        _animationService.StartIdle();
        _movementService.Pause();
        if (_characterService.CurrentCharacter == CharacterType.AiHayato)
        {
            EnsureAiHayatoIdleAnimationRunning();
        }
        else
        {
            _aiHayatoIdleAnimationService.Stop();
        }
        WalkAnimationMenuItem.IsChecked = false;
        IdleAnimationMenuItem.IsChecked = true;
    }

    private void EnsureAiHayatoIdleAnimationRunning()
    {
        if (_characterService.CurrentCharacter != CharacterType.AiHayato
            || _animationService?.CurrentMode != AnimationMode.Idle)
        {
            System.Diagnostics.Debug.WriteLine("[AiHayatoIdle] Running = false");
            return;
        }

        if (!_aiHayatoIdleAnimationService.IsRunning)
        {
            _aiHayatoIdleAnimationService.Start();
        }

        System.Diagnostics.Debug.WriteLine(
            $"[AiHayatoIdle] Running = {_aiHayatoIdleAnimationService.IsRunning}");
    }

    private void SetCharacterImage(System.Windows.Media.ImageSource image)
    {
        if (!CharacterImage.Dispatcher.CheckAccess())
        {
            CharacterImage.Dispatcher.Invoke(() => SetCharacterImage(image));
            return;
        }

        CharacterImage.Source = image;
        var frameName = ReferenceEquals(image, _aiHayatoIdleAnimationService.OpenImage)
            ? "AI-Hayato OPEN"
            : ReferenceEquals(image, _aiHayatoIdleAnimationService.ClosedImage)
                ? "AI-Hayato CLOSED"
                : "Pompompurin/WALK";
        LogImage($"[Image] Source changed: {frameName} ({System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(image)})");
    }

    private static void LogImage(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        Console.WriteLine(message);
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopMascot");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "debug.log"),
                $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Diagnostics must never affect rendering.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnostics must never affect rendering.
        }
    }

    private void SaveAnimationMode(AnimationMode mode)
    {
        _settings.AnimationMode = mode.ToString();
        _settingsService.Save(_settings);
    }

    private static AnimationMode ParseAnimationMode(string? value)
        => Enum.TryParse<AnimationMode>(value, ignoreCase: true, out var mode)
            && Enum.IsDefined(mode)
            ? mode
            : AnimationMode.Walk;

    private static CharacterType ParseCharacterType(string? value)
        => Enum.TryParse<CharacterType>(value, ignoreCase: true, out var character)
            && Enum.IsDefined(character)
            ? character
            : CharacterType.Pompompurin;

    private static bool Confirm(string message)
        => MessageBox.Show(
            message,
            "DesktopMascot",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    private static void ShowCleanupResult(CleanupResult result, string displayName)
    {
        if (result.Success)
        {
            MessageBox.Show(
                result.NothingToDelete
                    ? $"{displayName}の削除対象キャッシュはありませんでした。"
                    : $"{displayName}のキャッシュを削除しました。",
                "DesktopMascot",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        MessageBox.Show(
            $"{displayName}のキャッシュを完全に削除できませんでした。\n\n{result.Message}\n\n{displayName}を終了してから再度実行してください。",
            "DesktopMascot",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ExitMascot_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("DesktopMascotを終了しますか？"))
        {
            return;
        }

        Application.Current.Shutdown();
    }
}
