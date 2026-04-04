using System;
using System.Collections.Generic;

namespace OptiscalerClient.Models
{
    /// <summary>
    /// Configuration for GitHub repositories
    /// </summary>
    public class RepositoryConfig
    {
        public string RepoOwner { get; set; } = string.Empty;
        public string RepoName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Configuration for scan sources
    /// </summary>
    public class ScanSourcesConfig
    {
        public bool ScanSteam { get; set; } = true;
        public bool ScanEpic { get; set; } = true;
        public bool ScanGOG { get; set; } = true;
        public bool ScanXbox { get; set; } = true;
        public bool ScanEA { get; set; } = true;
        public bool ScanUbisoft { get; set; } = true;
        public List<string> CustomFolders { get; set; } = new();
    }

    /// <summary>
    /// Root configuration containing all repository configurations
    /// </summary>
    public class AppConfiguration
    {
        public RepositoryConfig App { get; set; } = new() { RepoOwner = "Agustinm28", RepoName = "Optiscaler-Switcher" };
        public RepositoryConfig OptiScaler { get; set; } = new();
        public RepositoryConfig OptiScalerBetas { get; set; } = new();
        public RepositoryConfig OptiScalerExtras { get; set; } = new();
        public RepositoryConfig Fakenvapi { get; set; } = new();
        public RepositoryConfig NukemFG { get; set; } = new();
        public string Language { get; set; } = "en";
        public bool Debug { get; set; } = false;
        public string DefaultProfileName { get; set; } = OptiScalerProfile.BuiltInDefaultName;
        public bool AutoScan { get; set; } = true;
        public bool AnimationsEnabled { get; set; } = true;
        public bool ShowBetaVersions { get; set; } = true;
        public bool PreferGridView { get; set; } = true;
        public string? DefaultGpuId { get; set; } = null;
        public bool HasShownInitialScanPrompt { get; set; } = false;
        public bool HasCompletedInitialScan { get; set; } = false;
        public List<string> ScanDriveRoots { get; set; } = new();

        // Window state persistence
        public double WindowWidth { get; set; } = 1200;
        public double WindowHeight { get; set; } = 720;
        public bool WindowMaximized { get; set; } = false;
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        /// <summary>
        /// The default FSR 4 INT8 extras version to pre-select in ManageGameWindow.
        /// Null or "none" means "do not inject".
        /// </summary>
        public string? DefaultExtrasVersion { get; set; } = null;
        public ScanSourcesConfig ScanSources { get; set; } = new();
        public string SteamGridDBApiKey { get; set; } = string.Empty;
        public List<ScanExclusion> ScanExclusions { get; set; } = new();
    }

    /// <summary>
    /// Version information for all components
    /// </summary>
    public class ComponentVersions
    {
        public string? OptiScalerVersion { get; set; }
        public string? FakenvapiVersion { get; set; }
        public string? NukemFGVersion { get; set; }
    }

    /// <summary>
    /// A single OptiScaler release entry stored in the local releases cache.
    /// Only metadata is stored — no binaries are downloaded at this stage.
    /// </summary>
    public class OptiScalerReleaseEntry
    {
        public string Version { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public bool IsBeta { get; set; }
        public bool IsLatestStable { get; set; }
        public bool IsLatestBeta { get; set; }
    }

    /// <summary>
    /// Local cache of OptiScaler release metadata fetched from GitHub.
    /// Updated on each successful API call and merged with existing entries.
    /// </summary>
    public class OptiScalerReleasesCache
    {
        public DateTime LastUpdated { get; set; } = DateTime.MinValue;
        public List<OptiScalerReleaseEntry> Releases { get; set; } = new();
    }

    /// <summary>
    /// A single OptiScaler Extras (FSR4 INT8 mod) release entry stored in the local cache.
    /// </summary>
    public class ExtrasReleaseEntry
    {
        public string Version { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public bool IsLatest { get; set; }
    }

    /// <summary>
    /// Local cache of OptiScaler Extras release metadata.
    /// </summary>
    public class ExtrasReleasesCache
    {
        public DateTime LastUpdated { get; set; } = DateTime.MinValue;
        public List<ExtrasReleaseEntry> Releases { get; set; } = new();
    }
}
