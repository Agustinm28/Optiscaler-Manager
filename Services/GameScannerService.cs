// OptiScaler Client - A frontend for managing OptiScaler installations
// Copyright (C) 2026 Agustín Montaña (Agustinm28)
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using OptiscalerClient.Models;
using OptiscalerClient.Views;
using System.IO;
using System.Runtime.Versioning;

namespace OptiscalerClient.Services;

[SupportedOSPlatform("windows")]
public class GameScannerService
{
    private readonly IGameScanner _steamScanner;
    private readonly IGameScanner _epicScanner;
    private readonly IGameScanner _gogScanner;
    private readonly IGameScanner _xboxScanner;
    private readonly IGameScanner _eaScanner;
    private readonly IGameScanner _battleNetScanner;
    private readonly IGameScanner _ubisoftScanner;
    private readonly ExclusionService _exclusions;

    public GameScannerService()
    {
        _steamScanner = new SteamScanner();
        _epicScanner = new EpicScanner();
        _gogScanner = new GogScanner();
        _xboxScanner = new XboxScanner();
        _eaScanner = new EaScanner();
        _battleNetScanner = new BattleNetScanner();
        _ubisoftScanner = new UbisoftScanner();

        // config.json lives next to the executable (copied by the build)
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        _exclusions = new ExclusionService(configPath);
    }

    public async Task<List<Game>> ScanAllGamesAsync(ScanSourcesConfig? scanConfig = null, IReadOnlyCollection<string>? allowedDriveRoots = null)
    {
        return await Task.Run(() =>
        {
            var games = new List<Game>();
            var analyzer = new GameAnalyzerService();
            DebugWindow.Log("[Scanner] Executing global game scan across all platforms...");

            // Use default config if none provided
            if (scanConfig == null)
            {
                scanConfig = new ScanSourcesConfig();
            }

            bool IsDriveAllowed(Game game)
            {
                if (allowedDriveRoots == null || allowedDriveRoots.Count == 0)
                    return true;

                try
                {
                    var root = Path.GetPathRoot(game.InstallPath);
                    if (string.IsNullOrEmpty(root))
                        return false;

                    return allowedDriveRoots.Contains(root, StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }

            void ProcessGames(IEnumerable<Game> scannedGames)
            {
                foreach (var game in scannedGames)
                {
                    if (_exclusions.IsExcluded(game)) continue;
                    if (!IsDriveAllowed(game)) continue;
                    analyzer.AnalyzeGame(game);
                    games.Add(game);
                }
            }

            // Scan platform sources based on config
            if (scanConfig.ScanSteam)
            {
                try
                {
                    DebugWindow.Log("[Scanner] Scanning Steam library...");
                    ProcessGames(_steamScanner.Scan());
                }
                catch (Exception ex) { DebugWindow.Log($"[Scanner] Steam scan error: {ex.Message}"); }
            }

            if (scanConfig.ScanEpic)
            {
                try
                {
                    DebugWindow.Log("[Scanner] Scanning Epic Games library...");
                    ProcessGames(_epicScanner.Scan());
                }
                catch (Exception ex) { DebugWindow.Log($"[Scanner] Epic scan error: {ex.Message}"); }
            }

            if (scanConfig.ScanGOG)
            {
                try
                {
                    DebugWindow.Log("[Scanner] Scanning GOG library...");
                    ProcessGames(_gogScanner.Scan());
                }
                catch (Exception ex) { DebugWindow.Log($"[Scanner] GOG scan error: {ex.Message}"); }
            }

            if (scanConfig.ScanXbox)
            {
                try
                {
                    DebugWindow.Log("[Scanner] Scanning Xbox library (MS Store)...");
                    ProcessGames(_xboxScanner.Scan());
                }
                catch (Exception ex) { DebugWindow.Log($"[Scanner] Xbox scan error: {ex.Message}"); }
            }

            if (scanConfig.ScanEA)
            {
                try
                {
                    DebugWindow.Log("[Scanner] Scanning EA App library...");
                    ProcessGames(_eaScanner.Scan());
                }
                catch (Exception ex) { DebugWindow.Log($"[Scanner] EA scan error: {ex.Message}"); }
            }

            // Always scan Battle.net (no config switch yet)
            try
            {
                DebugWindow.Log("[Scanner] Scanning Battle.net library...");
                ProcessGames(_battleNetScanner.Scan());
            }
            catch (Exception ex) { DebugWindow.Log($"[Scanner] Battle.net scan error: {ex.Message}"); }

            if (scanConfig.ScanUbisoft)
            {
                try
                {
                    DebugWindow.Log("[Scanner] Scanning Ubisoft Connect library...");
                    ProcessGames(_ubisoftScanner.Scan());
                }
                catch (Exception ex) { DebugWindow.Log($"[Scanner] Ubisoft scan error: {ex.Message}"); }
            }

            // Scan custom folders
            if (scanConfig.CustomFolders != null && scanConfig.CustomFolders.Count > 0)
            {
                DebugWindow.Log($"[Scanner] Scanning {scanConfig.CustomFolders.Count} custom folder(s)...");
                foreach (var customFolder in scanConfig.CustomFolders)
                {
                    try
                    {
                        var customGames = ScanCustomFolder(customFolder);
                        DebugWindow.Log($"[Scanner] Found {customGames.Count} games in '{customFolder}'");
                        ProcessGames(customGames);
                    }
                    catch (Exception ex)
                    {
                        DebugWindow.Log($"[Scanner] Error scanning custom folder '{customFolder}': {ex.Message}");
                    }
                }
            }

            DebugWindow.Log($"[Scanner] Scan completed. Found {games.Count} valid games.");

            return games.OrderBy(g => g.Platform).ThenBy(g => g.Name).ToList();
        });
    }

    private List<Game> ScanCustomFolder(string rootFolder)
    {
        var games = new List<Game>();

        if (!Directory.Exists(rootFolder))
        {
            DebugWindow.Log($"[Scanner] Custom folder does not exist: {rootFolder}");
            return games;
        }

        try
        {
            // Get all subdirectories (game folders)
            var gameFolders = Directory.GetDirectories(rootFolder);

            foreach (var gameFolder in gameFolders)
            {
                try
                {
                    // Find all .exe files in this game folder (recursive, but limited depth)
                    var exeFiles = Directory.GetFiles(gameFolder, "*.exe", SearchOption.AllDirectories);

                    foreach (var exePath in exeFiles)
                    {
                        // Use the game folder name as the game name
                        var gameName = Path.GetFileName(gameFolder);
                        
                        // Skip common non-game executables
                        var exeName = Path.GetFileNameWithoutExtension(exePath).ToLower();
                        if (exeName.Contains("unins") || exeName.Contains("setup") || 
                            exeName.Contains("installer") || exeName.Contains("crash") ||
                            exeName.Contains("launcher") && !exeName.Contains("game"))
                        {
                            continue;
                        }

                        var game = new Game
                        {
                            Name = gameName,
                            ExecutablePath = exePath,
                            InstallPath = gameFolder,
                            Platform = GamePlatform.Custom,
                            AppId = "Custom_" + Path.GetFileName(gameFolder)
                        };

                        games.Add(game);
                        DebugWindow.Log($"[Scanner] Found custom game: {gameName} ({Path.GetFileName(exePath)})");
                        
                        // Only take the first valid exe per game folder to avoid duplicates
                        break;
                    }
                }
                catch (Exception ex)
                {
                    DebugWindow.Log($"[Scanner] Error scanning game folder '{gameFolder}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugWindow.Log($"[Scanner] Error accessing custom folder '{rootFolder}': {ex.Message}");
        }

        return games;
    }
}
