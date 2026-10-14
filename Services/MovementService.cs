using System.Windows;
using System.Windows.Threading;

namespace DesktopMascot.Services;

public sealed class MovementService : IDisposable
{
    private const double MovementSpeed = 25.0; // WPF DIPs per second (originally 50)
    private const double PixelsPerTick = 2.0;
    private const double TickIntervalMilliseconds = 1000.0 * PixelsPerTick / MovementSpeed;
    private const double BottomMargin = 20.0;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(TickIntervalMilliseconds);

    private readonly Window _window;
    private readonly MonitorService _monitorService;
    private readonly double _characterWidth;
    private readonly DispatcherTimer _timer;
    private Direction _direction = Direction.Right;
    private bool _paused;

    public bool AlwaysMoveLeft { get; set; }

    public event Action<Direction>? DirectionChanged;

    public MovementService(
        Window window,
        MonitorService monitorService,
        double characterWidth)
    {
        _window = window;
        _monitorService = monitorService;
        _characterWidth = characterWidth;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TickInterval
        };
        _timer.Tick += OnTick;
        _monitorService.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void Start()
    {
        PlaceNearBottom();
        DirectionChanged?.Invoke(_direction);
        _timer.Start();
    }

    public void Pause()
    {
        _paused = true;
        _timer.Stop();
    }

    public void Resume()
    {
        if (_timer.IsEnabled)
        {
            return;
        }

        _paused = false;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_paused)
        {
            return;
        }

        var bounds = _monitorService.GetVirtualDesktopBounds();
        var characterOffset = GetCharacterHorizontalOffset();
        var minX = bounds.Left - characterOffset;
        var maxX = bounds.Right - characterOffset - _characterWidth;

        var nextLeft = _window.Left - PixelsPerTick;

        if (AlwaysMoveLeft)
        {
            // AI-hayato always walks to the left. Wrap around so the walk
            // continues instead of turning around at the screen edge.
            if (nextLeft <= minX)
            {
                nextLeft = maxX;
            }

            _window.Left = nextLeft;
            _window.Top = bounds.Bottom - _window.ActualHeight - BottomMargin;
            return;
        }

        nextLeft = _window.Left + (_direction == Direction.Right ? PixelsPerTick : -PixelsPerTick);

        if (nextLeft >= maxX)
        {
            nextLeft = maxX;
            SetDirection(Direction.Left);
        }
        else if (nextLeft <= minX)
        {
            nextLeft = minX;
            SetDirection(Direction.Right);
        }

        _window.Left = nextLeft;
        _window.Top = bounds.Bottom - _window.ActualHeight - BottomMargin;
    }

    private void SetDirection(Direction direction)
    {
        if (_direction == direction)
        {
            return;
        }

        _direction = direction;
        DirectionChanged?.Invoke(direction);
    }

    private void PlaceNearBottom()
    {
        var bounds = _monitorService.GetVirtualDesktopBounds();
        var height = _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height;
        var characterOffset = GetCharacterHorizontalOffset();
        var minX = bounds.Left - characterOffset;
        var maxX = bounds.Right - characterOffset - _characterWidth;

        if (double.IsNaN(_window.Left) || _window.Left < minX || _window.Left > maxX)
        {
            _window.Left = Math.Max(minX, Math.Min(0, maxX));
        }

        _window.Top = bounds.Bottom - height - BottomMargin;
    }

    private double GetCharacterHorizontalOffset()
    {
        var width = _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
        return Math.Max(0, (width - _characterWidth) / 2);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _window.Dispatcher.Invoke(PlaceNearBottom);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        _monitorService.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }
}
