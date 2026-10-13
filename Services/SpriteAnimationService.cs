using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopMascot.Services;

public sealed class SpriteAnimationService : IDisposable
{
    private const double AnimationFramesPerSecond = 5.0;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000 / AnimationFramesPerSecond);
    private readonly CharacterSpriteService _sprites;
    private readonly Action<ImageSource> _setImage;
    private readonly DispatcherTimer _timer;

    public AnimationMode CurrentMode { get; private set; } = AnimationMode.Walk;

    public SpriteAnimationService(CharacterSpriteService sprites, Action<ImageSource> setImage)
    {
        _sprites = sprites;
        _setImage = setImage;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FrameInterval };
        _timer.Tick += OnTick;
    }

    public void SetMode(AnimationMode mode)
    {
        CurrentMode = mode;
        if (mode == AnimationMode.Idle)
        {
            Stop();
        }
    }

    public void StartWalk()
    {
        SetMode(AnimationMode.Walk);
        Resume();
    }

    public void StartIdle()
    {
        SetMode(AnimationMode.Idle);
    }

    public void Resume() => _timer.Start();

    public void Pause() => _timer.Stop();

    public void Stop()
    {
        _timer.Stop();
    }

    public void SetDirection(Direction direction)
    {
        _setImage(_sprites.SetDirection(direction));
        // Give the first frame a full interval after changing direction.
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            _timer.Start();
        }
    }

    private void OnTick(object? sender, EventArgs e)
        => _setImage(_sprites.AdvanceFrame());

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
