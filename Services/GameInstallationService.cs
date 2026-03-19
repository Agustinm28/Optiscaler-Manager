using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using OptiscalerClient.Models;

namespace OptiscalerClient.Services
{
    public class GameInstallationService
    {
        private const string BackupFolderName = "OptiScalerBackup";
        private const string ManifestFileName = "optiscaler_manifest.json";

        // Files that we want to track specifically for backup purposes if they exist in the game folder
        // essentially anything that OptiScaler might replace.
        // We will backup ANYTHING we overwrite, but these are known criticals.
        private readonly string[] _criticalFiles = { "dxgi.dll", "version.dll", "winmm.dll", "nvngx.dll", "nvngx_dlssg.dll", "libxess.dll" };

        public void InstallOptiScaler(Game game, string cachePath, string injectionDllName = "dxgi.dll",
                                     bool installFakenvapi = false, string fakenvapiCachePath = "",
                                     bool installNukemFG = false, string nukemFGCachePath = "",
                                     string? optiscalerVersion = null,
                                     bool isManualMode = false)
        {
            if (!Directory.Exists(cachePath))
                throw new DirectoryNotFoundException("Updates cache directory not found. Please download OptiScaler first.");

            // Verify cache is not empty
            var cacheFiles = Directory.GetFiles(cachePath, "*.*", SearchOption.AllDirectories);
            if (cacheFiles.Length == 0)
                throw new Exception("Cache directory is empty. Download update again.");

            // Determine game directory intelligently (rules for base exe, Phoenix override, or user modal)
            string? gameDir;
            if (isManualMode)
            {
                gameDir = ShowFolderSelectionDialog(game);
            }
            else
            {
                gameDir = DetermineInstallDirectory(game);
                if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                {
                    throw new Exception("Could not automatically detect the game directory. Please use Manual Install.");
                }
            }

            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                throw new Exception("Installation cancelled or valid directory not found.");

            var backupDir = Path.Combine(gameDir, BackupFolderName);

            // Create backup folder
            if (!Directory.Exists(backupDir))
                Directory.CreateDirectory(backupDir);

            // Create installation manifest — OptiscalerVersion is the authoritative source for the UI
            var manifest = new InstallationManifest
            {
                InjectionMethod = injectionDllName,
                InstallDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                OptiscalerVersion = optiscalerVersion,
                // Store the EXACT directory used (already resolved for Phoenix/UE5 games).
                // Uninstall will read this directly, avoiding re-detection issues.
                InstalledGameDirectory = gameDir
            };

            // Find the main OptiScaler DLL (OptiScaler.dll or nvngx.dll for older versions)
            string? optiscalerMainDll = null;
            foreach (var file in cacheFiles)
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("nvngx.dll", StringComparison.OrdinalIgnoreCase))
                {
                    optiscalerMainDll = file;
                    break;
                }
            }

            if (optiscalerMainDll == null)
                throw new Exception("OptiScaler.dll or nvngx.dll not found in the downloaded package. Please re-download OptiScaler.");

            // Step 1: Install the main OptiScaler DLL with the selected injection method name
            var injectionDllPath = Path.Combine(gameDir, injectionDllName);

            // Backup existing file if it exists
            if (File.Exists(injectionDllPath))
            {
                var backupPath = Path.Combine(backupDir, injectionDllName);
                var backupSubDir = Path.GetDirectoryName(backupPath);
                if (backupSubDir != null && !Directory.Exists(backupSubDir))
                    Directory.CreateDirectory(backupSubDir);

                if (!File.Exists(backupPath))
                {
                    File.Copy(injectionDllPath, backupPath);
                    manifest.BackedUpFiles.Add(injectionDllName);
                }
            }

            // Copy OptiScaler.dll as the injection DLL
            File.Copy(optiscalerMainDll, injectionDllPath, true);
            manifest.InstalledFiles.Add(injectionDllName);

