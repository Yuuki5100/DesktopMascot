using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopMascot.Services;

public sealed class AiHayatoIdleAnimationService : IDisposable
{
    private static readonly TimeSpan NormalOpenDuration = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan FastOpenDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan ClosedDuration = TimeSpan.FromMilliseconds(80);
    private readonly Action<ImageSource> _setImage;
    private readonly ImageSource _openImage;
    private readonly ImageSource _closedImage;
    private readonly DispatcherTimer _timer;
    private int _normalBlinkCount;
    private int _fastBlinkCount;
    private bool _eyesClosed;
    private bool _fastPhase;

    public AiHayatoIdleAnimationService(Action<ImageSource> setImage)
    {
        _setImage = setImage;
        _openImage = LoadImage("hayato-wakeup.png");
        _closedImage = LoadImage("hayato-sleep.png");
        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += OnTick;
        ResetCycle();
    }

    public ImageSource OpenImage => _openImage;

    public void Start()
    {
        ResetCycle();
        _timer.Start();
    }

    public void Pause() => _timer.Stop();
    public void Resume() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        ResetCycle();
    }

    private void ResetCycle()
    {
        _normalBlinkCount = 0;
        _fastBlinkCount = 0;
        _fastPhase = false;
        _eyesClosed = false;
        _setImage(_openImage);
        _timer.Interval = NormalOpenDuration;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_eyesClosed)
        {
            _setImage(_closedImage);
            _eyesClosed = true;
            _timer.Interval = ClosedDuration;
            return;
        }

        _setImage(_openImage);
        _eyesClosed = false;
        if (!_fastPhase)
        {
            _normalBlinkCount++;
            if (_normalBlinkCount >= 5)
            {
                _fastPhase = true;
                _fastBlinkCount = 0;
            }
        }
        else
        {
            _fastBlinkCount++;
            if (_fastBlinkCount >= 2)
            {
                _fastPhase = false;
                _normalBlinkCount = 0;
            }
        }

        _timer.Interval = _fastPhase ? FastOpenDuration : NormalOpenDuration;
    }

    private static ImageSource LoadImage(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "AI-hayato", "building", fileName);
        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            return CreateTransparentFallback();
        }
    }

    private static ImageSource CreateTransparentFallback()
    {
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        source.Freeze();
        return source;
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
