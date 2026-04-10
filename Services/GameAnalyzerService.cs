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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace OptiscalerClient.Services;

public class GameAnalyzerService
{
    private static readonly object _cacheLock = new();
    private static readonly Dictionary<string, AnalysisCacheEntry> _analysisCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] _dlssNames = new[] { "nvngx_dlss.dll" };
    private static readonly string[] _dlssFrameGenNames = new[] { "nvngx_dlssg.dll" };
    private static readonly string[] _fsrNames = new[] {
        "amd_fidelityfx_dx12.dll",
        "amd_fidelityfx_vk.dll",
        "amd_fidelityfx_upscaler_dx12.dll",
        "amd_fidelityfx_loader_dx12.dll",
        "ffx_fsr2_api_x64.dll",
        "ffx_fsr2_api_dx12_x64.dll",
        "ffx_fsr2_api_vk_x64.dll",
        "ffx_fsr3_api_x64.dll",
        "ffx_fsr3_api_dx12_x64.dll"
    };
    private static readonly string[] _xessNames = new[] { "libxess.dll" };

    private static readonly HashSet<string> _allTargetFileNames;
    private static readonly string _diskCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptiscalerClient", "analysis_cache.json");
    private static volatile bool _diskCacheLoaded = false;

    static GameAnalyzerService()
    {
        _allTargetFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "optiscaler_manifest.json",
            "optiscaler.log",
            "OptiScaler.ini"
        };
        foreach (var n in _dlssNames) _allTargetFileNames.Add(n);
        foreach (var n in _dlssFrameGenNames) _allTargetFileNames.Add(n);
        foreach (var n in _fsrNames) _allTargetFileNames.Add(n);
        foreach (var n in _xessNames) _allTargetFileNames.Add(n);
    }

    public static void InvalidateCacheForPath(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            return;

        string normalized;
        try
        {
            normalized = Path.GetFullPath(installPath);
        }
        catch
        {
            normalized = installPath;
        }

        lock (_cacheLock)
        {
            _analysisCache.Remove(normalized);
        }
    }

    public void AnalyzeGame(Game game, bool forceRefresh = false)
    {
        if (string.IsNullOrEmpty(game.InstallPath) || !Directory.Exists(game.InstallPath))
            return;

        string normalizedInstallPath;
        DateTime directoryWriteStamp;

        try
        {
            normalizedInstallPath = Path.GetFullPath(game.InstallPath);
            directoryWriteStamp = Directory.GetLastWriteTimeUtc(normalizedInstallPath);
        }
        catch
        {
            normalizedInstallPath = game.InstallPath;
            directoryWriteStamp = DateTime.MinValue;
        }

        if (!forceRefresh && TryApplyCachedAnalysis(game, normalizedInstallPath, directoryWriteStamp))
            return;

        // Reset current versions before analysis
        game.DlssVersion = null;
        game.DlssPath = null;
        game.FsrVersion = null;
        game.FsrPath = null;
        game.XessVersion = null;
        game.XessPath = null;
        game.IsOptiscalerInstalled = false;
        game.OptiscalerVersion = null; // Will be repopulated from manifest or log

        HashSet<string> ignoredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blockHeuristicFallbackDetection = false;

        try
        {
            // ── Single-pass file collection ──────────────────────────────────────────
            // Traverse the game directory once and classify all relevant files by name.
            var collectedFiles = CollectRelevantFiles(game.InstallPath);

            // ── Detect OptiScaler ──────────────────────────────────────────────────
            // Do this first so we can ignore its installed files when looking for native DLLs
            try
            {
                // ── Priority 1: manifest ────────────────────────────────────────────
                var manifestFiles = collectedFiles.TryGetValue("optiscaler_manifest.json", out var mf) ? mf.ToArray() : Array.Empty<string>();
                if (manifestFiles.Length > 0)
                {
                    try
                    {
                        var manifestJson = File.ReadAllText(manifestFiles[0]);
                        var manifest = System.Text.Json.JsonSerializer.Deserialize<Models.InstallationManifest>(manifestJson);
                        if (manifest != null)
                        {
                            // Only a committed manifest should be treated as a valid installation.
                            var isCommitted = string.Equals(
                                manifest.OperationStatus,
                                "committed",
                                StringComparison.OrdinalIgnoreCase);

                            if (!isCommitted)
                            {
                                blockHeuristicFallbackDetection = true;
                                try
                                {
                                    var installer = new GameInstallationService();
                                    installer.RecoverIncompleteInstallIfNeeded(game.InstallPath);
                                }
                                catch
                                {
                                    // Ignore recovery errors here; we'll just avoid false positives
                                    // from fallback detection for this analysis pass.
                                }
                            }

                            // Determine absolute game directory to validate expected markers and ignored files.
                            string originDir = string.IsNullOrEmpty(manifest.InstalledGameDirectory)
                                ? Path.GetDirectoryName(Path.GetDirectoryName(manifestFiles[0]))!
                                : manifest.InstalledGameDirectory;

                            var markersLookValid = true;
                            if (isCommitted && !string.IsNullOrEmpty(originDir))
                            {
                                var markers = manifest.ExpectedFinalMarkers ?? new List<string>();
                                if (markers.Count > 0)
                                {
                                    markersLookValid = markers.Any(rel =>
                                    {
                                        try
                                        {
                                            return File.Exists(Path.Combine(originDir, rel));
                                        }
                                        catch
                                        {
                                            return false;
                                        }
                                    });
                                }
                            }

                            if (isCommitted && markersLookValid)
                            {
                                game.IsOptiscalerInstalled = true;
                                if (!string.IsNullOrEmpty(manifest.OptiscalerVersion))
                                    game.OptiscalerVersion = manifest.OptiscalerVersion;

                                if (!string.IsNullOrEmpty(originDir))
                                {
                                    foreach (var relFile in manifest.InstalledFiles)
                                    {
                                        ignoredFiles.Add(Path.GetFullPath(Path.Combine(originDir, relFile)));
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugWindow.Log($"[Analyzer] Corrupt manifest in '{game.InstallPath}': {ex.Message}");
                    }
                }

                // ── Priority 2: runtime log (overrides if it has richer version info) ──
                if (!blockHeuristicFallbackDetection &&
                    (!game.IsOptiscalerInstalled || string.IsNullOrEmpty(game.OptiscalerVersion)))
                {
                    try
                    {
                        var logs = collectedFiles.TryGetValue("optiscaler.log", out var lf) ? lf.ToArray() : Array.Empty<string>();
                        if (logs.Length > 0)
                        {
                            // Example log line: "[2024-...] [Init] OptiScaler v0.7.0-rc1"
                            foreach (var line in File.ReadLines(logs[0]).Take(10))
                            {
                                if (line.Contains("OptiScaler v", StringComparison.OrdinalIgnoreCase))
                                {
                                    var idx = line.IndexOf("OptiScaler v", StringComparison.OrdinalIgnoreCase);
                                    if (idx != -1)
                                    {
                                        var verPart = line.Substring(idx + 12).Trim();
                                        var endIdx = verPart.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
                                        if (endIdx != -1) verPart = verPart.Substring(0, endIdx);
                                        if (!string.IsNullOrEmpty(verPart))
                                        {
                                            game.IsOptiscalerInstalled = true;
                                            game.OptiscalerVersion = verPart;
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugWindow.Log($"[Analyzer] Error reading OptiScaler log for '{game.Name}': {ex.Message}");
                    }
                }

                // ── Priority 3: OptiScaler.ini presence (no version — last resort) ──
                if (!blockHeuristicFallbackDetection && !game.IsOptiscalerInstalled)
                {
                    var iniFiles = collectedFiles.TryGetValue("OptiScaler.ini", out var inf) ? inf.ToArray() : Array.Empty<string>();
                    if (iniFiles.Length > 0)
                        game.IsOptiscalerInstalled = true;
                }
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[Analyzer] OptiScaler detection error for '{game.Name}': {ex.Message}");
            }

            // DLSS
            FindBestVersionFromCollected(game, collectedFiles, _dlssNames, ignoredFiles, (g, path, ver) =>
            {
                g.DlssPath = path;
                g.DlssVersion = ver;
            });

            // DLSS Frame Gen
            FindBestVersionFromCollected(game, collectedFiles, _dlssFrameGenNames, ignoredFiles, (g, path, ver) => { g.DlssFrameGenPath = path; g.DlssFrameGenVersion = ver; });

            // FSR
            FindBestVersionFromCollected(game, collectedFiles, _fsrNames, ignoredFiles, (g, path, ver) => { g.FsrPath = path; g.FsrVersion = ver; });

            // XeSS
            FindBestVersionFromCollected(game, collectedFiles, _xessNames, ignoredFiles, (g, path, ver) => { g.XessPath = path; g.XessVersion = ver; });

        }
        catch (Exception ex)
        {
            DebugWindow.Log($"[Analyzer] General analysis error for '{game.Name}': {ex.Message}");
        }

        SaveAnalysisCache(game, normalizedInstallPath, directoryWriteStamp);
    }

    private static bool TryApplyCachedAnalysis(Game game, string installPath, DateTime directoryWriteStamp)
    {
        lock (_cacheLock)
        {
            if (!_analysisCache.TryGetValue(installPath, out var cached))
                return false;

            if (cached.DirectoryWriteStampUtc != directoryWriteStamp)
                return false;

            cached.ApplyTo(game);
            return true;
        }
    }

    private static void SaveAnalysisCache(Game game, string installPath, DateTime directoryWriteStamp)
    {
        var snapshot = AnalysisCacheEntry.FromGame(game, directoryWriteStamp);
        lock (_cacheLock)
        {
            _analysisCache[installPath] = snapshot;
        }
    }

    private static void FindBestVersionFromCollected(Game game, Dictionary<string, List<string>> collectedFiles, string[] filePatterns, HashSet<string> ignoredFiles, Action<Game, string, string> updateAction)
    {
        var highestVer = new Version(0, 0);
        string? bestPath = null;
        string? bestVerStr = null;

        foreach (var pattern in filePatterns)
        {
            if (!collectedFiles.TryGetValue(pattern, out var files)) continue;
            foreach (var file in files)
            {
                if (ignoredFiles.Contains(Path.GetFullPath(file))) continue;

                var versionStr = GetFileVersion(file);

                // Clean up version string if it contains "FSR ", e.g. "FSR 3.1.4"
                string parseableVerStr = versionStr;
                if (parseableVerStr.StartsWith("FSR ", StringComparison.OrdinalIgnoreCase))
                    parseableVerStr = parseableVerStr.Substring(4).Trim();

                // Also take only the first component if there are spaces, e.g. "3.1.0 (release)"
                parseableVerStr = parseableVerStr.Split(' ')[0];

                if (Version.TryParse(parseableVerStr, out var currentVer) && currentVer > highestVer)
                {
                    highestVer = currentVer;
                    bestPath = file;
                    bestVerStr = versionStr; // keep original string for display
                }
            }
        }

        if (bestPath != null && bestVerStr != null)
            updateAction(game, bestPath, bestVerStr);
    }

    private static Dictionary<string, List<string>> CollectRelevantFiles(string path)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", options))
            {
                var name = Path.GetFileName(file);
                if (_allTargetFileNames.Contains(name))
                {
                    if (!result.TryGetValue(name, out var list))
                    {
                        list = new List<string>();
                        result[name] = list;
                    }
                    list.Add(file);
                }
            }
        }
        catch (Exception ex)
        {
            DebugWindow.Log($"[Analyzer] Error enumerating files in '{path}': {ex.Message}");
        }

        return result;
    }

    public static void LoadCacheFromDisk()
    {
        lock (_cacheLock)
        {
            if (_diskCacheLoaded) return;
            _diskCacheLoaded = true;

            try
            {
                if (!File.Exists(_diskCachePath)) return;
                var json = File.ReadAllText(_diskCachePath);
                var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, AnalysisCacheEntry>>(json);
                if (loaded == null) return;

                foreach (var kv in loaded)
                    _analysisCache[kv.Key] = kv.Value;

                DebugWindow.Log($"[Analyzer] Loaded {loaded.Count} cached entries from disk.");
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[Analyzer] Failed to load disk cache: {ex.Message}");
            }
        }
    }

    public static void FlushCacheToDisk()
    {
        try
        {
            Dictionary<string, AnalysisCacheEntry> snapshot;
            lock (_cacheLock)
            {
                snapshot = new Dictionary<string, AnalysisCacheEntry>(_analysisCache, StringComparer.OrdinalIgnoreCase);
            }
            var dir = Path.GetDirectoryName(_diskCachePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
            File.WriteAllText(_diskCachePath, json);
            DebugWindow.Log($"[Analyzer] Flushed {snapshot.Count} cache entries to disk.");
        }
        catch (Exception ex)
        {
            DebugWindow.Log($"[Analyzer] Failed to flush cache to disk: {ex.Message}");
        }
    }

    private static string GetFileVersion(string filePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(filePath);

            // ProductVersion is usually more accurate for libraries like DLSS (e.g. "3.7.10.0")
            // FileVersion might be "1.0.0.0" wrapper.
            if (!string.IsNullOrEmpty(info.ProductVersion) && info.ProductVersion != "1.0.0.0" && !info.ProductVersion.StartsWith("1.0."))
            {
                return info.ProductVersion.Replace(',', '.').Split(' ')[0];
            }

            if (!string.IsNullOrEmpty(info.FileVersion))
            {
                return info.FileVersion.Replace(',', '.').Split(' ')[0];
            }

            return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
        }
        catch
        {
            return "0.0.0.0";
        }
    }

    private sealed class AnalysisCacheEntry
    {
        public DateTime DirectoryWriteStampUtc { get; set; }
        public string? DlssVersion { get; set; }
        public string? DlssPath { get; set; }
        public string? DlssFrameGenVersion { get; set; }
        public string? DlssFrameGenPath { get; set; }
        public string? FsrVersion { get; set; }
        public string? FsrPath { get; set; }
        public string? XessVersion { get; set; }
        public string? XessPath { get; set; }
        public bool IsOptiscalerInstalled { get; set; }
        public string? OptiscalerVersion { get; set; }

        public static AnalysisCacheEntry FromGame(Game game, DateTime directoryWriteStampUtc)
        {
            return new AnalysisCacheEntry
            {
                DirectoryWriteStampUtc = directoryWriteStampUtc,
                DlssVersion = game.DlssVersion,
                DlssPath = game.DlssPath,
                DlssFrameGenVersion = game.DlssFrameGenVersion,
                DlssFrameGenPath = game.DlssFrameGenPath,
                FsrVersion = game.FsrVersion,
                FsrPath = game.FsrPath,
                XessVersion = game.XessVersion,
                XessPath = game.XessPath,
                IsOptiscalerInstalled = game.IsOptiscalerInstalled,
                OptiscalerVersion = game.OptiscalerVersion
            };
        }

        public void ApplyTo(Game game)
        {
            game.DlssVersion = DlssVersion;
            game.DlssPath = DlssPath;
            game.DlssFrameGenVersion = DlssFrameGenVersion;
            game.DlssFrameGenPath = DlssFrameGenPath;
            game.FsrVersion = FsrVersion;
            game.FsrPath = FsrPath;
            game.XessVersion = XessVersion;
            game.XessPath = XessPath;
            game.IsOptiscalerInstalled = IsOptiscalerInstalled;
            game.OptiscalerVersion = OptiscalerVersion;
        }
    }
}
