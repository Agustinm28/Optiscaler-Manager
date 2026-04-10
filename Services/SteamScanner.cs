using Microsoft.Win32;
using OptiscalerClient.Models;
using OptiscalerClient.Views;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace OptiscalerClient.Services;

public class SteamScanner : IGameScanner
{
    private const string REGISTRY_PATH = @"SOFTWARE\Valve\Steam";

    public List<Game> Scan()
    {
        var games = new List<Game>();
        var installPath = GetSteamInstallPath();

        if (string.IsNullOrEmpty(installPath))
            return games;

        var libraryFolders = GetLibraryFolders(installPath);

        foreach (var libraryPath in libraryFolders)
        {
            try
            {
                var steamappsPath = Path.Combine(libraryPath, "steamapps");
                DebugWindow.Log($"[Steam] Scanning library: {steamappsPath}");
                if (!Directory.Exists(steamappsPath)) continue;

                var manifestFiles = Directory.GetFiles(steamappsPath, "appmanifest_*.acf");
                foreach (var file in manifestFiles)
                {
                    var game = ParseManifest(file);
                    if (game != null)
                    {
                        // Verify install path exists
                        if (Directory.Exists(game.InstallPath))
                        {
                            games.Add(game);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[Steam] Error scanning library '{libraryPath}': {ex.Message}");
            }
        }

        return games;
    }

    private string? GetSteamInstallPath()
    {
        if (OperatingSystem.IsWindows())
            return GetSteamInstallPathWindows();
        return GetSteamInstallPathLinux();
    }

    [SupportedOSPlatform("windows")]
    private string? GetSteamInstallPathWindows()
    {
        try
        {
            // Try 32-bit registry view first (Steam is usually 32-bit app)
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = baseKey.OpenSubKey(REGISTRY_PATH);
            return key?.GetValue("InstallPath") as string;
        }
        catch (Exception ex)
        {
            DebugWindow.Log($"[Steam] Error reading registry: {ex.Message}");
            return null;
        }
    }

    private string? GetSteamInstallPathLinux()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            Path.Combine(home, "snap", "steam", "common", ".steam", "steam"),
        };
        return candidates.FirstOrDefault(p => Directory.Exists(Path.Combine(p, "steamapps")));
    }

    private List<string> GetLibraryFolders(string steamPath)
    {
        var folders = new List<string> { steamPath }; // Default library is always the install path
        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

        if (!File.Exists(vdfPath)) return folders;

        try
        {
            var content = File.ReadAllText(vdfPath);
            // Regex to find "path" "..."
            var matches = Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"");

            foreach (Match match in matches)
            {
                if (match.Success && match.Groups.Count > 1)
                {
                    // Unescape backslashes (VDF escapes them as \\ on Windows)
                    var path = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (!folders.Contains(path, StringComparer.Ordinal))
                    {
                        folders.Add(path);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugWindow.Log($"[Steam] Error reading libraryfolders.vdf: {ex.Message}");
        }

        return folders;
    }

    private Game? ParseManifest(string manifestPath)
    {
        try
        {
            var content = File.ReadAllText(manifestPath);

            // Extract AppID
            var appIdMatch = Regex.Match(content, "\"appid\"\\s+\"(\\d+)\"");
            var appId = appIdMatch.Success ? appIdMatch.Groups[1].Value : Path.GetFileName(manifestPath).Replace("appmanifest_", "").Replace(".acf", "");

            // Extract Name
            var nameMatch = Regex.Match(content, "\"name\"\\s+\"([^\"]+)\"");
            var name = nameMatch.Success ? nameMatch.Groups[1].Value : "Unknown Game";

            // Extract InstallDir
            var installDirMatch = Regex.Match(content, "\"installdir\"\\s+\"([^\"]+)\"");
            if (!installDirMatch.Success) return null;

            var installDirName = installDirMatch.Groups[1].Value;
            var steamappsPath = Path.GetDirectoryName(manifestPath); // .../steamapps
            if (steamappsPath == null) return null;

            var commonPath = Path.Combine(steamappsPath, "common");
            var fullInstallPath = Path.Combine(commonPath, installDirName);

            return new Game
            {
                AppId = appId,
                Name = name,
                InstallPath = fullInstallPath,
                Platform = GamePlatform.Steam
            };
        }
        catch (Exception ex)
        {
            DebugWindow.Log($"[Steam] Error parsing manifest '{manifestPath}': {ex.Message}");
            return null;
        }
    }
}
