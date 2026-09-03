namespace VerciWin.Core.Settings;

/// <summary>
/// User settings model persisted to %AppData%/VerciWin/settings.json.
/// </summary>
public sealed class AppSettings
{
    public double Opacity { get; set; } = 1.0;
    public string VisualStyle { get; set; } = "Glow"; // "Glow" | "Minimal"
    public bool IsOverlayMode { get; set; } = true;    // true = Always on Desktop (click-through); false = Normal Window
    public string OverlayPosition { get; set; } = "LowerThird"; // "LowerThird" | "Center" | "FullScreen"

    public static AppSettings Defaults => new();
}
