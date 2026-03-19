using System.IO;
using System.Text.Json;
using OptiscalerClient.Models;

namespace OptiscalerClient.Services;

public class GamePersistenceService
{
    private readonly string _filePath;

    public GamePersistenceService()
    {
        // Guardamos en AppData para ser correctos con los permisos de usuario
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "OptiscalerClient");
        
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        _filePath = Path.Combine(folder, "games.json");
    }

    public void SaveGames(IEnumerable<Game> games)
    {
        var json = JsonSerializer.Serialize(games.ToList(), OptimizerContext.Default.ListGame);
        File.WriteAllText(_filePath, json);
    }

    public List<Game> LoadGames()
    {
        if (!File.Exists(_filePath))
        {
            return new List<Game>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize(json, OptimizerContext.Default.ListGame) ?? new List<Game>();
        }
        catch
        {
            return new List<Game>();
        }
    }
}
