using System.IO;

namespace DesktopMascot.Services;

public sealed record CleanupResult(bool Success, bool NothingToDelete, string Message = "");

public sealed class CacheCleanupService
{
    public CleanupResult ClearVsCodeCache()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return ClearDirectories(
            Path.Combine(appData, "Code", "Cache"),
            Path.Combine(appData, "Code", "CachedData"));
    }

    public CleanupResult ClearTeamsCache()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return ClearDirectories(
            Path.Combine(
                localAppData,
                "Packages",
                "MSTeams_8wekyb3d8bbwe",
                "LocalCache",
                "Microsoft",
                "MSTeams"));
    }

    private static CleanupResult ClearDirectories(params string[] paths)
    {
        var existing = paths
            .Where(IsSafeAbsolutePath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (existing.Count == 0)
        {
            return new CleanupResult(true, true);
        }

        var errors = new List<string>();

        foreach (var path in existing)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                errors.Add($"{path}: {ex.Message}");
            }
        }

        return errors.Count == 0
            ? new CleanupResult(true, false)
            : new CleanupResult(false, false, string.Join(Environment.NewLine, errors));
    }

    private static bool IsSafeAbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        var root = Path.GetPathRoot(path);
        if (string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), root?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
