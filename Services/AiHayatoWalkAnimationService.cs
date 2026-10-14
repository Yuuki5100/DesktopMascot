using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopMascot.Services;

public sealed class AiHayatoWalkAnimationService : IDisposable
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1);
    private static readonly Regex NumberPattern = new(@"\d+", RegexOptions.Compiled);
    private readonly Action<ImageSource> _setImage;
    private readonly IReadOnlyList<ImageSource> _walkFrames;
    private readonly DispatcherTimer _timer;
    private int _currentFrameIndex;

    public bool IsRunning => _timer.IsEnabled;

    public AiHayatoWalkAnimationService(Action<ImageSource> setImage)
    {
        _setImage = setImage;
        _walkFrames = LoadWalkFrames();
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
        {
            Interval = FrameInterval
        };
        _timer.Tick += OnTick;
        Log($"[AiHayatoWalk] Frames loaded: {_walkFrames.Count}");
    }

    public void Start()
    {
        if (_timer.IsEnabled)
        {
            return;
        }

        if (_walkFrames.Count == 0)
        {
            Log("[AiHayatoWalk] No walk frames found.");
            return;
        }

        _currentFrameIndex = 0;
        _setImage(_walkFrames[0]);
        Log("[AiHayatoWalk] Start");
        Log("[AiHayatoWalk] Frame 0");
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _currentFrameIndex = 0;
        Log("[AiHayatoWalk] Stop");
    }

    public void Pause() => _timer.Stop();

    public void Resume()
    {
        if (_walkFrames.Count > 0 && !_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_walkFrames.Count == 0)
        {
            _timer.Stop();
            return;
        }

        _currentFrameIndex = (_currentFrameIndex + 1) % _walkFrames.Count;
        _setImage(_walkFrames[_currentFrameIndex]);
        Log($"[AiHayatoWalk] Frame {_currentFrameIndex}");
    }

    private static IReadOnlyList<ImageSource> LoadWalkFrames()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Assets", "AI-hayato", "walk");
        if (!Directory.Exists(directory))
        {
            return Array.Empty<ImageSource>();
        }

        var paths = Directory.EnumerateFiles(directory)
            .Where(path => IsSupportedImage(path))
            .OrderBy(path => Path.GetFileName(path), NaturalFileNameComparer.Instance)
            .ToList();
        var frames = new List<ImageSource>(paths.Count);
        foreach (var path in paths)
        {
            var frame = TryLoadImage(path);
            if (frame is not null)
            {
                frames.Add(frame);
            }
            else
            {
                Log($"[AiHayatoWalk] Failed to load: {path}");
            }
        }

        return frames;
    }

    private static bool IsSupportedImage(string path)
        => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static ImageSource? TryLoadImage(string path)
    {
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
            return null;
        }
    }

    private static void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        Console.WriteLine(message);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private sealed class NaturalFileNameComparer : IComparer<string>
    {
        public static readonly NaturalFileNameComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var xParts = NumberPattern.Split(x ?? string.Empty);
            var yParts = NumberPattern.Split(y ?? string.Empty);
            var xNumbers = NumberPattern.Matches(x ?? string.Empty);
            var yNumbers = NumberPattern.Matches(y ?? string.Empty);
            for (var i = 0; i < Math.Min(xParts.Length, yParts.Length); i++)
            {
                var comparison = StringComparer.OrdinalIgnoreCase.Compare(xParts[i], yParts[i]);
                if (comparison != 0) return comparison;
                if (i < xNumbers.Count && i < yNumbers.Count
                    && int.TryParse(xNumbers[i].Value, out var xNumber)
                    && int.TryParse(yNumbers[i].Value, out var yNumber)
                    && xNumber != yNumber)
                {
                    return xNumber.CompareTo(yNumber);
                }
            }

            return StringComparer.OrdinalIgnoreCase.Compare(x, y);
        }
    }
}
