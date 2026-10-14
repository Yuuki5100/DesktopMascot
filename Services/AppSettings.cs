namespace DesktopMascot.Services;

public sealed class AppSettings
{
    public string AnimationMode { get; set; } = nameof(DesktopMascot.Services.AnimationMode.Walk);
    public string CharacterType { get; set; } = nameof(DesktopMascot.Services.CharacterType.Pompompurin);
    public string SpeechMessage { get; set; } = "こんにちは";
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
}
