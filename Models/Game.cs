namespace OptiscalerClient.Models;

public enum GamePlatform
{
    Steam,
    Epic,
    GOG,
    Xbox,
    EA,
    BattleNet,
    Ubisoft,
    Manual
}

public class Game
{
    public string Name { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public GamePlatform Platform { get; set; }
    public bool IsManual => Platform == GamePlatform.Manual;
    public string AppId { get; set; } = string.Empty; // Steam AppId or Epic ItemId
    public string ExecutablePath { get; set; } = string.Empty; // Path to main .exe (if detectable)

    public string? CoverImageUrl { get; set; }

    // Detected Technologies
    public string? DlssVersion { get; set; }
    public string? DlssPath { get; set; }

    public string? DlssFrameGenVersion { get; set; }
    public string? DlssFrameGenPath { get; set; }

    public string? FsrVersion { get; set; }
    public string? FsrPath { get; set; }

    public string? XessVersion { get; set; }
    public string? XessPath { get; set; }

    public bool IsOptiscalerInstalled { get; set; }
    public string? OptiscalerVersion { get; set; }
}
