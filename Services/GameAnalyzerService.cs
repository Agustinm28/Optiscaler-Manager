using OptiscalerClient.Models;
using System.Diagnostics;
using System.IO;

namespace OptiscalerClient.Services;

public class GameAnalyzerService
{
    private static readonly string[] _dlssNames = new[] { "nvngx_dlss.dll" };
    private static readonly string[] _dlssFrameGenNames = new[] { "nvngx_dlssg.dll" };
    private static readonly string[] _fsrNames = new[] {
        "amd_fidelityfx_dx12.dll",
        "amd_fidelityfx_vk.dll",
        "ffx_fsr2_api_x64.dll",
        "ffx_fsr2_api_dx12_x64.dll",
        "ffx_fsr2_api_vk_x64.dll",
        "ffx_fsr3_api_x64.dll",
        "ffx_fsr3_api_dx12_x64.dll"
    };
    private static readonly string[] _xessNames = new[] { "libxess.dll" };

    public void AnalyzeGame(Game game)
    {
        if (string.IsNullOrEmpty(game.InstallPath) || !Directory.Exists(game.InstallPath))
            return;

        // Reset current versions before analysis
        game.DlssVersion = null;
        game.DlssPath = null;
        game.FsrVersion = null;
        game.FsrPath = null;
        game.XessVersion = null;
        game.XessPath = null;
        game.IsOptiscalerInstalled = false;
        game.OptiscalerVersion = null; // Will be repopulated from manifest or log

        HashSet<string> ignoredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            };

