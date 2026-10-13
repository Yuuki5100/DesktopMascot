using System.Windows;
using Microsoft.Win32;

namespace DesktopMascot.Services;

public sealed class MonitorService : IDisposable
{
    public event EventHandler? DisplaySettingsChanged;

    public MonitorService()
    {
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public Rect GetVirtualDesktopBounds()
        => new(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => DisplaySettingsChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }
}