            // Step 2: Copy all other files (configs, dependencies, etc.)
            foreach (var sourcePath in cacheFiles)
            {
                var fileName = Path.GetFileName(sourcePath);

                // Skip the main OptiScaler DLL as we already handled it
                if (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("nvngx.dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(cachePath, sourcePath);
                var destPath = Path.Combine(gameDir, relativePath);
                var destDir = Path.GetDirectoryName(destPath);

                // Track created directories
                if (destDir != null && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);

                    // Add to manifest (relative to game directory)
                    var relativeDir = Path.GetRelativePath(gameDir, destDir);
                    if (!manifest.InstalledDirectories.Contains(relativeDir))
                    {
                        manifest.InstalledDirectories.Add(relativeDir);
                    }
                }

                // Backup existing file if needed
                if (File.Exists(destPath))
                {
                    var backupPath = Path.Combine(backupDir, relativePath);
                    var backupSubDir = Path.GetDirectoryName(backupPath);
                    if (backupSubDir != null && !Directory.Exists(backupSubDir))
                        Directory.CreateDirectory(backupSubDir);

                    if (!File.Exists(backupPath))
                    {
                        File.Copy(destPath, backupPath);
                        manifest.BackedUpFiles.Add(relativePath);
                    }
                }

                File.Copy(sourcePath, destPath, true);
                manifest.InstalledFiles.Add(relativePath);
            }

            // Step 3: Install Fakenvapi if requested (AMD/Intel only)
            if (installFakenvapi && !string.IsNullOrEmpty(fakenvapiCachePath) && Directory.Exists(fakenvapiCachePath))
            {
                var fakeFiles = Directory.GetFiles(fakenvapiCachePath, "*.*", SearchOption.AllDirectories);

                foreach (var sourcePath in fakeFiles)
                {
                    var fileName = Path.GetFileName(sourcePath);

                    // Only copy nvapi64.dll and fakenvapi.ini
                    if (fileName.Equals("nvapi64.dll", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Equals("fakenvapi.ini", StringComparison.OrdinalIgnoreCase))
                    {
                        var destPath = Path.Combine(gameDir, fileName);

                        // Backup if exists
                        if (File.Exists(destPath))
                        {
                            var backupPath = Path.Combine(backupDir, fileName);
                            if (!File.Exists(backupPath))
                            {
                                File.Copy(destPath, backupPath);
                                manifest.BackedUpFiles.Add(fileName);
                            }
                        }

                        File.Copy(sourcePath, destPath, true);
                        manifest.InstalledFiles.Add(fileName);
                    }
                }
            }

            // Step 4: Install NukemFG if requested
            if (installNukemFG && !string.IsNullOrEmpty(nukemFGCachePath) && Directory.Exists(nukemFGCachePath))
            {
                var nukemFiles = Directory.GetFiles(nukemFGCachePath, "*.*", SearchOption.AllDirectories);

                foreach (var sourcePath in nukemFiles)
                {
                    var fileName = Path.GetFileName(sourcePath);

                    // ONLY copy dlssg_to_fsr3_amd_is_better.dll
                    // DO NOT copy nvngx.dll (200kb) - it will break the mod!
                    if (fileName.Equals("dlssg_to_fsr3_amd_is_better.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        var destPath = Path.Combine(gameDir, fileName);

                        // Backup if exists
                        if (File.Exists(destPath))
                        {
                            var backupPath = Path.Combine(backupDir, fileName);
                            if (!File.Exists(backupPath))
                            {
                                File.Copy(destPath, backupPath);
                                manifest.BackedUpFiles.Add(fileName);
                            }
                        }

                        File.Copy(sourcePath, destPath, true);
                        manifest.InstalledFiles.Add(fileName);

                        // Modify OptiScaler.ini to set FGType=nukems
                        ModifyOptiScalerIni(gameDir, "FGType", "nukems");
                    }
                }
            }

            // Save manifest
            var manifestPath = Path.Combine(backupDir, ManifestFileName);
            var manifestJson = JsonSerializer.Serialize(manifest, OptimizerContext.Default.InstallationManifest);
            File.WriteAllText(manifestPath, manifestJson);

            // Immediately update the game object so the UI reflects the correct state
            // without waiting for the next full scan/analysis cycle.
            game.IsOptiscalerInstalled = true;
            if (!string.IsNullOrEmpty(optiscalerVersion))
                game.OptiscalerVersion = optiscalerVersion;

            // Post-Install: Re-analyze to refresh DLSS/FSR/XeSS fields.
            // AnalyzeGame will also confirm OptiscalerVersion via the manifest.
            var analyzer = new GameAnalyzerService();
            analyzer.AnalyzeGame(game);
        }

        public void UninstallOptiScaler(Game game)
        {
            // ── Determine candidate root directory ───────────────────────────────
            // We need a starting point to search for the manifest.
            string? rootDir = null;

            if (!string.IsNullOrEmpty(game.ExecutablePath))
                rootDir = Path.GetDirectoryName(game.ExecutablePath);

            if (string.IsNullOrEmpty(rootDir) && !string.IsNullOrEmpty(game.InstallPath))
                rootDir = game.InstallPath;

            if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir))
                throw new Exception($"Invalid game directory: ExecutablePath='{game.ExecutablePath}', InstallPath='{game.InstallPath}'");

            // ── Search for the manifest recursively from the root ─────────────────
            // This is more robust than assuming the path: handles Phoenix/UE5 games
            // where the actual install is in a subdirectory.
            string? manifestPath = null;
            string? gameDir = null;

            try
            {
                var searchOptions = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    MatchCasing = MatchCasing.CaseInsensitive
                };

                var manifests = Directory.GetFiles(rootDir, ManifestFileName, searchOptions);
                if (manifests.Length > 0)
                {
                    manifestPath = manifests[0]; // Use first found manifest
                    // The manifest lives inside OptiScalerBackup/, so its parent == backup dir
                }
            }
            catch { /* If search fails, fall through to legacy */ }