            // ── Detect OptiScaler ──────────────────────────────────────────────────
            // Do this first so we can ignore its installed files when looking for native DLLs
            try
            {
                // ── Priority 1: manifest ────────────────────────────────────────────
                var manifestFiles = Directory.GetFiles(game.InstallPath, "optiscaler_manifest.json", options);
                if (manifestFiles.Length > 0)
                {
                    try
                    {
                        var manifestJson = File.ReadAllText(manifestFiles[0]);
                        var manifest = System.Text.Json.JsonSerializer.Deserialize<Models.InstallationManifest>(manifestJson);
                        if (manifest != null)
                        {
                            game.IsOptiscalerInstalled = true;
                            if (!string.IsNullOrEmpty(manifest.OptiscalerVersion))
                                game.OptiscalerVersion = manifest.OptiscalerVersion;

                            // Determine absolute game directory to construct absolute paths
                            string originDir = string.IsNullOrEmpty(manifest.InstalledGameDirectory)
                                ? Path.GetDirectoryName(Path.GetDirectoryName(manifestFiles[0]))!
                                : manifest.InstalledGameDirectory;

                            if (!string.IsNullOrEmpty(originDir))
                            {
                                foreach (var relFile in manifest.InstalledFiles)
                                {
                                    ignoredFiles.Add(Path.GetFullPath(Path.Combine(originDir, relFile)));
                                }
                            }
                        }
                    }
                    catch { /* Corrupt manifest — fall through to next priority */ }
                }

                // ── Priority 2: runtime log (overrides if it has richer version info) ──
                if (!game.IsOptiscalerInstalled || string.IsNullOrEmpty(game.OptiscalerVersion))
                {
                    try
                    {
                        var logs = Directory.GetFiles(game.InstallPath, "optiscaler.log", options);
                        if (logs.Length > 0)
                        {
                            // Example log line: "[2024-...] [Init] OptiScaler v0.7.0-rc1"
                            foreach (var line in File.ReadLines(logs[0]).Take(10))
                            {
                                if (line.Contains("OptiScaler v", StringComparison.OrdinalIgnoreCase))
                                {
                                    var idx = line.IndexOf("OptiScaler v", StringComparison.OrdinalIgnoreCase);
                                    if (idx != -1)
                                    {
                                        var verPart = line.Substring(idx + 12).Trim();
                                        var endIdx = verPart.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
                                        if (endIdx != -1) verPart = verPart.Substring(0, endIdx);
                                        if (!string.IsNullOrEmpty(verPart))
                                        {
                                            game.IsOptiscalerInstalled = true;
                                            game.OptiscalerVersion = verPart;
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                // ── Priority 3: OptiScaler.ini presence (no version — last resort) ──
                if (!game.IsOptiscalerInstalled)
                {
                    var iniFiles = Directory.GetFiles(game.InstallPath, "OptiScaler.ini", options);
                    if (iniFiles.Length > 0)
                        game.IsOptiscalerInstalled = true;
                }
            }
            catch { /* Ignore OptiScaler detection errors */ }

            // Efficiently search ONLY for the specific files we care about
            // This avoids listing thousands of DLLs

            // DLSS
            FindBestVersion(game, game.InstallPath, _dlssNames, options, ignoredFiles, (g, path, ver) =>
            {
                g.DlssPath = path;
                g.DlssVersion = ver;
            });

            // DLSS Frame Gen
            FindBestVersion(game, game.InstallPath, _dlssFrameGenNames, options, ignoredFiles, (g, path, ver) => { g.DlssFrameGenPath = path; g.DlssFrameGenVersion = ver; });

            // FSR
            FindBestVersion(game, game.InstallPath, _fsrNames, options, ignoredFiles, (g, path, ver) => { g.FsrPath = path; g.FsrVersion = ver; });

            // XeSS
            FindBestVersion(game, game.InstallPath, _xessNames, options, ignoredFiles, (g, path, ver) => { g.XessPath = path; g.XessVersion = ver; });

        }
        catch { /* General error */ }
    }

    private void FindBestVersion(Game game, string path, string[] filePatterns, EnumerationOptions options, HashSet<string> ignoredFiles, Action<Game, string, string> updateAction)
    {
        var highestVer = new Version(0, 0);
        string? bestPath = null;
        string? bestVerStr = null;

        foreach (var pattern in filePatterns)
        {
            try
            {
                var files = Directory.GetFiles(path, pattern, options);
                foreach (var file in files)
                {
                    if (ignoredFiles.Contains(Path.GetFullPath(file))) continue;

                    var versionStr = GetFileVersion(file);

                    // Clean up version string if it contains "FSR ", e.g. "FSR 3.1.4"
                    string parseableVerStr = versionStr;
                    if (parseableVerStr.StartsWith("FSR ", StringComparison.OrdinalIgnoreCase))
                    {
                        parseableVerStr = parseableVerStr.Substring(4).Trim();
                    }

                    // Also take only the first component if there are spaces, e.g. "3.1.0 (release)"
                    parseableVerStr = parseableVerStr.Split(' ')[0];

                    if (Version.TryParse(parseableVerStr, out var currentVer))
                    {
                        if (currentVer > highestVer)
                        {
                            highestVer = currentVer;
                            bestPath = file;
                            bestVerStr = versionStr; // keep original string for display
                        }
                    }
                }
            }
            catch { /* Ignore individual search errors */ }
        }

        if (bestPath != null && bestVerStr != null)
        {
            updateAction(game, bestPath, bestVerStr);
        }
    }

    private string GetFileVersion(string filePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(filePath);

            // ProductVersion is usually more accurate for libraries like DLSS (e.g. "3.7.10.0")
            // FileVersion might be "1.0.0.0" wrapper.
            if (!string.IsNullOrEmpty(info.ProductVersion) && info.ProductVersion != "1.0.0.0" && !info.ProductVersion.StartsWith("1.0."))
            {
                return info.ProductVersion.Replace(',', '.').Split(' ')[0];
            }

            if (!string.IsNullOrEmpty(info.FileVersion))
            {
                return info.FileVersion.Replace(',', '.').Split(' ')[0];
            }

            return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
        }
        catch
        {
            return "0.0.0.0";
        }
    }
}
