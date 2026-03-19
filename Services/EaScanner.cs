using Microsoft.Win32;
using OptiscalerClient.Models;
using System.IO;

namespace OptiscalerClient.Services;

public class EaScanner
{
    private readonly string[] REGISTRY_PATHS = new[]
    {
        @"SOFTWARE\WOW6432Node\Electronic Arts\EA Games",
        @"SOFTWARE\Electronic Arts\EA Games"
    };

    public List<Game> Scan()
    {
        var games = new List<Game>();

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            foreach (var basePath in REGISTRY_PATHS)
            {
                using var eaGamesKey = baseKey.OpenSubKey(basePath);
                if (eaGamesKey == null) continue;

                foreach (var subKeyName in eaGamesKey.GetSubKeyNames())
                {
                    using var gameKey = eaGamesKey.OpenSubKey(subKeyName);
                    if (gameKey == null) continue;

                    var gameName = gameKey.GetValue("DisplayName") as string ?? subKeyName;
                    var path = gameKey.GetValue("Install Dir") as string;

                    if (!string.IsNullOrEmpty(gameName) && !string.IsNullOrEmpty(path))
                    {
                        if (Directory.Exists(path))
                        {
                            games.Add(new Game
                            {
                                AppId = subKeyName,
                                Name = gameName,
                                InstallPath = path,
                                Platform = GamePlatform.EA
                            });
                        }
                    }
                }
            }
        }
        catch { /* Ignore registry or parsing errors */ }

        return games;
    }
}
