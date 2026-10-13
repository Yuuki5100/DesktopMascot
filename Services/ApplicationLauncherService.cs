using System.IO;
using System.Diagnostics;

namespace DesktopMascot.Services;

public sealed record LaunchResult(bool Success, string Message = "");

public sealed class ApplicationLauncherService
{
    public LaunchResult LaunchEdge()
        => LaunchFirstAvailable(
            new[]
            {
                "msedge.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
            });

    public LaunchResult LaunchVsCode()
        => LaunchFirstAvailable(
            new[]
            {
                "code.cmd",
                "code.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe")
            });

    public LaunchResult LaunchWezTerm()
        => LaunchFirstAvailable(
            new[]
            {
                "wezterm.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WezTerm", "wezterm-gui.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WezTerm", "wezterm-gui.exe")
            });

    private static LaunchResult LaunchFirstAvailable(IEnumerable<string> candidates)
    {
        var errors = new List<string>();

        foreach (var candidate in candidates.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            try
            {
                var isPath = candidate.Contains(Path.DirectorySeparatorChar) || candidate.Contains(Path.AltDirectorySeparatorChar);
                if (isPath && !File.Exists(candidate))
                {
                    continue;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    UseShellExecute = true
                });

                return new LaunchResult(true);
            }
            catch (Exception ex)
            {
                errors.Add($"{candidate}: {ex.Message}");
            }
        }

        return new LaunchResult(false, errors.Count > 0
            ? string.Join(Environment.NewLine, errors)
            : "実行ファイルが見つかりませんでした。");
    }
}
