using System.Windows;
using System.Windows.Input;
using DesktopMascot.Services;

namespace DesktopMascot;

public partial class MainWindow : Window
{
    private const double CharacterWidth = 128;
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

    public MainWindow()
    {
        InitializeComponent();
        _characterService = new CharacterService();
        _aiHayatoIdleAnimationService = new AiHayatoIdleAnimationService(
            SetCharacterImage);
        _settings = _settingsService.Load();
        RestoreWindowPosition();
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
        Width = CharacterWidth;
        if (CharacterImage.Source is System.Windows.Media.Imaging.BitmapSource frame)
        {
            Height = CharacterWidth * frame.PixelHeight / frame.PixelWidth;
        }

        _animationService = new SpriteAnimationService(
            _spriteService,
            SetCharacterImage);
        _movementService = new MovementService(this, _monitorService);
        _movementService.DirectionChanged += _animationService.SetDirection;

        ApplyCharacterMenuState();
        ApplyAnimationMode(animationMode, startMovement: true);
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
        if (_animationService?.CurrentMode != AnimationMode.Idle)
        {
            return;
        }

        _mouseDownForDrag = true;
        _isDragging = false;
        _dragStartScreenPosition = CharacterImage.PointToScreen(e.GetPosition(CharacterImage));
        _dragStartWindowLeft = Left;
        _dragStartWindowTop = Top;
        CharacterImage.CaptureMouse();
        e.Handled = true;
    }

    private void CharacterImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDownForDrag || _animationService?.CurrentMode != AnimationMode.Idle)
        {
            return;
        }

        var currentScreenPosition = CharacterImage.PointToScreen(e.GetPosition(CharacterImage));
        var deltaX = currentScreenPosition.X - _dragStartScreenPosition.X;
        var deltaY = currentScreenPosition.Y - _dragStartScreenPosition.Y;
        if (!_isDragging
            && Math.Abs(deltaX) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(deltaY) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDragging = true;
        SetWindowPositionClamped(_dragStartWindowLeft + deltaX, _dragStartWindowTop + deltaY);
        e.Handled = true;
    }

    private void CharacterImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
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
            SetWindowPositionClamped(left, top);
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
        Left = Math.Clamp(left, minLeft, maxLeft);
        Top = Math.Clamp(top, minTop, maxTop);
    }

    private void SaveWindowPosition()
    {
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settingsService.Save(_settings);
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
    }

    private void LaunchEdge_Click(object sender, RoutedEventArgs e)
        => LaunchApplication(_launcherService.LaunchEdge(), "Microsoft Edge");

    private void LaunchVsCode_Click(object sender, RoutedEventArgs e)
        => LaunchApplication(_launcherService.LaunchVsCode(), "VS Code");

    private void LaunchWezTerm_Click(object sender, RoutedEventArgs e)
        => LaunchApplication(_launcherService.LaunchWezTerm(), "WezTerm");

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
        if (_animationService?.CurrentMode != AnimationMode.Walk)
        {
            if (_characterService.CurrentCharacter == CharacterType.AiHayato)
            {
                StartAiHayatoIdleAnimation(resume: true);
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
            if (startMovement)
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
            StartAiHayatoIdleAnimation();
        }
        else
        {
            _aiHayatoIdleAnimationService.Stop();
        }
        WalkAnimationMenuItem.IsChecked = false;
        IdleAnimationMenuItem.IsChecked = true;
    }

    private void StartAiHayatoIdleAnimation(bool resume = false)
    {
        if (_characterService.CurrentCharacter != CharacterType.AiHayato
            || _animationService?.CurrentMode != AnimationMode.Idle)
        {
            return;
        }

        if (resume)
        {
            _aiHayatoIdleAnimationService.Resume();
        }
        else
        {
            _aiHayatoIdleAnimationService.Start();
        }
    }

    private void SetCharacterImage(System.Windows.Media.ImageSource image)
    {
        CharacterImage.Source = image;
        System.Diagnostics.Debug.WriteLine(
            $"CharacterImage.Source updated: {System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(image)}");
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
