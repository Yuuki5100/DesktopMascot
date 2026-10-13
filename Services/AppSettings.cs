namespace DesktopMascot.Services;

public sealed class AppSettings
{
    public string AnimationMode { get; set; } = nameof(DesktopMascot.Services.AnimationMode.Walk);
}
