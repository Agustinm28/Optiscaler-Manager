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

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using OptiscalerClient.Models;
using OptiscalerClient.Views;

namespace OptiscalerClient.Services
{
    public class GameInstallationService
    {
        private const string BackupFolderName = "OptiScalerBackup";
        private const string ManifestFileName = "optiscaler_manifest.json";
        private static readonly string[] KnownOptiscalerArtifacts =
        {
            // OptiScaler core
            "OptiScaler.ini", "OptiScaler.log", "OptiScaler.dll",
            "dxgi.dll", "winmm.dll", "d3d12.dll", "dbghelp.dll",
            "version.dll", "wininet.dll", "winhttp.dll",
            "nvngx.dll", "libxess.dll", "amdxcffx64.dll",
            // Fakenvapi
            "nvapi64.dll", "fakenvapi.ini", "fakenvapi.log",
            // NukemFG
            "dlssg_to_fsr3_amd_is_better.dll",
            // FSR 4 INT8 mod
            "amd_fidelityfx_upscaler_dx12.dll",
            // OptiPatcher
            @"plugins\OptiPatcher.asi"
        };

        // Files that we want to track specifically for backup purposes if they exist in the game folder
        // essentially anything that OptiScaler might replace.
        // We will backup ANYTHING we overwrite, but these are known criticals.
        private readonly string[] _criticalFiles = { "dxgi.dll", "version.dll", "winmm.dll", "nvngx.dll", "nvngx_dlssg.dll", "libxess.dll" };

        public void InstallOptiScaler(Game game, string cachePath, string injectionDllName = "dxgi.dll",
                                     bool installFakenvapi = false, string fakenvapiCachePath = "",
                                     bool installNukemFG = false, string nukemFGCachePath = "",
                                     string? optiscalerVersion = null,
                                     string? overrideGameDir = null,
                                     OptiScalerProfile? profile = null)
        {
            DebugWindow.Log($"[Install] Starting OptiScaler installation for game: {game.Name}");
            DebugWindow.Log($"[Install] Version: {optiscalerVersion}, Injection: {injectionDllName}");
            DebugWindow.Log($"[Install] Cache path: {cachePath}");

            if (!Directory.Exists(cachePath))
                throw new DirectoryNotFoundException("Updates cache directory not found. Please download OptiScaler first.");

            // Verify cache is not empty
            var cacheFiles = Directory.GetFiles(cachePath, "*.*", SearchOption.AllDirectories);
            if (cacheFiles.Length == 0)
                throw new Exception("Cache directory is empty. Download update again.");

            DebugWindow.Log($"[Install] Cache contains {cacheFiles.Length} files");

            // Determine game directory intelligently (rules for base exe, Phoenix override, or user modal)
            string? gameDir;
            if (overrideGameDir != null)
            {
                gameDir = overrideGameDir;
                DebugWindow.Log($"[Install] Using override game directory: {gameDir}");
            }
            else
            {
                gameDir = DetermineInstallDirectory(game);
                if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                {
                    throw new Exception("Could not automatically detect the game directory. Please use Manual Install.");
                }
                DebugWindow.Log($"[Install] Detected game directory: {gameDir}");
            }

            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                throw new Exception("Installation cancelled or valid directory not found.");

            var backupDir = Path.Combine(gameDir, BackupFolderName);
            DebugWindow.Log($"[Install] Backup directory: {backupDir}");

            // Create backup folder
            if (!Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(backupDir);
                DebugWindow.Log($"[Install] Created backup directory");
            }

            // Create installation manifest — OptiscalerVersion is the authoritative source for the UI
            var manifest = new InstallationManifest
            {
                OperationId = Guid.NewGuid().ToString("N"),
                OperationStatus = "in_progress",
                StartedAtUtc = DateTime.UtcNow.ToString("O"),
                InjectionMethod = injectionDllName,
                InstallDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                OptiscalerVersion = optiscalerVersion,
                IncludesOptiscaler = true,
                IncludesFakenvapi = installFakenvapi,
                IncludesNukemFG = installNukemFG,
                // Store the EXACT directory used (already resolved for Phoenix/UE5 games).
                // Uninstall will read this directly, avoiding re-detection issues.
                InstalledGameDirectory = gameDir
            };

            manifest.PreInstallKeyFiles = CapturePreInstallKeySnapshot(gameDir, injectionDllName);
            manifest.ExpectedFinalMarkers.Add(injectionDllName);
            manifest.ExpectedFinalMarkers.Add(Path.Combine(BackupFolderName, ManifestFileName));
            manifest.AppliedProfileName = profile?.Name;
            var manifestPath = Path.Combine(backupDir, ManifestFileName);

            // Persist immediately as in-progress so crashes can be recovered later.
            SaveManifest(manifestPath, manifest);

            try
            {

            // Find the main OptiScaler DLL (OptiScaler.dll or nvngx.dll for older versions)
            string? optiscalerMainDll = null;
            foreach (var file in cacheFiles)
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("nvngx.dll", StringComparison.OrdinalIgnoreCase))
                {
                    optiscalerMainDll = file;
                    DebugWindow.Log($"[Install] Found main OptiScaler DLL: {fileName}");
                    break;
                }
            }

