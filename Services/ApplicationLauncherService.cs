using System.ComponentModel;
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

    public LaunchResult LaunchCompactWslDisk()
        => LaunchPowerShellScript("Compact-WslDisk.ps1", runAsAdministrator: true);

    public LaunchResult LaunchRemoveStaleWorktree()
        => LaunchPowerShellScript("Remove-StaleWorktree.ps1", runAsAdministrator: false);

    private static LaunchResult LaunchPowerShellScript(string scriptName, bool runAsAdministrator)
    {
        var scriptPath = FindScript(Path.Combine("script", "volume-clear-code", scriptName));
        if (scriptPath is null)
        {
            return new LaunchResult(false, $"{scriptName} が見つかりませんでした。");
        }

        try
        {
            // スクリプト側で yes/no の確認や Enter 待ちを行うため、コンソールを表示して起動する
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                Verb = runAsAdministrator ? "runas" : string.Empty
            });

            return new LaunchResult(true);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new LaunchResult(false, "管理者権限での実行がキャンセルされました。");
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, ex.Message);
        }
    }

    private static string? FindScript(string relativePath)
    {
        // dotnet run では bin 配下から起動されるため、親フォルダをたどってリポジトリ内のスクリプトを探す
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private const int ErrorCancelled = 1223;

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
