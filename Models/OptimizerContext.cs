using System.Text.Json.Serialization;
using OptiscalerClient.Models;

namespace OptiscalerClient.Models
{
    /// <summary>
    /// Source generator for JSON serialization to support high-performance trimming.
    /// This allows the compiler to remove unused reflection code, significantly reducing binary size.
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(AppConfiguration))]
    [JsonSerializable(typeof(ComponentVersions))]
    [JsonSerializable(typeof(InstallationManifest))]
    [JsonSerializable(typeof(List<Game>))]
    [JsonSerializable(typeof(Game))]
    internal partial class OptimizerContext : JsonSerializerContext
    {
    }
}
