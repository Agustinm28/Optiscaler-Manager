using OptiscalerClient.Models;
using System.IO;

namespace OptiscalerClient.Services;

public class GameScannerService
{
    private readonly SteamScanner _steamScanner;
    private readonly EpicScanner _epicScanner;
    private readonly GogScanner _gogScanner;
    private readonly XboxScanner _xboxScanner;
    private readonly EaScanner _eaScanner;
    private readonly BattleNetScanner _battleNetScanner;
    private readonly UbisoftScanner _ubisoftScanner;
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

    public async Task<List<Game>> ScanAllGamesAsync()
    {
        return await Task.Run(() =>
        {
            var games = new List<Game>();
            var analyzer = new GameAnalyzerService();

            void ProcessGames(IEnumerable<Game> scannedGames)
            {
                foreach (var game in scannedGames)
                {
                    if (_exclusions.IsExcluded(game)) continue;
                    analyzer.AnalyzeGame(game);
                    games.Add(game);
                }
            }

            try
            {
                ProcessGames(_steamScanner.Scan());
            }
            catch { /* Log error */ }

            try
            {
                ProcessGames(_epicScanner.Scan());
            }
            catch { /* Log error */ }

            try
            {
                ProcessGames(_gogScanner.Scan());
            }
            catch { /* Log error */ }

            try
            {
                ProcessGames(_xboxScanner.Scan());
            }
            catch { /* Log error */ }

            try
            {
                ProcessGames(_eaScanner.Scan());
            }
            catch { /* Log error */ }

            try
            {
                ProcessGames(_battleNetScanner.Scan());
            }
            catch { /* Log error */ }

            try
            {
                ProcessGames(_ubisoftScanner.Scan());
            }
            catch { /* Log error */ }

            return games.OrderBy(g => g.Platform).ThenBy(g => g.Name).ToList();
        });
    }
}