            if (optiscalerMainDll == null)
                throw new Exception("Installation failed because the downloaded package is corrupt or incomplete (missing OptiScaler.dll). Please go to Settings -> Manage Cache, delete this version, and try the installation again.");

            // Step 1: Install the main OptiScaler DLL with the selected injection method name
            var injectionDllPath = Path.Combine(gameDir, injectionDllName);
            DebugWindow.Log($"[Install] Installing main DLL as: {injectionDllName}");
            var injectionExisted = File.Exists(injectionDllPath);
            var injectionPreHash = injectionExisted ? ComputeSha256(injectionDllPath) : null;

            // Backup existing file if it exists
            if (injectionExisted)
            {
                var backupPath = Path.Combine(backupDir, injectionDllName);
                var backupSubDir = Path.GetDirectoryName(backupPath);
                if (backupSubDir != null && !Directory.Exists(backupSubDir))
                    Directory.CreateDirectory(backupSubDir);

                if (!File.Exists(backupPath))
                {
                    File.Copy(injectionDllPath, backupPath);
                    manifest.BackedUpFiles.Add(injectionDllName);
                    DebugWindow.Log($"[Install] Backed up existing file: {injectionDllName}");
                }
            }

            // Copy OptiScaler.dll as the injection DLL
            File.Copy(optiscalerMainDll, injectionDllPath, true);
            manifest.InstalledFiles.Add(injectionDllName);
            TrackManifestFileMutation(
                manifest,
                relativePath: injectionDllName,
                existedBefore: injectionExisted,
                preInstallHash: injectionPreHash,
                postInstallHash: ComputeSha256(injectionDllPath));
            DebugWindow.Log($"[Install] Installed main OptiScaler DLL");

            // Step 2: Copy all other files (configs, dependencies, etc.)
            DebugWindow.Log($"[Install] Copying additional files...");
            var additionalFileCount = 0;

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
                    DebugWindow.Log($"[Install] Created directory: {Path.GetRelativePath(gameDir, destDir)}");

                    // Add to manifest (relative to game directory)
                    var relativeDir = Path.GetRelativePath(gameDir, destDir);
                    if (!manifest.InstalledDirectories.Contains(relativeDir))
                    {
                        manifest.InstalledDirectories.Add(relativeDir);
                    }
                }

                // Backup existing file if needed
                bool existedBefore = File.Exists(destPath);
                var preHash = existedBefore ? ComputeSha256(destPath) : null;
                if (existedBefore)
                {
                    var backupPath = Path.Combine(backupDir, relativePath);
                    var backupSubDir = Path.GetDirectoryName(backupPath);
                    if (backupSubDir != null && !Directory.Exists(backupSubDir))
                        Directory.CreateDirectory(backupSubDir);

                    if (!File.Exists(backupPath))
                    {
                        File.Copy(destPath, backupPath);
                        manifest.BackedUpFiles.Add(relativePath);
                        DebugWindow.Log($"[Install] Backed up existing file: {relativePath}");
                    }
                }

