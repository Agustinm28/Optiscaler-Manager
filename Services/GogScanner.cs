using Microsoft.Win32;
using OptiscalerClient.Models;
using System.IO;

namespace OptiscalerClient.Services;

public class GogScanner
{
    private const string REGISTRY_PATH = @"SOFTWARE\WOW6432Node\GOG.com\Games";

    public List<Game> Scan()
    {
        var games = new List<Game>();

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var gamesKey = baseKey.OpenSubKey(REGISTRY_PATH);

            if (gamesKey == null)
                return games;

            foreach (var subKeyName in gamesKey.GetSubKeyNames())
            {
                using var gameKey = gamesKey.OpenSubKey(subKeyName);
                if (gameKey == null) continue;

                var gameName = gameKey.GetValue("gameName") as string;
                var path = gameKey.GetValue("path") as string;
                var gameId = gameKey.GetValue("gameID") as string ?? subKeyName;

                if (!string.IsNullOrEmpty(gameName) && !string.IsNullOrEmpty(path))
                {
                    if (Directory.Exists(path))
                    {
                        games.Add(new Game
                        {
                            AppId = gameId,
                            Name = gameName,
                            InstallPath = path,
                            Platform = GamePlatform.GOG
                        });
                    }
                }
            }
        }
        catch { /* Ignore registry or parsing errors */ }

        return games;
    }
}