            InstallationManifest? manifest = null;

            if (manifestPath != null && File.Exists(manifestPath))
            {
                try
                {
                    var manifestJson = File.ReadAllText(manifestPath);
                    manifest = JsonSerializer.Deserialize(manifestJson, OptimizerContext.Default.InstallationManifest);
                }
                catch { /* Corrupt manifest — use legacy fallback */ }
            }

            // ── Resolve gameDir ───────────────────────────────────────────────────
            // Priority 1: InstalledGameDirectory stored in manifest (exact path from install time)
            // Priority 2: Parent of the backup directory containing the manifest
            // Priority 3: Re-detect via DetectCorrectInstallDirectory
            if (manifest?.InstalledGameDirectory != null && Directory.Exists(manifest.InstalledGameDirectory))
            {
                gameDir = manifest.InstalledGameDirectory;
            }
            else if (manifestPath != null)
            {
                // Manifest backup dir is {gameDir}/OptiScalerBackup/optiscaler_manifest.json
                // So: parent of manifest → backup dir → parent → gameDir
                gameDir = Path.GetDirectoryName(Path.GetDirectoryName(manifestPath));
            }
            else
            {
                // Last resort: re-detect (same logic as before, may fail for Phoenix games
                // if the executable path is not available)
                gameDir = DetectCorrectInstallDirectory(rootDir);
            }

            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                throw new Exception($"Could not determine installation directory for '{game.Name}'.");

            var backupDir = Path.Combine(gameDir, BackupFolderName);

