using Microsoft.Win32;
using OptiscalerClient.Models;
using System.IO;
using System.Text.RegularExpressions;

namespace OptiscalerClient.Services;

public class SteamScanner
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
            catch { /* Ignore errors accessing folders */ }
        }

        return games;
    }

    private string? GetSteamInstallPath()
    {
        try
        {
            // Try 32-bit registry view first (Steam is usually 32-bit app)
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = baseKey.OpenSubKey(REGISTRY_PATH);
            return key?.GetValue("InstallPath") as string;
        }
        catch 
        {
            return null; 
        }
    }

    private List<string> GetLibraryFolders(string steamPath)
    {
        var folders = new List<string> { steamPath }; // Default library is always the install path
        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

        if (!File.Exists(vdfPath)) return folders;

        try
        {
            var content = File.ReadAllText(vdfPath);
            // Regex to find "path" "C:\\..."
            // VDF format uses "path" <tab/space> "value"
            var matches = Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"");

            foreach (Match match in matches)
            {
                if (match.Success && match.Groups.Count > 1)
                {
                    // Unescape backslashes (VDF escapes them as \\)
                    var path = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (!folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        folders.Add(path);
                    }
                }
            }
        }
        catch { }

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
            var libraryPath = Path.GetDirectoryName(Path.GetDirectoryName(manifestPath)); // Go up from steamapps/appmanifest_... to library root?
            // Actually manifest is in steamapps, game is in steamapps/common/
            
            // The libraryPath in GetLibraryFolders returns the root (e.g. C:\Steam).
            // manifestPath is C:\Steam\steamapps\appmanifest_123.acf
            // Game path is C:\Steam\steamapps\common\GameName
            
            var steamappsPath = Path.GetDirectoryName(manifestPath); // ...\steamapps
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
        catch
        {
            return null;
        }
    }
}
