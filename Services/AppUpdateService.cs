using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using OptiscalerClient.Models;
using OptiscalerClient.Views;

namespace OptiscalerClient.Services
{
    public class AppUpdateService
    {
        private readonly HttpClient _httpClient;
        private readonly ComponentManagementService _componentService;

        public string? LatestVersion { get; private set; }
        public string? ReleaseNotes { get; private set; }
        public string? DownloadUrl { get; private set; }
        public bool IsError { get; private set; }

        public AppUpdateService(ComponentManagementService componentService)
        {
            _componentService = componentService;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "OptiscalerClientUpdater");
        }

        public async Task<bool> CheckForAppUpdateAsync()
        {
            IsError = false;
            try
            {
                var repo = _componentService.Config.App;
                if (string.IsNullOrWhiteSpace(repo.RepoOwner) || string.IsNullOrWhiteSpace(repo.RepoName))
                    return false;

                var url = $"https://api.github.com/repos/{repo.RepoOwner}/{repo.RepoName}/releases/latest";
                DebugWindow.Log($"[AppUpdate] Fetching latest App version from: {url}");
                
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    DebugWindow.Log($"[AppUpdate] API Error: {response.StatusCode} ({(int)response.StatusCode})");
                    IsError = true;
                    return false;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("tag_name", out var tagProp))
                {
                    var versionTag = tagProp.GetString() ?? string.Empty;
                    LatestVersion = versionTag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                        ? versionTag.Substring(1)
                        : versionTag;

                    if (doc.RootElement.TryGetProperty("body", out var bodyProp))
                        ReleaseNotes = bodyProp.GetString();

                    if (doc.RootElement.TryGetProperty("assets", out var assets))
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            if (asset.TryGetProperty("browser_download_url", out var downloadProp))
                            {
                                var assetUrl = downloadProp.GetString();
                                if (assetUrl != null && assetUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (assetUrl.Contains("OptiscalerClient_Portable.zip", StringComparison.OrdinalIgnoreCase))
                                    {
                                        DownloadUrl = assetUrl;
                                        break;
                                    }
                                    else if (DownloadUrl == null)
                                    {
                                        DownloadUrl = assetUrl; // Fallback just in case
                                    }
                                }
                            }
                        }
                    }

                    // More robust way to get current version
                    string currentVersionStr = typeof(AppUpdateService).Assembly
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                        .InformationalVersion ?? "0.0.0.0";
                    
                    // Cleanup version string (remove common git suffixes like +...)
                    if (currentVersionStr.Contains("+")) currentVersionStr = currentVersionStr.Split('+')[0];
                    if (currentVersionStr.StartsWith("v", StringComparison.OrdinalIgnoreCase)) currentVersionStr = currentVersionStr.Substring(1);

                    if (string.IsNullOrEmpty(LatestVersion)) return false;