            if (manifest != null)
            {
                // ── Manifest-based uninstallation (precise) ───────────────────────

                // Step 1: Delete all installed files (OptiScaler + Fakenvapi + NukemFG)
                foreach (var installedFile in manifest.InstalledFiles)
                {
                    try
                    {
                        var filePath = Path.Combine(gameDir, installedFile);
                        if (File.Exists(filePath))
                            File.Delete(filePath);
                    }
                    catch { /* Continue */ }
                }

                // Step 2: Restore backed-up files (files that existed before installation)
                foreach (var backedUpFile in manifest.BackedUpFiles)
                {
                    try
                    {
                        var backupPath = Path.Combine(backupDir, backedUpFile);
                        var destPath = Path.Combine(gameDir, backedUpFile);

                        if (File.Exists(backupPath))
                            File.Copy(backupPath, destPath, overwrite: true);
                    }
                    catch { /* Continue */ }
                }

                // Step 3: Remove installed (now-empty) subdirectories, deepest first
                foreach (var installedDir in manifest.InstalledDirectories.OrderByDescending(d => d.Length))
                {
                    try
                    {
                        var dirPath = Path.Combine(gameDir, installedDir);
                        if (Directory.Exists(dirPath) && !Directory.EnumerateFileSystemEntries(dirPath).Any())
                            Directory.Delete(dirPath, false);
                    }
                    catch { /* Continue */ }
                }

                // Step 4: Remove the backup directory itself
                try
                {
                    if (Directory.Exists(backupDir))
                        Directory.Delete(backupDir, true);
                }
                catch { }
            }
            else
            {
                // ── Legacy fallback (no manifest present) ─────────────────────────
                // Covers installations created before the manifest system was introduced.

                // Collect all directories to scan: gameDir + Phoenix subdir if present
                var dirsToScan = new List<string> { gameDir };
                var phoenixDir = DetectCorrectInstallDirectory(gameDir);
                if (!phoenixDir.Equals(gameDir, StringComparison.OrdinalIgnoreCase))
                    dirsToScan.Add(phoenixDir);

                // Restore backed-up files first
                foreach (var dir in dirsToScan)
                {
                    var legacyBackupDir = Path.Combine(dir, BackupFolderName);
                    if (Directory.Exists(legacyBackupDir))
                    {
                        foreach (var backupFile in Directory.GetFiles(legacyBackupDir, "*.*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                var relativePath = Path.GetRelativePath(legacyBackupDir, backupFile);
                                if (relativePath.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                var destPath = Path.Combine(dir, relativePath);
                                File.Copy(backupFile, destPath, overwrite: true);
                            }
                            catch { }
                        }

                        try { Directory.Delete(legacyBackupDir, true); }
                        catch { }
                    }
                }

                // Delete all known OptiScaler / Fakenvapi / NukemFG files
                var knownFiles = new[]
                {
                    // OptiScaler core
                    "OptiScaler.ini", "OptiScaler.log", "OptiScaler.dll",
                    "dxgi.dll", "winmm.dll", "d3d12.dll", "dbghelp.dll",
                    "version.dll", "wininet.dll", "winhttp.dll",
                    "nvngx.dll", "libxess.dll", "amdxcffx64.dll",
                    // Fakenvapi
                    "nvapi64.dll", "fakenvapi.ini",
                    // NukemFG
                    "dlssg_to_fsr3_amd_is_better.dll"
                };

                foreach (var dir in dirsToScan)
                {
                    var legacyBackupDir = Path.Combine(dir, BackupFolderName);
                    foreach (var fileName in knownFiles)
                    {
                        var filePath = Path.Combine(dir, fileName);
                        if (!File.Exists(filePath)) continue;

                        try
                        {
                            // Always delete OptiScaler config/log
                            if (fileName.StartsWith("OptiScaler", StringComparison.OrdinalIgnoreCase))
                            {
                                File.Delete(filePath);
                                continue;
                            }

                            // For DLLs: only delete if there was no original backup
                            // (backup dir was already deleted above, so !Directory.Exists is true
                            // when there was no backup — safe to delete)
                            var backupPath = Path.Combine(legacyBackupDir, fileName);
                            if (!File.Exists(backupPath) && !Directory.Exists(legacyBackupDir))
                                File.Delete(filePath);
                        }
                        catch { }
                    }
                }
            }

            // Clear game state immediately so the UI reflects the uninstallation
            game.IsOptiscalerInstalled = false;
            game.OptiscalerVersion = null;

            // Re-analyze to refresh DLSS/FSR/XeSS detection after files were removed/restored
            var analyzer = new GameAnalyzerService();
            analyzer.AnalyzeGame(game);
        }