                File.Copy(sourcePath, destPath, true);
                manifest.InstalledFiles.Add(relativePath);
                TrackManifestFileMutation(
                    manifest,
                    relativePath: relativePath,
                    existedBefore: existedBefore,
                    preInstallHash: preHash,
                    postInstallHash: ComputeSha256(destPath));
                additionalFileCount++;
            }

            DebugWindow.Log($"[Install] Copied {additionalFileCount} additional files");

            // Step 2.5: Generate OptiScaler.ini from profile if provided (skip for Default profile)
            if (profile != null && profile.IniSettings.Count > 0)
            {
                try
                {
                    var profileService = new ProfileManagementService();
                    profileService.WriteOptiScalerIniToFile(gameDir, profile);
                    DebugWindow.Log($"[Install] Generated OptiScaler.ini from profile: {profile.Name}");
                }
                catch (Exception ex)
                {
                    DebugWindow.Log($"[Install] Warning: Failed to generate OptiScaler.ini from profile: {ex.Message}");
                }
            }
            else if (profile != null && profile.Name == "Default")
            {
                DebugWindow.Log($"[Install] Using Default profile - OptiScaler will use its default configuration");
            }

            // Step 3: Install Fakenvapi if requested (AMD/Intel only)
            if (installFakenvapi && !string.IsNullOrEmpty(fakenvapiCachePath) && Directory.Exists(fakenvapiCachePath))
            {
                DebugWindow.Log($"[Install] Installing Fakenvapi...");
                var fakeFiles = Directory.GetFiles(fakenvapiCachePath, "*.*", SearchOption.AllDirectories);
                var fakeFileCount = 0;

                foreach (var sourcePath in fakeFiles)
                {
                    var fileName = Path.GetFileName(sourcePath);

                    // Only copy nvapi64.dll and fakenvapi.ini
                    if (fileName.Equals("nvapi64.dll", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Equals("fakenvapi.ini", StringComparison.OrdinalIgnoreCase))
                    {
                        var destPath = Path.Combine(gameDir, fileName);
                        var existedBefore = File.Exists(destPath);
                        var preHash = existedBefore ? ComputeSha256(destPath) : null;

                        // Backup if exists
                        if (existedBefore)
                        {
                            var backupPath = Path.Combine(backupDir, fileName);
                            if (!File.Exists(backupPath))
                            {
                                File.Copy(destPath, backupPath);
                                manifest.BackedUpFiles.Add(fileName);
                                DebugWindow.Log($"[Install] Backed up existing Fakenvapi file: {fileName}");
                            }
                        }

                        File.Copy(sourcePath, destPath, true);
                        manifest.InstalledFiles.Add(fileName);
                        TrackManifestFileMutation(
                            manifest,
                            relativePath: fileName,
                            existedBefore: existedBefore,
                            preInstallHash: preHash,
                            postInstallHash: ComputeSha256(destPath));
                        fakeFileCount++;
                        DebugWindow.Log($"[Install] Installed Fakenvapi file: {fileName}");
                    }
                }

                DebugWindow.Log($"[Install] Installed {fakeFileCount} Fakenvapi files");
                if (fakeFileCount > 0)
                {
                    manifest.IncludesFakenvapi = true;
                    manifest.ExpectedFinalMarkers.Add("nvapi64.dll");
                }
                else
                {
                    throw new Exception("Installation failed because the Fakenvapi package is corrupt or incomplete.");
                }
            }

            // Step 4: Install NukemFG if requested
            if (installNukemFG && !string.IsNullOrEmpty(nukemFGCachePath) && Directory.Exists(nukemFGCachePath))
            {
                DebugWindow.Log($"[Install] Installing NukemFG...");
                var nukemFiles = Directory.GetFiles(nukemFGCachePath, "*.*", SearchOption.AllDirectories);
                var nukemFileCount = 0;

                foreach (var sourcePath in nukemFiles)
                {
                    var fileName = Path.GetFileName(sourcePath);

                    // ONLY copy dlssg_to_fsr3_amd_is_better.dll
                    // DO NOT copy nvngx.dll (200kb) - it will break the mod!
                    if (fileName.Equals("dlssg_to_fsr3_amd_is_better.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        var destPath = Path.Combine(gameDir, fileName);
                        var existedBefore = File.Exists(destPath);
                        var preHash = existedBefore ? ComputeSha256(destPath) : null;

                        // Backup if exists
                        if (existedBefore)
                        {
                            var backupPath = Path.Combine(backupDir, fileName);
                            if (!File.Exists(backupPath))
                            {
                                File.Copy(destPath, backupPath);
                                manifest.BackedUpFiles.Add(fileName);
                                DebugWindow.Log($"[Install] Backed up existing NukemFG file: {fileName}");
                            }
                        }

                        File.Copy(sourcePath, destPath, true);
                        manifest.InstalledFiles.Add(fileName);
                        TrackManifestFileMutation(
                            manifest,
                            relativePath: fileName,
                            existedBefore: existedBefore,
                            preInstallHash: preHash,
                            postInstallHash: ComputeSha256(destPath));
                        nukemFileCount++;
                        DebugWindow.Log($"[Install] Installed NukemFG file: {fileName}");

                        // Modify OptiScaler.ini to set FGType=nukems
                        ModifyOptiScalerIni(gameDir, "FGType", "nukems");
                        DebugWindow.Log($"[Install] Modified OptiScaler.ini for NukemFG");
                    }
                }

                DebugWindow.Log($"[Install] Installed {nukemFileCount} NukemFG files");
                if (nukemFileCount > 0)
                {
                    manifest.IncludesNukemFG = true;
                    manifest.ExpectedFinalMarkers.Add("dlssg_to_fsr3_amd_is_better.dll");
                }
                else
                {
                    throw new Exception("Installation failed because the NukemFG package is corrupt or incomplete.");
                }
            }

            // Save manifest
            manifest.ExpectedFinalMarkers = manifest.ExpectedFinalMarkers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            manifest.OperationStatus = "committed";
            manifest.FinishedAtUtc = DateTime.UtcNow.ToString("O");
            SaveManifest(manifestPath, manifest);
            DebugWindow.Log($"[Install] Saved installation manifest");

            // Immediately update the game object so the UI reflects the correct state
            // without waiting for the next full scan/analysis cycle.
            game.IsOptiscalerInstalled = true;
            if (!string.IsNullOrEmpty(optiscalerVersion))
                game.OptiscalerVersion = optiscalerVersion;

            // Post-Install: Re-analyze to refresh DLSS/FSR/XeSS fields.
            // AnalyzeGame will also confirm OptiscalerVersion via the manifest.
            DebugWindow.Log($"[Install] Re-analyzing game to update component information...");
            var analyzer = new GameAnalyzerService();
            GameAnalyzerService.InvalidateCacheForPath(game.InstallPath);
            analyzer.AnalyzeGame(game, forceRefresh: true);
            GameAnalyzerService.FlushCacheToDisk();

            DebugWindow.Log($"[Install] OptiScaler installation completed successfully for {game.Name}");
            DebugWindow.Log($"[Install] Total files installed: {manifest.InstalledFiles.Count}");
            DebugWindow.Log($"[Install] Total files backed up: {manifest.BackedUpFiles.Count}");
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[Install] Installation failed. Starting rollback. Error: {ex.Message}");

                manifest.OperationStatus = "failed";
                manifest.FinishedAtUtc = DateTime.UtcNow.ToString("O");
                SaveManifest(manifestPath, manifest);

                var rollbackSummary = RollbackFailedInstall(gameDir, backupDir, manifest);
                DebugWindow.Log($"[Install] Rollback completed. Restored={rollbackSummary.Restored}, Deleted={rollbackSummary.Deleted}");

                throw new Exception($"{ex.Message}", ex);
            }
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
            catch (Exception ex)
            {
                DebugWindow.Log($"[Uninstall] Manifest search failed, using legacy fallback: {ex.Message}");
            }

            InstallationManifest? manifest = null;

            if (manifestPath != null && File.Exists(manifestPath))
            {
                try
                {
                    var manifestJson = File.ReadAllText(manifestPath);
                    manifest = JsonSerializer.Deserialize(manifestJson, OptimizerContext.Default.InstallationManifest);
                }
                catch (Exception ex)
                {
                    DebugWindow.Log($"[Uninstall] Corrupt manifest at '{manifestPath}': {ex.Message}");
                }
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

                // Step 1: Delete files created by install.
                // If v2 tracking is unavailable, fall back to legacy InstalledFiles list.
                var filesToDelete = manifest.FilesCreated.Count > 0
                    ? manifest.FilesCreated.Select(f => f.RelativePath)
                    : manifest.InstalledFiles;

                foreach (var installedFile in filesToDelete.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var filePath = Path.Combine(gameDir, installedFile);
                        if (File.Exists(filePath))
                            File.Delete(filePath);
                    }
                    catch (Exception ex) { DebugWindow.Log($"[Uninstall] Failed to delete '{installedFile}': {ex.Message}"); }
                }

                // Step 2: Restore overwritten files from backup.
                // If v2 tracking is unavailable, fall back to legacy BackedUpFiles list.
                if (manifest.FilesOverwritten.Count > 0)
                {
                    foreach (var overwritten in manifest.FilesOverwritten)
                    {
                        try
                        {
                            var backupRelative = string.IsNullOrWhiteSpace(overwritten.BackupRelativePath)
                                ? overwritten.RelativePath
                                : overwritten.BackupRelativePath;
                            var backupPath = Path.Combine(backupDir, backupRelative);
                            var destPath = Path.Combine(gameDir, overwritten.RelativePath);

                            if (File.Exists(backupPath))
                                File.Copy(backupPath, destPath, overwrite: true);
                        }
                        catch (Exception ex) { DebugWindow.Log($"[Uninstall] Failed to restore '{overwritten.RelativePath}': {ex.Message}"); }
                    }
                }
                else
                {
                    foreach (var backedUpFile in manifest.BackedUpFiles)
                    {
                        try
                        {
                            var backupPath = Path.Combine(backupDir, backedUpFile);
                            var destPath = Path.Combine(gameDir, backedUpFile);

                            if (File.Exists(backupPath))
                                File.Copy(backupPath, destPath, overwrite: true);
                        }
                        catch (Exception ex) { DebugWindow.Log($"[Uninstall] Failed to restore backup '{backedUpFile}': {ex.Message}"); }
                    }
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
                    catch (Exception ex) { DebugWindow.Log($"[Uninstall] Failed to remove directory '{installedDir}': {ex.Message}"); }
                }

                // NOTE: Backup directory is NOT deleted here — ForceRemoveAllArtifacts
                // will clean it up after ValidateAndHealPostUninstall has had a chance
                // to use the backups for restoration.
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
                            catch (Exception ex) { DebugWindow.Log($"[Uninstall] Failed to restore legacy backup file: {ex.Message}"); }
                        }

                        try { Directory.Delete(legacyBackupDir, true); }
                        catch (Exception ex) { DebugWindow.Log($"[Uninstall] Failed to delete legacy backup dir: {ex.Message}"); }
                    }
                }

                foreach (var dir in dirsToScan)
                {
                    var legacyBackupDir = Path.Combine(dir, BackupFolderName);
                    foreach (var fileName in KnownOptiscalerArtifacts)
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
                        catch (Exception ex) { DebugWindow.Log($"[Uninstall] Failed to clean legacy artifact '{fileName}': {ex.Message}"); }
                    }
                }
            }

            // We verify expected state and try to heal residues without touching files
            // that existed before installation.
            ValidateAndHealPostUninstall(gameDir, backupDir, manifest);

            // Final unconditional sweep: remove ALL known OptiScaler artifacts.
            // This guarantees a clean game directory regardless of manifest state,
            // interrupted installs, or corrupted tracking data.
            ForceRemoveAllArtifacts(gameDir);

            // Last resort: compare every file in the cached OptiScaler version against
            // the game directory. If any cached file still exists in the game dir, it
            // means the previous steps missed it — delete it unconditionally.
            SweepResidualFilesFromCache(gameDir, manifest);

            // Clear game state immediately so the UI reflects the uninstallation
            game.IsOptiscalerInstalled = false;
            game.OptiscalerVersion = null;
            game.Fsr4ExtraVersion = null;

            // Re-analyze to refresh DLSS/FSR/XeSS detection after files were removed/restored
            var analyzer = new GameAnalyzerService();
            GameAnalyzerService.InvalidateCacheForPath(game.InstallPath);
            analyzer.AnalyzeGame(game, forceRefresh: true);
            GameAnalyzerService.FlushCacheToDisk();
        }

        public bool RecoverIncompleteInstallIfNeeded(string installRoot)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                return false;

            string? manifestPath = null;
            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    MatchCasing = MatchCasing.CaseInsensitive
                };

                manifestPath = Directory.GetFiles(installRoot, ManifestFileName, options).FirstOrDefault();
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
                return false;

            InstallationManifest? manifest;
            try
            {
                var manifestJson = File.ReadAllText(manifestPath);
                manifest = JsonSerializer.Deserialize(manifestJson, OptimizerContext.Default.InstallationManifest);
            }
            catch
            {
                return false;
            }

            if (manifest == null)
                return false;

            if (string.Equals(manifest.OperationStatus, "committed", StringComparison.OrdinalIgnoreCase))
                return false;

            var gameDir = !string.IsNullOrWhiteSpace(manifest.InstalledGameDirectory) &&
                          Directory.Exists(manifest.InstalledGameDirectory)
                ? manifest.InstalledGameDirectory
                : Path.GetDirectoryName(Path.GetDirectoryName(manifestPath));

            if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
                return false;

            var backupDir = Path.Combine(gameDir, BackupFolderName);
            DebugWindow.Log($"[Recovery] Found incomplete install manifest (status={manifest.OperationStatus}). Starting recovery for: {gameDir}");

            var rollbackSummary = RollbackFailedInstall(gameDir, backupDir, manifest);
            ValidateAndHealPostUninstall(gameDir, backupDir, manifest);

            DebugWindow.Log($"[Recovery] Completed. Restored={rollbackSummary.Restored}, Deleted={rollbackSummary.Deleted}");
            return true;
        }

        private List<KeyFileSnapshot> CapturePreInstallKeySnapshot(string gameDir, string injectionDllName)
        {
            var keys = new HashSet<string>(_criticalFiles, StringComparer.OrdinalIgnoreCase)
            {
                injectionDllName,
                "OptiScaler.ini",
                "nvapi64.dll",
                "dlssg_to_fsr3_amd_is_better.dll",
                "amd_fidelityfx_upscaler_dx12.dll"
            };

            var snapshots = new List<KeyFileSnapshot>();
            foreach (var relPath in keys)
            {
                var fullPath = Path.Combine(gameDir, relPath);
                var existed = File.Exists(fullPath);
                snapshots.Add(new KeyFileSnapshot
                {
                    RelativePath = relPath,
                    Existed = existed,
                    Sha256 = existed ? ComputeSha256(fullPath) : null
                });
            }

            return snapshots.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void TrackManifestFileMutation(
            InstallationManifest manifest,
            string relativePath,
            bool existedBefore,
            string? preInstallHash,
            string? postInstallHash)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return;

            if (existedBefore)
            {
                var record = manifest.FilesOverwritten.FirstOrDefault(
                    x => x.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));

                if (record == null)
                {
                    record = new ManifestFileRecord
                    {
                        RelativePath = relativePath,
                        BackupRelativePath = relativePath
                    };
                    manifest.FilesOverwritten.Add(record);
                }

                record.ExistedBefore = true;
                record.PreInstallSha256 = preInstallHash;
                record.PostInstallSha256 = postInstallHash;
            }
            else
            {
                var record = manifest.FilesCreated.FirstOrDefault(
                    x => x.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));

                if (record == null)
                {
                    record = new ManifestFileRecord
                    {
                        RelativePath = relativePath,
                        BackupRelativePath = null
                    };
                    manifest.FilesCreated.Add(record);
                }

                record.ExistedBefore = false;
                record.PreInstallSha256 = null;
                record.PostInstallSha256 = postInstallHash;
            }
        }

        private static string? ComputeSha256(string filePath)
        {
            try
            {
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = sha.ComputeHash(stream);
                return Convert.ToHexString(hash);
            }
            catch
            {
                return null;
            }
        }

        private static void SaveManifest(string manifestPath, InstallationManifest manifest)
        {
            var manifestJson = JsonSerializer.Serialize(manifest, OptimizerContext.Default.InstallationManifest);
            File.WriteAllText(manifestPath, manifestJson);
        }

        private RollbackResult RollbackFailedInstall(string gameDir, string backupDir, InstallationManifest manifest)
        {
            var result = new RollbackResult();

            foreach (var record in manifest.FilesCreated)
            {
                var fullPath = Path.Combine(gameDir, record.RelativePath);
                if (TryDeleteFileIfExists(fullPath))
                    result.Deleted++;
            }

            foreach (var record in manifest.FilesOverwritten)
            {
                if (!record.ExistedBefore)
                    continue;

                if (TryRestoreFromBackup(gameDir, backupDir, record.RelativePath, record.BackupRelativePath))
                    result.Restored++;
            }

            if (manifest.FilesCreated.Count == 0)
            {
                foreach (var rel in manifest.InstalledFiles.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var fullPath = Path.Combine(gameDir, rel);
                    if (TryDeleteFileIfExists(fullPath))
                        result.Deleted++;
                }
            }

            if (manifest.FilesOverwritten.Count == 0)
            {
                foreach (var rel in manifest.BackedUpFiles.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (TryRestoreFromBackup(gameDir, backupDir, rel, rel))
                        result.Restored++;
                }
            }

            return result;
        }

        private void ValidateAndHealPostUninstall(string gameDir, string backupDir, InstallationManifest? manifest)
        {
            var preInstallState = BuildPreInstallStateMap(manifest);
            var deletedResidues = 0;
            var restoredFiles = 0;

            // 1) Ensure files created by install are removed.
            if (manifest != null)
            {
                foreach (var record in manifest.FilesCreated)
                {
                    var fullPath = Path.Combine(gameDir, record.RelativePath);
                    if (TryDeleteFileIfExists(fullPath))
                    {
                        deletedResidues++;
                        DebugWindow.Log($"[Uninstall][Validate] Removed residue created by install: {record.RelativePath}");
                    }
                }

                // 2) Ensure overwritten files were restored.
                foreach (var record in manifest.FilesOverwritten)
                {
                    if (!record.ExistedBefore)
                        continue;

                    var targetPath = Path.Combine(gameDir, record.RelativePath);
                    var currentHash = File.Exists(targetPath) ? ComputeSha256(targetPath) : null;
                    var hashMismatch = !string.IsNullOrEmpty(record.PreInstallSha256) &&
                                       !string.Equals(record.PreInstallSha256, currentHash, StringComparison.OrdinalIgnoreCase);

                    if (!File.Exists(targetPath) || hashMismatch)
                    {
                        if (TryRestoreFromBackup(gameDir, backupDir, record.RelativePath, record.BackupRelativePath))
                        {
                            restoredFiles++;
                            DebugWindow.Log($"[Uninstall][Validate] Restored overwritten file from backup: {record.RelativePath}");
                        }
                    }
                }
            }

            // 3) Fallback sweep over known artifacts.
            // If a file existed before install, we try to keep/restore it.
            // If it did not exist before install, we remove it as residue.
            foreach (var relativePath in KnownOptiscalerArtifacts)
            {
                var fullPath = Path.Combine(gameDir, relativePath);
                if (!File.Exists(fullPath))
                    continue;

                if (preInstallState.TryGetValue(relativePath, out var snapshot) && snapshot.Existed)
                {
                    var currentHash = ComputeSha256(fullPath);
                    var expectedHash = snapshot.Sha256;
                    var mismatch = !string.IsNullOrEmpty(expectedHash) &&
                                   !string.Equals(expectedHash, currentHash, StringComparison.OrdinalIgnoreCase);

                    if (mismatch && TryRestoreFromBackup(gameDir, backupDir, relativePath, relativePath))
                    {
                        restoredFiles++;
                        DebugWindow.Log($"[Uninstall][Validate] Restored key file to pre-install state: {relativePath}");
                    }
                }
                else
                {
                    if (TryDeleteFileIfExists(fullPath))
                    {
                        deletedResidues++;
                        DebugWindow.Log($"[Uninstall][Validate] Removed known residue: {relativePath}");
                    }
                }
            }

            // NOTE: Backup directory cleanup is now handled by ForceRemoveAllArtifacts.

            DebugWindow.Log($"[Uninstall][Validate] Validation completed. Restored={restoredFiles}, ResiduesRemoved={deletedResidues}");
        }

        /// <summary>
        /// Final unconditional sweep that removes ALL known OptiScaler artifacts from the
        /// game directory. This runs as the absolute last step of uninstall to guarantee a
        /// clean game directory regardless of manifest state, interrupted installs, double
        /// installs, or corrupted tracking data.
        ///
        /// By this point, the smart cleanup (manifest-based restore + ValidateAndHealPostUninstall)
        /// has already attempted to restore game originals from backup. Any file still matching
        /// a known artifact name is treated as an OptiScaler residue and deleted unconditionally.
        ///
        /// If a game shipped with files that share names with OptiScaler artifacts (e.g.
        /// nvngx.dll, libxess.dll), they may be deleted — the user can restore them via
        /// their game launcher's "Verify Files" feature.
        /// </summary>
        private void ForceRemoveAllArtifacts(string gameDir)
        {
            var dirsToScan = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { gameDir };
            var phoenixDir = DetectCorrectInstallDirectory(gameDir);
            if (!phoenixDir.Equals(gameDir, StringComparison.OrdinalIgnoreCase))
                dirsToScan.Add(phoenixDir);

            var deletedCount = 0;

            foreach (var dir in dirsToScan)
            {
                // Delete every known artifact unconditionally
                foreach (var artifact in KnownOptiscalerArtifacts)
                {
                    var fullPath = Path.Combine(dir, artifact);
                    try
                    {
                        if (File.Exists(fullPath))
                        {
                            File.Delete(fullPath);
                            deletedCount++;
                            DebugWindow.Log($"[Uninstall][ForceClean] Deleted: {artifact}");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugWindow.Log($"[Uninstall][ForceClean] Could not delete '{artifact}': {ex.Message}");
                    }
                }

                // Remove backup directory
                var backupDir = Path.Combine(dir, BackupFolderName);
                try
                {
                    if (Directory.Exists(backupDir))
                    {
                        Directory.Delete(backupDir, true);
                        DebugWindow.Log("[Uninstall][ForceClean] Removed backup directory.");
                    }
                }
                catch (Exception ex)
                {
                    DebugWindow.Log($"[Uninstall][ForceClean] Could not remove backup directory: {ex.Message}");
                }

                // Remove plugins directory if empty
                var pluginsDir = Path.Combine(dir, "plugins");
                try
                {
                    if (Directory.Exists(pluginsDir) && !Directory.EnumerateFileSystemEntries(pluginsDir).Any())
                    {
                        Directory.Delete(pluginsDir, false);
                        DebugWindow.Log("[Uninstall][ForceClean] Removed empty plugins directory.");
                    }
                }
                catch (Exception ex)
                {
                    DebugWindow.Log($"[Uninstall][ForceClean] Could not remove plugins directory: {ex.Message}");
                }
            }

            DebugWindow.Log($"[Uninstall][ForceClean] Final sweep completed. Artifacts removed: {deletedCount}");
        }

        /// <summary>
        /// Compares the actual files in the cached OptiScaler version (and component caches)
        /// against what remains in the game directory. Any file whose name matches a cached
        /// file is deleted unconditionally. This catches files not in KnownOptiscalerArtifacts
        /// (e.g. new DLLs added in future OptiScaler versions, setup scripts, readme files,
        /// subdirectories like D3D12_Optiscaler/, Licenses/, etc.).
        /// </summary>
        private void SweepResidualFilesFromCache(string gameDir, InstallationManifest? manifest)
        {
            var componentService = new ComponentManagementService();
            var cacheDirs = new List<string>();

            // Resolve the OptiScaler version cache directory
            var version = manifest?.OptiscalerVersion;
            if (!string.IsNullOrEmpty(version))
            {
                var optiCachePath = componentService.GetOptiScalerCachePath(version);
                if (Directory.Exists(optiCachePath))
                    cacheDirs.Add(optiCachePath);
            }

            // Also check Fakenvapi, NukemFG, Extras and OptiPatcher caches
            var fakenvapiCache = componentService.GetFakenvapiCachePath();
            if (Directory.Exists(fakenvapiCache))
                cacheDirs.Add(fakenvapiCache);

            var nukemCache = componentService.GetNukemFGCachePath();
            if (Directory.Exists(nukemCache))
                cacheDirs.Add(nukemCache);

            if (cacheDirs.Count == 0)
            {
                DebugWindow.Log("[Uninstall][CacheSweep] No cache directories found — skipping cache-based sweep.");
                return;
            }

            // Build the set of relative paths from all cache directories
            var cachedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cacheDir in cacheDirs)
            {
                try
                {
                    foreach (var entry in Directory.GetFileSystemEntries(cacheDir, "*", SearchOption.AllDirectories))
                    {
                        var relativePath = Path.GetRelativePath(cacheDir, entry);
                        cachedRelativePaths.Add(relativePath);
                    }
                }
                catch (Exception ex)
                {
                    DebugWindow.Log($"[Uninstall][CacheSweep] Error enumerating cache dir '{cacheDir}': {ex.Message}");
                }
            }

            if (cachedRelativePaths.Count == 0)
            {
                DebugWindow.Log("[Uninstall][CacheSweep] Cache directories are empty — skipping.");
                return;
            }

            // Collect all game directories to scan (main + Phoenix/UE5 subdirs)
            var dirsToScan = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { gameDir };
            var phoenixDir = DetectCorrectInstallDirectory(gameDir);
            if (!phoenixDir.Equals(gameDir, StringComparison.OrdinalIgnoreCase))
                dirsToScan.Add(phoenixDir);

            var deletedCount = 0;

            foreach (var dir in dirsToScan)
            {
                foreach (var relativePath in cachedRelativePaths)
                {
                    var fullPath = Path.Combine(dir, relativePath);

                    // Delete files
                    try
                    {
                        if (File.Exists(fullPath))
                        {
                            File.Delete(fullPath);
                            deletedCount++;
                            DebugWindow.Log($"[Uninstall][CacheSweep] Deleted residual file: {relativePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugWindow.Log($"[Uninstall][CacheSweep] Could not delete '{relativePath}': {ex.Message}");
                    }
                }

                // Delete directories that came from the cache (deepest first)
                var cachedDirs = cachedRelativePaths
                    .Select(p => Path.Combine(dir, p))
                    .Where(p => Directory.Exists(p))
                    .OrderByDescending(p => p.Length);

                foreach (var dirPath in cachedDirs)
                {
                    try
                    {
                        if (Directory.Exists(dirPath) && !Directory.EnumerateFileSystemEntries(dirPath).Any())
                        {
                            Directory.Delete(dirPath, false);
                            DebugWindow.Log($"[Uninstall][CacheSweep] Removed empty directory: {Path.GetRelativePath(dir, dirPath)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugWindow.Log($"[Uninstall][CacheSweep] Could not remove directory: {ex.Message}");
                    }
                }
            }

            DebugWindow.Log($"[Uninstall][CacheSweep] Cache comparison sweep completed. Residual files removed: {deletedCount}");
        }

        private sealed class RollbackResult
        {
            public int Restored { get; set; }
            public int Deleted { get; set; }
        }

        private static Dictionary<string, KeyFileSnapshot> BuildPreInstallStateMap(InstallationManifest? manifest)
        {
            var map = new Dictionary<string, KeyFileSnapshot>(StringComparer.OrdinalIgnoreCase);
            if (manifest?.PreInstallKeyFiles == null)
                return map;

            foreach (var snapshot in manifest.PreInstallKeyFiles)
            {
                if (string.IsNullOrWhiteSpace(snapshot.RelativePath))
                    continue;

                map[snapshot.RelativePath] = snapshot;
            }

            return map;
        }

        private static bool TryRestoreFromBackup(string gameDir, string backupDir, string relativePath, string? backupRelativePath)
        {
            try
            {
                var effectiveBackupRelative = string.IsNullOrWhiteSpace(backupRelativePath)
                    ? relativePath
                    : backupRelativePath;

                var backupPath = Path.Combine(backupDir, effectiveBackupRelative);
                if (!File.Exists(backupPath))
                    return false;

                var destinationPath = Path.Combine(gameDir, relativePath);
                var destinationDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                    Directory.CreateDirectory(destinationDir);

                File.Copy(backupPath, destinationPath, overwrite: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDeleteFileIfExists(string fullPath)
        {
            try
            {
                if (!File.Exists(fullPath))
                    return false;

                File.Delete(fullPath);
                return true;
            }
            catch
            {
                return false;
            }
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
            catch (Exception ex)
            {
                DebugWindow.Log($"[Install] Could not enumerate executables in '{game.InstallPath}': {ex.Message}");
            }

            string? bestMatchDir = null;

            if (allExes.Length > 0)
            {
                // Try to match by name or context
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
                        fileName.Contains("Launcher", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("UnrealCEFSubProcess", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("Prerequisites", StringComparison.OrdinalIgnoreCase))
                        continue;

                    int score = 0;
                    var exeLetters = new string(fileName.Where(char.IsLetterOrDigit).ToArray());

                    if (!string.IsNullOrEmpty(exeLetters) && !string.IsNullOrEmpty(gameNameLetters))
                    {
                        if (exeLetters.Contains(gameNameLetters, StringComparison.OrdinalIgnoreCase) ||
                            gameNameLetters.Contains(exeLetters, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 15;
                        }
                    }

                    if (exePath.Contains(@"Binaries\Win64", StringComparison.OrdinalIgnoreCase))
                    {
                        score += 5;
                    }

                    try
                    {
                        // Main game executables are usually decently sized (> 5MB)
                        var fileInfo = new FileInfo(exePath);
                        if (fileInfo.Length > 5 * 1024 * 1024)
                        {
                            score += 10;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugWindow.Log($"[Install] Could not read file info for '{exePath}': {ex.Message}");
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
                                if (dllName.Contains("amd") || dllName.Contains("fsr") || dllName.Contains("nvngx") || dllName.Contains("dlss") || dllName.Contains("sl.interposer") || dllName.Contains("xess"))
                                {
                                    score += 25; // High confidence if scaling DLLs are nearby
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugWindow.Log($"[Install] Could not enumerate DLLs in '{exeDir}': {ex.Message}");
                        }
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
            catch (Exception ex)
            {
                DebugWindow.Log($"[Install] Failed to modify OptiScaler.ini, creating new: {ex.Message}");
                File.WriteAllText(iniPath, $"[General]\n{key}={value}\n");
            }
        }
    }
}
