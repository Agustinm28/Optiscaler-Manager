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
using System.Diagnostics;
using System.IO;

namespace OptiscalerClient.Services;

public class GameAnalyzerService
{
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

    public void AnalyzeGame(Game game)
    {
        if (string.IsNullOrEmpty(game.InstallPath) || !Directory.Exists(game.InstallPath))
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

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            };

            // ── Detect OptiScaler ──────────────────────────────────────────────────
            // Do this first so we can ignore its installed files when looking for native DLLs
            try
            {
                // ── Priority 1: manifest ────────────────────────────────────────────
                var manifestFiles = Directory.GetFiles(game.InstallPath, "optiscaler_manifest.json", options);
                if (manifestFiles.Length > 0)
                {
                    try
                    {
                        var manifestJson = File.ReadAllText(manifestFiles[0]);
                        var manifest = System.Text.Json.JsonSerializer.Deserialize<Models.InstallationManifest>(manifestJson);
                        if (manifest != null)
                        {
                            game.IsOptiscalerInstalled = true;
                            if (!string.IsNullOrEmpty(manifest.OptiscalerVersion))
                                game.OptiscalerVersion = manifest.OptiscalerVersion;

                            // Determine absolute game directory to construct absolute paths
                            string originDir = string.IsNullOrEmpty(manifest.InstalledGameDirectory)
                                ? Path.GetDirectoryName(Path.GetDirectoryName(manifestFiles[0]))!
                                : manifest.InstalledGameDirectory;

                            if (!string.IsNullOrEmpty(originDir))
                            {
                                foreach (var relFile in manifest.InstalledFiles)
                                {
                                    ignoredFiles.Add(Path.GetFullPath(Path.Combine(originDir, relFile)));
                                }
                            }
                        }
                    }
                    catch { /* Corrupt manifest — fall through to next priority */ }
                }

                // ── Priority 2: runtime log (overrides if it has richer version info) ──
                if (!game.IsOptiscalerInstalled || string.IsNullOrEmpty(game.OptiscalerVersion))
                {
                    try
                    {
                        var logs = Directory.GetFiles(game.InstallPath, "optiscaler.log", options);
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
                    catch { }
                }

                // ── Priority 3: OptiScaler.ini presence (no version — last resort) ──
                if (!game.IsOptiscalerInstalled)
                {
                    var iniFiles = Directory.GetFiles(game.InstallPath, "OptiScaler.ini", options);
                    if (iniFiles.Length > 0)
                        game.IsOptiscalerInstalled = true;
                }
            }
            catch { /* Ignore OptiScaler detection errors */ }

            // Efficiently search ONLY for the specific files we care about
            // This avoids listing thousands of DLLs

            // DLSS
            FindBestVersion(game, game.InstallPath, _dlssNames, options, ignoredFiles, (g, path, ver) =>
            {
                g.DlssPath = path;
                g.DlssVersion = ver;
            });

            // DLSS Frame Gen
            FindBestVersion(game, game.InstallPath, _dlssFrameGenNames, options, ignoredFiles, (g, path, ver) => { g.DlssFrameGenPath = path; g.DlssFrameGenVersion = ver; });

            // FSR
            FindBestVersion(game, game.InstallPath, _fsrNames, options, ignoredFiles, (g, path, ver) => { g.FsrPath = path; g.FsrVersion = ver; });

            // XeSS
            FindBestVersion(game, game.InstallPath, _xessNames, options, ignoredFiles, (g, path, ver) => { g.XessPath = path; g.XessVersion = ver; });

        }
        catch { /* General error */ }
    }

    private void FindBestVersion(Game game, string path, string[] filePatterns, EnumerationOptions options, HashSet<string> ignoredFiles, Action<Game, string, string> updateAction)
    {
        var highestVer = new Version(0, 0);
        string? bestPath = null;
        string? bestVerStr = null;

        foreach (var pattern in filePatterns)
        {
            try
            {
                var files = Directory.GetFiles(path, pattern, options);
                foreach (var file in files)
                {
                    if (ignoredFiles.Contains(Path.GetFullPath(file))) continue;

                    var versionStr = GetFileVersion(file);

                    // Clean up version string if it contains "FSR ", e.g. "FSR 3.1.4"
                    string parseableVerStr = versionStr;
                    if (parseableVerStr.StartsWith("FSR ", StringComparison.OrdinalIgnoreCase))
                    {
                        parseableVerStr = parseableVerStr.Substring(4).Trim();
                    }

                    // Also take only the first component if there are spaces, e.g. "3.1.0 (release)"
                    parseableVerStr = parseableVerStr.Split(' ')[0];

                    if (Version.TryParse(parseableVerStr, out var currentVer))
                    {
                        if (currentVer > highestVer)
                        {
                            highestVer = currentVer;
                            bestPath = file;
                            bestVerStr = versionStr; // keep original string for display
                        }
                    }
                }
            }
            catch { /* Ignore individual search errors */ }
        }

        if (bestPath != null && bestVerStr != null)
        {
            updateAction(game, bestPath, bestVerStr);
        }
    }

    private string GetFileVersion(string filePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(filePath);

            // ProductVersion is usually more accurate for libraries like DLSS (e.g. "3.7.10.0")
            // FileVersion might be "1.0.0.0" wrapper.
            string? version = null;
            if (!string.IsNullOrEmpty(info.ProductVersion) && info.ProductVersion != "1.0.0.0" && !info.ProductVersion.StartsWith("1.0."))
                version = info.ProductVersion.Replace(',', '.').Split(' ')[0];
            else if (!string.IsNullOrEmpty(info.FileVersion))
                version = info.FileVersion.Replace(',', '.').Split(' ')[0];
            else
                version = $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";

            // On Linux, FileVersionInfo cannot read Windows PE version resources — fall back to manual PE parsing
            if (version == "0.0.0.0" && !OperatingSystem.IsWindows())
                version = ReadPeFileVersion(filePath);

            return version;
        }
        catch
        {
            return OperatingSystem.IsWindows() ? "0.0.0.0" : ReadPeFileVersion(filePath);
        }
    }

    /// <summary>
    /// Reads the file version from a Windows PE binary by parsing the resource section directly.
    /// Used on Linux where FileVersionInfo cannot parse PE version resources.
    /// </summary>
    private string ReadPeFileVersion(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);

            // DOS header: MZ signature + e_lfanew at offset 0x3C
            if (reader.ReadUInt16() != 0x5A4D) return "0.0.0.0";
            fs.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = reader.ReadUInt32();

            // PE signature
            fs.Seek(peOffset, SeekOrigin.Begin);
            if (reader.ReadUInt32() != 0x00004550) return "0.0.0.0";

            // COFF header
            reader.ReadUInt16(); // Machine
            var numSections = reader.ReadUInt16();
            reader.ReadBytes(12); // TimeDateStamp, PointerToSymbolTable, NumberOfSymbols
            var optHeaderSize = reader.ReadUInt16();
            reader.ReadUInt16(); // Characteristics

            // Optional header
            var optHeaderStart = fs.Position;
            var magic = reader.ReadUInt16();
            bool is64 = magic == 0x20B; // PE32+ vs PE32

            // DataDirectory[2] = Resource Table
            // PE32:  DataDirectory starts at offset 96 → resource at 96 + 2*8 = 112
            // PE32+: DataDirectory starts at offset 112 → resource at 112 + 2*8 = 128
            var resourceDirOffset = (is64 ? 112 : 96) + 16;
            fs.Seek(optHeaderStart + resourceDirOffset, SeekOrigin.Begin);
            var rsrcRVA = reader.ReadUInt32();
            var rsrcSize = reader.ReadUInt32();

            if (rsrcRVA == 0 || rsrcSize == 0) return "0.0.0.0";

            // Section headers: find the section containing the resource RVA
            fs.Seek(optHeaderStart + optHeaderSize, SeekOrigin.Begin);
            uint rsrcFileOffset = 0;
            for (int i = 0; i < numSections; i++)
            {
                reader.ReadBytes(8); // Name
                reader.ReadUInt32(); // VirtualSize
                var va = reader.ReadUInt32(); // VirtualAddress
                var rawSize = reader.ReadUInt32();
                var rawOffset = reader.ReadUInt32();
                reader.ReadBytes(16); // Rest of section header

                if (va <= rsrcRVA && rsrcRVA < va + rawSize)
                {
                    rsrcFileOffset = rawOffset + (rsrcRVA - va);
                    break;
                }
            }

            if (rsrcFileOffset == 0) return "0.0.0.0";

            // Read resource section (cap at 4 MB — version resources are tiny)
            fs.Seek(rsrcFileOffset, SeekOrigin.Begin);
            var rsrcData = reader.ReadBytes((int)Math.Min(rsrcSize, 4 * 1024 * 1024));

            // Search for VS_FIXEDFILEINFO magic: FEEF04BD
            for (int i = 0; i <= rsrcData.Length - 20; i++)
            {
                if (rsrcData[i] != 0xBD || rsrcData[i + 1] != 0x04 ||
                    rsrcData[i + 2] != 0xEF || rsrcData[i + 3] != 0xFE) continue;

                // Offset +4: dwStrucVersion must be 0x00010000 (version 1.0)
                var structVer = BitConverter.ToUInt32(rsrcData, i + 4);
                if (structVer != 0x00010000) continue;

                var ms = BitConverter.ToUInt32(rsrcData, i + 8);  // dwFileVersionMS
                var ls = BitConverter.ToUInt32(rsrcData, i + 12); // dwFileVersionLS

                if (ms == 0 && ls == 0) continue;

                return $"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}";
            }
        }
        catch { }

        return "0.0.0.0";
    }
}