        /// <summary>
        /// Determines the correct installation directory for games based on user rules.
        /// </summary>
        public string? DetermineInstallDirectory(Game game)
        {
            if (string.IsNullOrEmpty(game.InstallPath) || !Directory.Exists(game.InstallPath))
            {
                // If InstallPath is missing, try ExecutablePath
                if (!string.IsNullOrEmpty(game.ExecutablePath) && File.Exists(game.ExecutablePath))
                    return Path.GetDirectoryName(game.ExecutablePath);

                return null;
            }

            // Rule 2: If Phoenix folder is present, ignore step 1 and search inside Phoenix/Binaries/Win64
            var phoenixPath = Path.Combine(game.InstallPath, "Phoenix", "Binaries", "Win64");
            if (Directory.Exists(phoenixPath))
            {
                var phoenixExes = Directory.GetFiles(phoenixPath, "*.exe", SearchOption.TopDirectoryOnly);
                if (phoenixExes.Length > 0)
                {
                    return phoenixPath;
                }
            }

            // Rule 1: Try to extract in the same folder as the main .exe, scan to find it.
            string[] allExes = Array.Empty<string>();
            try
            {
                allExes = Directory.GetFiles(game.InstallPath, "*.exe", SearchOption.AllDirectories);
            }
            catch { }

            string? bestMatchDir = null;

            if (allExes.Length == 1)
            {
                // Only one executable found, must be it
                bestMatchDir = Path.GetDirectoryName(allExes[0]);
            }
            else if (allExes.Length > 1)
            {
                // Multiple executables, try to match by name or context
                int bestScore = -1;
                string? bestExe = null;

                var gameNameLetters = new string(game.Name.Where(char.IsLetterOrDigit).ToArray());

                foreach (var exePath in allExes)
                {
                    var fileName = Path.GetFileNameWithoutExtension(exePath);

                    // Filter out known non-game executables
                    if (fileName.Contains("Crash", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("Redist", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("Setup", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("UnrealCEFSubProcess", StringComparison.OrdinalIgnoreCase))
                        continue;

                    int score = 0;
                    var exeLetters = new string(fileName.Where(char.IsLetterOrDigit).ToArray());

                    if (!string.IsNullOrEmpty(exeLetters) && !string.IsNullOrEmpty(gameNameLetters))
                    {
                        if (exeLetters.Contains(gameNameLetters, StringComparison.OrdinalIgnoreCase) ||
                            gameNameLetters.Contains(exeLetters, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 10;
                        }
                    }

                    if (exePath.Contains(@"Binaries\Win64", StringComparison.OrdinalIgnoreCase))
                    {
                        score += 5;
                    }

                    var exeDir = Path.GetDirectoryName(exePath);
                    if (exeDir != null)
                    {
                        try
                        {
                            var dlls = Directory.GetFiles(exeDir, "*.dll", SearchOption.TopDirectoryOnly);
                            foreach (var dll in dlls)
                            {
                                var dllName = Path.GetFileName(dll).ToLowerInvariant();
                                if (dllName.Contains("amd") || dllName.Contains("fsr") || dllName.Contains("nvngx") || dllName.Contains("dlss") || dllName.Contains("sl.interposer"))
                                {
                                    score += 20; // High confidence if scaling DLLs are nearby
                                    break;
                                }
                            }
                        }
                        catch { }
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestExe = exePath;
                    }
                }

                if (bestExe != null)
                {
                    bestMatchDir = Path.GetDirectoryName(bestExe);
                }

                // Fallback: If no match by name, check known ExecutablePath
                if (bestMatchDir == null)
                {
                    if (!string.IsNullOrEmpty(game.ExecutablePath) && File.Exists(game.ExecutablePath))
                    {
                        bestMatchDir = Path.GetDirectoryName(game.ExecutablePath);
                    }
                    else
                    {
                        var binariesExes = allExes.Where(x => x.Contains(@"Binaries\Win64", StringComparison.OrdinalIgnoreCase)).ToList();
                        if (binariesExes.Count == 1)
                        {
                            bestMatchDir = Path.GetDirectoryName(binariesExes[0]);
                        }
                    }
                }
            }
            else if (allExes.Length == 0 && !string.IsNullOrEmpty(game.ExecutablePath) && File.Exists(game.ExecutablePath))
            {
                // Fallback if Directory.GetFiles fails but we have an ExecutablePath
                bestMatchDir = Path.GetDirectoryName(game.ExecutablePath);
            }

            if (bestMatchDir != null && Directory.Exists(bestMatchDir))
            {
                return bestMatchDir;
            }

            // Fallback to the main install path, if nothing else works
            return game.InstallPath;
        }

        private string? ShowFolderSelectionDialog(Game game)
        {
            string? selectedPath = null;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = $"Select the main executable (.exe) for '{game.Name}'",
                    InitialDirectory = game.InstallPath,
                    Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() == true)
                {
                    selectedPath = Path.GetDirectoryName(dialog.FileName);
                }
            });

            return selectedPath;
        }

        /// <summary>
        /// Detects the correct installation directory fallback for older uninstalls.
        /// </summary>
        private string DetectCorrectInstallDirectory(string baseDir)
        {
            // Check for UE5 Phoenix structure: Phoenix/Binaries/Win64
            var phoenixPath = Path.Combine(baseDir, "Phoenix", "Binaries", "Win64");
            if (Directory.Exists(phoenixPath))
            {
                return phoenixPath;
            }

            // Check for generic UE structure: GameName/Binaries/Win64
            var binariesPath = Path.Combine(baseDir, "Binaries", "Win64");
            if (Directory.Exists(binariesPath))
            {
                return binariesPath;
            }

            // Return original path if no special structure detected
            return baseDir;
        }

        /// <summary>
        /// Modifies a setting in OptiScaler.ini
        /// </summary>
        private void ModifyOptiScalerIni(string gameDir, string key, string value)
        {
            var iniPath = Path.Combine(gameDir, "OptiScaler.ini");

            if (!File.Exists(iniPath))
            {
                // Create a basic ini file if it doesn't exist
                File.WriteAllText(iniPath, $"[General]\n{key}={value}\n");
                return;
            }

            try
            {
                var lines = File.ReadAllLines(iniPath).ToList();
                bool keyFound = false;
                bool inGeneralSection = false;

                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i].Trim();

                    // Check if we're in [General] section
                    if (line.Equals("[General]", StringComparison.OrdinalIgnoreCase))
                    {
                        inGeneralSection = true;
                        continue;
                    }

                    // Check if we've moved to another section
                    if (line.StartsWith("[") && !line.Equals("[General]", StringComparison.OrdinalIgnoreCase))
                    {
                        if (inGeneralSection && !keyFound)
                        {
                            // Insert the key before the next section
                            lines.Insert(i, $"{key}={value}");
                            keyFound = true;
                            break;
                        }
                        inGeneralSection = false;
                    }

                    // If we're in General section and found the key, update it
                    if (inGeneralSection && line.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"{key}={value}";
                        keyFound = true;
                        break;
                    }
                }

                // If key wasn't found, add it to the end of [General] section or create it
                if (!keyFound)
                {
                    if (inGeneralSection)
                    {
                        lines.Add($"{key}={value}");
                    }
                    else
                    {
                        // Add [General] section if it doesn't exist
                        lines.Add("[General]");
                        lines.Add($"{key}={value}");
                    }
                }

                File.WriteAllLines(iniPath, lines);
            }
            catch
            {
                // If modification fails, try to create a new file
                File.WriteAllText(iniPath, $"[General]\n{key}={value}\n");
            }
        }
    }
}