                    // Normalize LatestVersion too (remove prefixes like 'OptiscalerClient-' or 'v')
                    if (LatestVersion.StartsWith("OptiscalerClient-", StringComparison.OrdinalIgnoreCase)) 
                        LatestVersion = LatestVersion.Substring("OptiscalerClient-".Length);
                    if (LatestVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)) 
                        LatestVersion = LatestVersion.Substring(1);

                    // Support for comparison logs
                    string logMsg = $"[AppUpdate] Normalized: Current='{currentVersionStr}', Latest='{LatestVersion}'";
                    Console.WriteLine(logMsg);
                    Console.Out.Flush();
                    System.IO.File.AppendAllText("update_debug.log", logMsg + Environment.NewLine);
                    DebugWindow.Log(logMsg);

                    if (Version.TryParse(currentVersionStr, out var currentVer) && Version.TryParse(LatestVersion, out var newVer))
                    {
                        string parseMsg = $"[AppUpdate] Parsed versions: Current='{currentVer}', New='{newVer}'";
                        Console.WriteLine(parseMsg);
                        Console.Out.Flush();
                        System.IO.File.AppendAllText("update_debug.log", parseMsg + Environment.NewLine);
                        DebugWindow.Log(parseMsg);

                        if (newVer > currentVer)
                        {
                            string updateMsg = $"[AppUpdate] Detected UPDATE: {newVer} > {currentVer}";
                            Console.WriteLine(updateMsg);
                            Console.Out.Flush();
                            System.IO.File.AppendAllText("update_debug.log", updateMsg + Environment.NewLine);
                            DebugWindow.Log(updateMsg);
                            return true;
                        }
                    }
                    else
                    {
                        string fallbackMsg = $"[AppUpdate] Fallback (non-SEMVER) comparison: '{LatestVersion}' != '{currentVersionStr}'";
                        Console.WriteLine(fallbackMsg);
                        Console.Out.Flush();
                        System.IO.File.AppendAllText("update_debug.log", fallbackMsg + Environment.NewLine);
                        DebugWindow.Log(fallbackMsg);
                        if (LatestVersion != currentVersionStr)
                            return true;
                    }
                }
            }
            catch (Exception ex)
            { 
                string errorMsg = $"[AppUpdate] FATAL ERROR: {ex.Message}";
                Console.WriteLine(errorMsg);
                Console.Out.Flush();
                System.IO.File.AppendAllText("update_debug.log", errorMsg + Environment.NewLine);
                DebugWindow.Log(errorMsg);
            }
            return false;
        }

        public async Task DownloadAndPrepareUpdateAsync(IProgress<double>? progress = null)
        {
            if (string.IsNullOrEmpty(DownloadUrl))
                throw new Exception("No valid download URL found for the update.");

            var tempZip = Path.Combine(Path.GetTempPath(), $"OptiscalerClientUpdate_{Guid.NewGuid()}.zip");
            var updateFolder = Path.Combine(AppContext.BaseDirectory, "update_temp");

            try
            {
                using var dlResponse = await _httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                dlResponse.EnsureSuccessStatusCode();

                var totalBytes = dlResponse.Content.Headers.ContentLength ?? 10 * 1024 * 1024;

                using (var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var cs = await dlResponse.Content.ReadAsStreamAsync())
                {
                    var buffer = new byte[8192];
                    var isMoreToRead = true;
                    long totalRead = 0;

                    do
                    {
                        var read = await cs.ReadAsync(buffer, 0, buffer.Length);
                        if (read == 0)
                            isMoreToRead = false;
                        else
                        {
                            await fs.WriteAsync(buffer, 0, read);
                            totalRead += read;
                            progress?.Report((double)totalRead / totalBytes * 100);
                        }
                    }
                    while (isMoreToRead);
                }

                if (Directory.Exists(updateFolder))
                    Directory.Delete(updateFolder, true);
                Directory.CreateDirectory(updateFolder);

                ZipFile.ExtractToDirectory(tempZip, updateFolder, overwriteFiles: true);

                // Check if zip contains a single folder inside it, then we move contents up
                var extractedDirs = Directory.GetDirectories(updateFolder);
                var extractedFiles = Directory.GetFiles(updateFolder);

                if (extractedDirs.Length == 1 && extractedFiles.Length == 0)
                {
                    var innerDir = extractedDirs[0];
                    foreach (var file in Directory.GetFiles(innerDir, "*.*", SearchOption.AllDirectories))
                    {
                        var destPath = file.Replace(innerDir, updateFolder);
                        var destDir = Path.GetDirectoryName(destPath);
                        if (destDir != null) Directory.CreateDirectory(destDir);
                        File.Move(file, destPath, overwrite: true);
                    }
                    Directory.Delete(innerDir, true);
                }

                // Create the update script (platform-specific)
                if (OperatingSystem.IsWindows())
                {
                    var basePath = AppContext.BaseDirectory.TrimEnd('\\');
                    var batPath = Path.Combine(basePath, "update.bat");
                    var batContent = $@"@echo off
echo Updating Optiscaler Client...
timeout /t 2 /nobreak > nul
cd /d ""{basePath}""
xcopy /Y /S ""{updateFolder}\*"" "".\""
rmdir /s /q ""{updateFolder}""
start """" ""OptiscalerClient.exe""
del ""%~f0""
";
                    File.WriteAllText(batPath, batContent);
                }
                else
                {
                    var basePath = AppContext.BaseDirectory.TrimEnd('/');
                    var shPath = Path.Combine(basePath, "update.sh");
                    var shContent = $@"#!/bin/sh
echo 'Updating Optiscaler Client...'
sleep 2
cp -rf ""{updateFolder}/""* ""{basePath}/""
rm -rf ""{updateFolder}""
""{basePath}/OptiscalerClient"" &
rm -- ""$0""
";
                    File.WriteAllText(shPath, shContent);
                    File.SetUnixFileMode(shPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
            }
            finally
            {
                if (File.Exists(tempZip))
                    File.Delete(tempZip);
            }
        }

        public void FinalizeAndRestart()
        {
            string scriptPath;
            ProcessStartInfo psi;

            if (OperatingSystem.IsWindows())
            {
                scriptPath = Path.Combine(AppContext.BaseDirectory, "update.bat");
                if (!File.Exists(scriptPath)) return;
                psi = new ProcessStartInfo
                {
                    FileName = scriptPath,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
            }
            else
            {
                scriptPath = Path.Combine(AppContext.BaseDirectory, "update.sh");
                if (!File.Exists(scriptPath)) return;
                psi = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = scriptPath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }

            Process.Start(psi);
            // Avalonia UI TODO: System.Windows.Application.Current.Shutdown();
        }
    }
}
