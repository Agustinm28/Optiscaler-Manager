namespace OptiscalerClient.Models;

/// <summary>
/// Represents a single entry in the scan exclusion list.
/// A game is excluded if its Name OR its install path ends with / contains PathSegment.
/// Both comparisons are case-insensitive.
/// </summary>
public class ScanExclusion
{
    /// <summary>
    /// Display-friendly name (e.g. "Wallpaper Engine").
    /// If non-empty, any game whose name matches this string is excluded.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The folder segment that appears after "…\steamapps\common\" (or any path component).
    /// Matching is done with a case-insensitive Contains check on the full install path,
    /// so partial folder names like "wallpaper_engine" or "Steamworks Shared" work correctly
    /// regardless of which Steam library the user has the game installed in.
    /// </summary>
    public string PathSegment { get; set; } = string.Empty;
}
