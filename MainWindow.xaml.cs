using System.Windows;
using System.Windows.Input;
using DesktopMascot.Services;

namespace DesktopMascot;

public partial class MainWindow : Window
{
    private const double CharacterWidth = 128;
    private readonly MonitorService _monitorService = new();
    private readonly CharacterSpriteService _spriteService = new();
    private readonly ApplicationLauncherService _launcherService = new();
    private readonly CacheCleanupService _cacheCleanupService = new();
    private MovementService? _movementService;
    private SpriteAnimationService? _animationService;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CharacterImage.Source = _spriteService.GetCurrentFrame();
        Width = CharacterWidth;
        if (CharacterImage.Source is System.Windows.Media.Imaging.BitmapSource frame)
        {
            Height = CharacterWidth * frame.PixelHeight / frame.PixelWidth;
        }

        _animationService = new SpriteAnimationService(
            _spriteService,
            frame => CharacterImage.Source = frame);
        _movementService = new MovementService(this, _monitorService);
        _movementService.DirectionChanged += _animationService.SetDirection;

        _movementService.Start();
        _animationService.Resume();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _movementService?.Dispose();
        _animationService?.Dispose();
        _monitorService.Dispose();
    }

    private void CharacterImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (MascotMenu.IsOpen)
        {
            return;
        }

        MascotMenu.PlacementTarget = CharacterImage;
        MascotMenu.IsOpen = true;
        e.Handled = true;
    }

    private void MascotMenu_Opened(object sender, RoutedEventArgs e)
    {
        _movementService?.Pause();
        _animationService?.Pause();
    }

    private void MascotMenu_Closed(object sender, RoutedEventArgs e)
    {
        _movementService?.Resume();
        _animationService?.Resume();
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
