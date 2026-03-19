using System.Collections.Generic;

namespace OptiscalerClient.Models
{
    /// <summary>
    /// Manifest that tracks all files installed by OptiScaler for a specific game.
    /// This allows complete uninstallation without leaving residual files.
    /// </summary>
    public class InstallationManifest
    {
        /// <summary>
        /// Version of the manifest format (for future compatibility)
        /// </summary>
        public int ManifestVersion { get; set; } = 1;

        /// <summary>
        /// OptiScaler version that was installed
        /// </summary>
        public string? OptiscalerVersion { get; set; }

        /// <summary>
        /// Injection method used (e.g., dxgi.dll, winmm.dll)
        /// </summary>
        public string InjectionMethod { get; set; } = string.Empty;

        /// <summary>
        /// Date and time of installation
        /// </summary>
        public string InstallDate { get; set; } = string.Empty;

        /// <summary>
        /// Absolute path of the directory where OptiScaler was physically installed.
        /// For UE5/Phoenix games this is the "Phoenix\Binaries\Win64" subdirectory,
        /// not the root InstallPath. Storing this avoids re-detection issues at uninstall time.
        /// </summary>
        public string? InstalledGameDirectory { get; set; }

        /// <summary>
        /// List of all files that were installed (relative paths from game directory)
        /// </summary>
        public List<string> InstalledFiles { get; set; } = new List<string>();

        /// <summary>
        /// List of files that were backed up (existed before installation)
        /// </summary>
        public List<string> BackedUpFiles { get; set; } = new List<string>();

        /// <summary>
        /// List of directories that were created during installation (relative paths from game directory)
        /// These will be deleted during uninstallation if they are empty
        /// </summary>
        public List<string> InstalledDirectories { get; set; } = new List<string>();
    }
}
