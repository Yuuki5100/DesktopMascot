using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopMascot.Services;

public sealed class CharacterSpriteService
{
    private const int FramesPerDirection = 3;
    private readonly IReadOnlyList<ImageSource> _rightFrames;
    private readonly IReadOnlyList<ImageSource> _leftFrames;
    private int _frameIndex;

    public Direction Direction { get; private set; } = Direction.Right;

    public CharacterSpriteService()
    {
        _rightFrames = LoadWalkFrames("right");
        _leftFrames = LoadWalkFrames("left");
    }

    public ImageSource GetCurrentFrame()
    {
        var frames = GetFrames();
        return frames[_frameIndex % frames.Count];
    }

    public ImageSource AdvanceFrame()
    {
        var frames = GetFrames();
        _frameIndex = (_frameIndex + 1) % frames.Count;
        return frames[_frameIndex];
    }

    public ImageSource SetDirection(Direction direction)
    {
        if (Direction != direction)
        {
            Direction = direction;
            _frameIndex = 0;
        }

        return GetCurrentFrame();
    }

    private IReadOnlyList<ImageSource> GetFrames()
        => Direction == Direction.Right ? _rightFrames : _leftFrames;

    private static IReadOnlyList<ImageSource> LoadWalkFrames(string direction)
    {
        var frames = new List<ImageSource>(FramesPerDirection);
        for (var index = 0; index < FramesPerDirection; index++)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Walk", $"walk{index + 1}-{direction}.png");
            var frame = TryLoadFile(path);
            if (frame is not null)
            {
                frames.Add(frame);
            }
        }

        if (frames.Count == 0)
        {
            return LoadFallback($"pack://application:,,,/DesktopMascot;component/Assets/character-{direction}.png");
        }

        // Ping-pong without repeating either endpoint: 1 -> 2 -> 3 -> 2.
        // Reuse the cached objects for the return journey.
        for (var index = frames.Count - 2; index > 0; index--)
        {
            frames.Add(frames[index]);
        }

        return frames;
    }

    private static BitmapSource? TryLoadFile(string path)
    {
        try
        {
            // Decode once and release the file; animation only switches cached images.
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

    private static IReadOnlyList<ImageSource> LoadFallback(string uri)
        => new[] { TryLoad(uri) ?? CreateTransparentFallback() };

    private static ImageSource? TryLoad(string uri)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(uri, UriKind.RelativeOrAbsolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource CreateTransparentFallback()
    {
        const int width = 1;
        const int height = 1;
        var pixels = new byte[4];
        var source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        source.Freeze();
        return source;
    }
}
