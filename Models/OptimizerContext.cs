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
    [JsonSerializable(typeof(ScanSourcesConfig))]
    [JsonSerializable(typeof(ComponentVersions))]
    [JsonSerializable(typeof(InstallationManifest))]
    [JsonSerializable(typeof(ManifestFileRecord))]
    [JsonSerializable(typeof(KeyFileSnapshot))]
    [JsonSerializable(typeof(List<ManifestFileRecord>))]
    [JsonSerializable(typeof(List<KeyFileSnapshot>))]
    [JsonSerializable(typeof(List<Game>))]
    [JsonSerializable(typeof(Game))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(OptiScalerReleaseEntry))]
    [JsonSerializable(typeof(OptiScalerReleasesCache))]
    [JsonSerializable(typeof(List<OptiScalerReleaseEntry>))]
    [JsonSerializable(typeof(ExtrasReleaseEntry))]
    [JsonSerializable(typeof(ExtrasReleasesCache))]
    [JsonSerializable(typeof(List<ExtrasReleaseEntry>))]
    [JsonSerializable(typeof(OptiPatcherReleaseEntry))]
    [JsonSerializable(typeof(OptiPatcherReleasesCache))]
    [JsonSerializable(typeof(List<OptiPatcherReleaseEntry>))]
    [JsonSerializable(typeof(OptiScalerProfile))]
    [JsonSerializable(typeof(List<OptiScalerProfile>))]
    [JsonSerializable(typeof(Dictionary<string, Dictionary<string, string>>))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    internal partial class OptimizerContext : JsonSerializerContext
    {
    }
}
