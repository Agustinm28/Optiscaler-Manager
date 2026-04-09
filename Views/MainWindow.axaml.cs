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
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using OptiscalerClient.Models;
using OptiscalerClient.Services;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using System.Collections.ObjectModel;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using System.IO;
using System.Collections.Generic;

namespace OptiscalerClient.Views
{
    public partial class MainWindow : Window
    {
        private readonly GameScannerService _scannerService;
        private readonly GamePersistenceService _persistenceService;
        private ObservableCollection<Game> _games;
        private List<Game> _allGames = new List<Game>();
        private readonly ComponentManagementService _componentService;
        private readonly IGpuDetectionService _gpuService;

        private GpuInfo? _lastDetectedGpu;
        private ScrollViewer? _gameListScrollViewer;
        private bool _isInitializingLanguage = true;
        private DispatcherTimer? _scanDotTimer;
        private double _scanDotPhase = 0;

        private readonly GameAnalyzerService _analyzerService = new();
        private GameMetadataService _metadataService = null!;

        private ListBox? _lstGames;
        private TextBlock? _txtStatus;
        private Button? _btnScan;
        private Grid? _overlayScanning;
        private TextBox? _txtSearch;
        private TextBlock? _txtSearchPlaceholder;
        private TextBlock? _txtGpuInfo;

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public MainWindow()
        {
            InitializeComponent();
            _scannerService = new GameScannerService();
            _persistenceService = new GamePersistenceService();
            _componentService = new ComponentManagementService();
            _metadataService = new GameMetadataService(_componentService);
            App.ChangeLanguage(_componentService.Config.Language);
            if (OperatingSystem.IsWindows())
                _gpuService = new WindowsGpuDetectionService();
            else if (OperatingSystem.IsLinux())
                _gpuService = new LinuxGpuDetectionService();
            else
                _gpuService = null!;
            _games = new ObservableCollection<Game>();

            // Debug Window check
            if (_componentService.Config.Debug)
            {
                var debugWindow = new DebugWindow(true);
                debugWindow.Show();
                DebugWindow.Log("Application Started in DEBUG mode.");
            }
            
            _componentService.OnStatusChanged += ComponentStatusChanged;
            this.Loaded += MainWindow_Loaded;
            
            // Restore window state
            RestoreWindowState();
            
            // Handle window state changes
            this.PropertyChanged += Window_PropertyChanged;
        }

        private void ComponentStatusChanged()
        {
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            _gameListScrollViewer = this.FindControl<ScrollViewer>("GameListScrollViewer");
            _lstGames = this.FindControl<ListBox>("LstGames");
            _txtStatus = this.FindControl<TextBlock>("TxtStatus");
            _btnScan = this.FindControl<Button>("BtnScan");
            _overlayScanning = this.FindControl<Grid>("OverlayScanning");
            _txtSearch = this.FindControl<TextBox>("TxtSearch");
            _txtSearchPlaceholder = this.FindControl<TextBlock>("TxtSearchPlaceholder");
            _txtGpuInfo = this.FindControl<TextBlock>("TxtGpuInfo");

            if (_lstGames != null) _lstGames.ItemsSource = _games;

            bool hadSavedGames = LoadSavedGames();
            _ = LoadGpuInfoAsync();
            _ = CheckUpdatesOnStartupAsync();

            var linuxNotice = this.FindControl<Border>("LinuxNotice");
            if (linuxNotice != null)
                linuxNotice.IsVisible = OperatingSystem.IsLinux();
            
            UpdateAnimationsState(_componentService.Config.AnimationsEnabled);

            if (!hadSavedGames && _componentService.Config.AutoScan)
            {
                BtnScan_Click(null!, null!);
            }
        }

        private void UpdateSearchPlaceholderVisibility()
        {
            if (_txtSearchPlaceholder == null || _txtSearch == null) return;

            if (_txtSearch.IsFocused)
            {
                _txtSearchPlaceholder.IsVisible = false;
            }
            else
            {
                _txtSearchPlaceholder.IsVisible = string.IsNullOrEmpty(_txtSearch.Text);
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSearchPlaceholderVisibility();
            if (sender is TextBox textBox)
            {
                ApplyFilter(textBox.Text);
            }
        }

        private void ApplyFilter(string? searchText)
        {
            if (_allGames == null) return;

            var filtered = string.IsNullOrWhiteSpace(searchText) 
                ? _allGames 
                : _allGames.Where(g => g.Name != null && g.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();

            _games.Clear();
            foreach (var game in filtered)
            {
                _games.Add(game);
            }
        }

        private void TxtSearch_GotFocus(object sender, GotFocusEventArgs e)
        {
            UpdateSearchPlaceholderVisibility();
        }

        private void TxtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdateSearchPlaceholderVisibility();
        }

        private void GameListScrollViewer_PointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            if (_gameListScrollViewer != null && e.Delta.Y != 0)
            {
                e.Handled = true;
                // Move manually with boost
                var currentOffset = _gameListScrollViewer.Offset;
                var newY = currentOffset.Y - (e.Delta.Y * 120); // 120 for fast and fluid
                _gameListScrollViewer.Offset = new Vector(currentOffset.X, Math.Max(0, newY));
            }
        }

        private async void BtnGuide_Click2(object sender, RoutedEventArgs e)
        {
            var guide = new GuideWindow(this);
            await guide.ShowDialog(this);
        }

        private static readonly string[] _viewNames = { "ViewGames", "ViewSettings", "ViewHelp" };

        private void SwitchToView(string viewName)
        {
            foreach (var name in _viewNames)
            {
                var grid = this.FindControl<Grid>(name);
                if (grid == null) continue;
                bool isActive = name == viewName;
                grid.Opacity = isActive ? 1.0 : 0.0;
                grid.IsHitTestVisible = isActive;
            }
        }

        private void NavGames_Click(object sender, RoutedEventArgs e)
        {
            SwitchToView("ViewGames");
        }

        private void NavHelp_Click(object sender, RoutedEventArgs e)
        {
            SwitchToView("ViewHelp");
            PopulateHelpContent();
        }

        private void NavSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToView("ViewSettings");

            _isInitializingLanguage = true;
            var cmbLanguage = this.FindControl<ComboBox>("CmbLanguage");
            if (cmbLanguage != null)
            {
                foreach (var baseItem in cmbLanguage.Items)
                {
                    if (baseItem is ComboBoxItem item && item.Tag?.ToString() == App.CurrentLanguage)
                    {
                        cmbLanguage.SelectedItem = item;
                        break;
                    }
                }
            }
            var tglAutoScan = this.FindControl<ToggleSwitch>("TglAutoScan");
            if (tglAutoScan != null)
            {
                tglAutoScan.IsChecked = _componentService.Config.AutoScan;
            }
            var tglAnimations = this.FindControl<ToggleSwitch>("TglAnimations");
            if (tglAnimations != null)
            {
                tglAnimations.IsChecked = _componentService.Config.AnimationsEnabled;
            }
            var tglBetaVersions = this.FindControl<ToggleSwitch>("TglBetaVersions");
            if (tglBetaVersions != null)
            {
                tglBetaVersions.IsChecked = _componentService.Config.ShowBetaVersions;
            }

            // Populate FSR4 INT8 default version selector
            var cmbDefaultExtras = this.FindControl<ComboBox>("CmbDefaultExtrasVersion");
            if (cmbDefaultExtras != null)
            {
                _isInitializingLanguage = true; // reuse flag to suppress SelectionChanged during init
                cmbDefaultExtras.Items.Clear();
                cmbDefaultExtras.Items.Add(new ComboBoxItem { Content = "None", Tag = "none" });
                foreach (var ver in _componentService.ExtrasAvailableVersions)
                {
                    cmbDefaultExtras.Items.Add(new ComboBoxItem { Content = ver, Tag = ver });
                }

                var savedDefault = _componentService.Config.DefaultExtrasVersion;
                cmbDefaultExtras.SelectedIndex = 0; // default: None
                if (!string.IsNullOrEmpty(savedDefault) &&
                    !savedDefault.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 1; i < cmbDefaultExtras.Items.Count; i++)
                    {
                        if ((cmbDefaultExtras.Items[i] as ComboBoxItem)?.Tag?.ToString() == savedDefault)
                        {
                            cmbDefaultExtras.SelectedIndex = i;
                            break;
                        }
                    }
                }
                _isInitializingLanguage = false;
            }
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingLanguage) return;
            var cmbLanguage = sender as ComboBox;
            if (cmbLanguage?.SelectedItem is ComboBoxItem selectedItem)
            {
                string? langCode = selectedItem.Tag?.ToString();
                if (!string.IsNullOrEmpty(langCode))
                {
                    App.ChangeLanguage(langCode);
                    _componentService.Config.Language = langCode;
                    _componentService.SaveConfiguration();
                }
            }
        }

        private async void BtnManageCache_Click(object sender, RoutedEventArgs e)
        {
            var cacheWindow = new CacheManagementWindow(this);
            await cacheWindow.ShowDialog<object>(this);
        }

        private async void BtnManageScanSources_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ManageScanSourcesWindow(this, _componentService);
            await dialog.ShowDialog<bool?>(this);
        }

        private void TglAutoScan_IsCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializingLanguage) return;
            if (sender is ToggleSwitch tgl)
            {
                _componentService.Config.AutoScan = tgl.IsChecked ?? true;
                _componentService.SaveConfiguration();
            }
        }

        private void TglAnimations_IsCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializingLanguage) return;
            if (sender is ToggleSwitch tgl)
            {
                _componentService.Config.AnimationsEnabled = tgl.IsChecked ?? true;
                _componentService.SaveConfiguration();
                UpdateAnimationsState(_componentService.Config.AnimationsEnabled);
            }
        }

        private void TglBetaVersions_IsCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializingLanguage) return;
            if (sender is ToggleSwitch tgl)
            {
                _componentService.Config.ShowBetaVersions = tgl.IsChecked ?? true;
                _componentService.SaveConfiguration();
            }
        }

        private void CmbDefaultExtrasVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingLanguage) return;
            if (sender is ComboBox cmb && cmb.SelectedItem is ComboBoxItem item)
            {
                var ver = item.Tag?.ToString() ?? "none";
                _componentService.Config.DefaultExtrasVersion = ver.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : ver;
                _componentService.SaveConfiguration();
            }
        }

        private void UpdateAnimationsState(bool enabled)
        {
            var duration = enabled ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero;
            
            // Update main view transitions
            foreach (var viewName in _viewNames)
            {
                var grid = this.FindControl<Grid>(viewName);
                if (grid?.Transitions != null)
                {
                    grid.Transitions.Clear();
                    if (enabled)
                    {
                        grid.Transitions.Add(new Avalonia.Animation.DoubleTransition 
                        { 
                            Property = Visual.OpacityProperty, 
                            Duration = duration,
                            Easing = new Avalonia.Animation.Easings.CubicEaseOut()
                        });
                    }
                }
            }
        }

        private async Task CheckUpdatesOnStartupAsync()
        {
            try
            {
                if (_txtStatus != null) _txtStatus.Text = GetResourceString("TxtCheckingUpdates", "Checking for updates...");
                await _componentService.CheckForUpdatesAsync();
            }
            catch { }
            finally
            {
                ComponentStatusChanged();
                if (_txtStatus != null) _txtStatus.Text = GetResourceString("TxtReady", "Ready");
            }
        }

        private void PopulateHelpContent()
        {
            var txtAppVersion = this.FindControl<TextBlock>("TxtAppVersion");
            var txtBuildDate = this.FindControl<TextBlock>("TxtBuildDate");
            
            if (txtAppVersion != null) txtAppVersion.Text = $"v{App.AppVersion}";

            try
            {
                var buildDate = System.IO.File.GetLastWriteTime(System.AppContext.BaseDirectory);
                if (txtBuildDate != null) txtBuildDate.Text = buildDate.ToString("yyyy-MM-dd");
            }
            catch
            {
                if (txtBuildDate != null) txtBuildDate.Text = "Unknown";
            }

            var txtOptiVersion = this.FindControl<TextBlock>("TxtOptiVersion");
            var bdOptiUpdate = this.FindControl<Border>("BdOptiUpdate");
            
            if (txtOptiVersion != null)
                txtOptiVersion.Text = string.IsNullOrWhiteSpace(_componentService.OptiScalerVersion) ? "Not installed" : _componentService.OptiScalerVersion;
            
            if (bdOptiUpdate != null)
                bdOptiUpdate.IsVisible = _componentService.IsOptiScalerUpdateAvailable;

            var txtFakeVersion = this.FindControl<TextBlock>("TxtFakeVersion");
            if (txtFakeVersion != null)
                txtFakeVersion.Text = string.IsNullOrWhiteSpace(_componentService.FakenvapiVersion) ? "Not installed" : _componentService.FakenvapiVersion;

            var txtNukemVersion = this.FindControl<TextBlock>("TxtNukemVersion");
            var btnUpdateNukemFG = this.FindControl<Button>("BtnUpdateNukemFG");
            if (_componentService.IsNukemFGInstalled)
            {
                var ver = _componentService.NukemFGVersion;
                if (txtNukemVersion != null) txtNukemVersion.Text = (string.IsNullOrWhiteSpace(ver) || ver == "manual") ? "Available" : ver;
                if (btnUpdateNukemFG != null) btnUpdateNukemFG.Content = "Update";
            }
            else
            {
                if (txtNukemVersion != null) txtNukemVersion.Text = "Not installed";
                if (btnUpdateNukemFG != null) btnUpdateNukemFG.Content = "Install";
            }
        }

        private async void BtnUpdateFakenvapi_Click(object sender, RoutedEventArgs e)
        {
            var btnUpdateFakenvapi = this.FindControl<Button>("BtnUpdateFakenvapi");
            if (btnUpdateFakenvapi == null) return;
            
            btnUpdateFakenvapi.IsEnabled = false;
            var originalContent = btnUpdateFakenvapi.Content;
            btnUpdateFakenvapi.Content = "Checking...";
            try
            {
                await _componentService.CheckForUpdatesAsync();
                
                if (_componentService.IsFakenvapiUpdateAvailable || string.IsNullOrEmpty(_componentService.FakenvapiVersion))
                {
                    btnUpdateFakenvapi.Content = "Downloading...";
                    await _componentService.DownloadAndExtractFakenvapiAsync();
                    await new ConfirmDialog(this, "Success", "Fakenvapi downloaded successfully.").ShowDialog<object>(this);
                    PopulateHelpContent();
                }
                else
                {
                    await new ConfirmDialog(this, "Up to date", "You already have the latest version of Fakenvapi.").ShowDialog<object>(this);
                }
            }
            catch (Exception ex)
            {
                await new ConfirmDialog(this, "Error", $"Error updating Fakenvapi: {ex.Message}").ShowDialog<object>(this);
            }
            finally
            {
                btnUpdateFakenvapi.Content = originalContent;
                btnUpdateFakenvapi.IsEnabled = true;
            }
        }

        private async void BtnUpdateNukemFG_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isUpdate = _componentService.IsNukemFGInstalled;
                DebugWindow.Log($"[NukemFG] Starting manual {(isUpdate ? "update" : "install")}");
                
                bool result = await _componentService.ProvideNukemFGManuallyAsync(isUpdate);
                
                if (result)
                {
                    DebugWindow.Log("[NukemFG] Manual process completed successfully.");
                    PopulateHelpContent();
                }
                else
                {
                    DebugWindow.Log("[NukemFG] Manual process cancelled or failed.");
                }
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[NukemFG] Error: {ex.Message}");
                await new ConfirmDialog(this, "Error", $"Error installing NukemFG: {ex.Message}").ShowDialog<object>(this);
            }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            var btnCheckUpdates = this.FindControl<Button>("BtnCheckUpdates");
            if (btnCheckUpdates == null) return;

            btnCheckUpdates.IsEnabled = false;
            var originalContent = btnCheckUpdates.Content;
            btnCheckUpdates.Content = GetResourceString("TxtCheckingUpdates", "Checking…");

            try
            {
                // 1. Check for component updates (Fakenvapi, etc)
                await _componentService.CheckForUpdatesAsync();
                PopulateHelpContent();

                // 2. Check for App Updates
                var appUpdateService = new AppUpdateService(_componentService);
                bool hasUpdate = await appUpdateService.CheckForAppUpdateAsync();

                if (hasUpdate)
                {
                    var updateTitle = GetResourceString("TxtUpdateAvailableTitle", "Update Available");
                    var updateMsgFormat = GetResourceString("TxtUpdateAvailableMsg", "A new version is available (v{0}). Download now?");
                    var updateMsg = string.Format(updateMsgFormat, appUpdateService.LatestVersion);

                    var dialog = new ConfirmDialog(this, updateTitle, updateMsg, false);
                    if (await dialog.ShowDialog<bool>(this)) // true if confirmed
                    {
                        btnCheckUpdates.Content = GetResourceString("TxtUpdatingApp", "Updating...");
                        
                        await appUpdateService.DownloadAndPrepareUpdateAsync(new Progress<double>(p => {
                            btnCheckUpdates.Content = $"{GetResourceString("TxtUpdatingApp", "Updating")} ({p:F0}%)";
                        }));

                        var readyTitle = GetResourceString("TxtUpdateReady", "Update Ready");
                        var readyMsg = GetResourceString("TxtUpdateReadyMsg", "Update downloaded. Restarting...");
                        
                        await new ConfirmDialog(this, readyTitle, readyMsg).ShowDialog<object>(this);
                        
                        appUpdateService.FinalizeAndRestart();
                        
                        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            desktop.Shutdown();
                        }
                    }
                }
                else if (appUpdateService.IsError)
                {
                    var errorMsg = GetResourceString("TxtUpdateCheckError", "There was a problem checking for updates.");
                    await new ConfirmDialog(this, GetResourceString("TxtUpdateError", "Error"), errorMsg).ShowDialog<object>(this);
                }
                else
                {
                    var noUpdateMsg = GetResourceString("TxtNoUpdateFound", "No new updates found.");
                    await new ConfirmDialog(this, GetResourceString("TxtReady", "Updates"), noUpdateMsg).ShowDialog<object>(this);
                }
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[AppUpdate] Fatal exception: {ex.Message}");
                var errorTitle = GetResourceString("TxtUpdateError", "Error");
                await new ConfirmDialog(this, errorTitle, $"Error: {ex.Message}").ShowDialog<object>(this);
            }
            finally
            {
                btnCheckUpdates.Content = originalContent;
                btnCheckUpdates.IsEnabled = true;
            }
        }

        private async void BtnGithub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var repoOwner = _componentService.Config.App.RepoOwner ?? "Agustinm28";
                var repoName = _componentService.Config.App.RepoName ?? "Optiscaler-Switcher";
                var url = $"https://github.com/{repoOwner}/{repoName}";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                await new ConfirmDialog(this, "Error", $"Could not open browser: {ex.Message}").ShowDialog<object>(this);
            }
        }

        private bool LoadSavedGames()
        {
            var savedGames = _persistenceService.LoadGames();
            _allGames = savedGames;
            
            ApplyFilter(_txtSearch?.Text);

            var loadedFormat = GetResourceString("TxtLoadedGamesFormat", "Loaded {0} games.");
            if (_txtStatus != null) _txtStatus.Text = string.Format(loadedFormat, savedGames.Count);

            if (savedGames.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    foreach (var game in savedGames)
                    {
                        try { _analyzerService.AnalyzeGame(game); }
                        catch { }

                        if (string.IsNullOrEmpty(game.CoverImageUrl) || game.CoverImageUrl.StartsWith("http"))
                        {
                            var appIdKey = !string.IsNullOrEmpty(game.AppId) ? game.AppId :
                                         !string.IsNullOrEmpty(game.Name) ? game.Name : Guid.NewGuid().ToString();

                            game.CoverImageUrl = await _metadataService.FetchAndCacheCoverImageAsync(game.Name, appIdKey);
                        }
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_lstGames != null)
                        {
                            _lstGames.ItemsSource = null;
                            _lstGames.ItemsSource = _games;
                        }
                        _persistenceService.SaveGames(_games);
                    });
                });
            }

            return savedGames.Count > 0;
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            if (_btnScan != null) _btnScan.IsEnabled = false;
            if (_txtStatus != null) _txtStatus.Text = GetResourceString("TxtScanningShort", "Scanning for games...");
            if (_overlayScanning != null) _overlayScanning.IsVisible = true;
            StartScanDotAnimation();

            try
            {
                List<Game> scanResults;
                if (_scannerService != null)
                {
                    scanResults = await _scannerService.ScanAllGamesAsync(_componentService.Config.ScanSources);
                }
                else
                {
                    scanResults = new List<Game>();
                }
                var manualGames = _games.Where(g => g.Platform == GamePlatform.Manual).ToList();

                _games.Clear();

                foreach (var manualGame in manualGames)
                {
                    _analyzerService.AnalyzeGame(manualGame);
                    _games.Add(manualGame);
                }

                foreach (var scannedGame in scanResults)
                {
                    if (!_games.Any(g => g.InstallPath.Equals(scannedGame.InstallPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (string.IsNullOrEmpty(scannedGame.CoverImageUrl))
                        {
                            var appIdKey = !string.IsNullOrEmpty(scannedGame.AppId) ? scannedGame.AppId : scannedGame.Name;
                            scannedGame.CoverImageUrl = await _metadataService.FetchAndCacheCoverImageAsync(scannedGame.Name, appIdKey);
                        }
                        _games.Add(scannedGame);
                    }
                }

                _allGames = _games.ToList();
                _persistenceService.SaveGames(_games);

                if (_txtSearch != null && !string.IsNullOrEmpty(_txtSearch.Text))
                {
                    ApplyFilter(_txtSearch.Text);
                }

                var scanCompleteFormat = GetResourceString("TxtScanCompleteFormat", "Scan complete. Total games: {0}");
                if (_txtStatus != null) _txtStatus.Text = string.Format(scanCompleteFormat, _games.Count);
            }
            catch (Exception ex)
            {
                var errorFormat = GetResourceString("TxtErrorFormat", "Error: {0}");
                if (_txtStatus != null) _txtStatus.Text = string.Format(errorFormat, ex.Message);
                await new ConfirmDialog(this, "Error", ex.Message).ShowDialog<object>(this);
            }
            finally
            {
                StopScanDotAnimation();
                if (_btnScan != null) _btnScan.IsEnabled = true;
                if (_overlayScanning != null) _overlayScanning.IsVisible = false;
            }
        }

        private async void BtnAddManual_Click(object sender, RoutedEventArgs e)
        {
            var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = GetResourceString("TxtSelectExe", "Select Game Executable"),
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new FilePickerFileType("Executable Files (*.exe)")
                    {
                        Patterns = new List<string> { "*.exe" }
                    }
                }
            });

            if (files != null && files.Count > 0)
            {
                try
                {
                    var filePath = files[0].Path.LocalPath;
                    var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                    var installDir = System.IO.Path.GetDirectoryName(filePath) ?? "";

                    var newGame = new Game
                    {
                        Name = fileName,
                        InstallPath = installDir,
                        ExecutablePath = filePath,
                        Platform = GamePlatform.Manual,
                        AppId = "Manual_" + Guid.NewGuid().ToString().Substring(0, 8)
                    };

                    _analyzerService.AnalyzeGame(newGame);
                    newGame.CoverImageUrl = await _metadataService.FetchAndCacheCoverImageAsync(newGame.Name, newGame.AppId);

                    _games.Insert(0, newGame);
                    _allGames = _games.ToList();
                    _persistenceService.SaveGames(_games);

                    if (_lstGames != null)
                    {
                        _lstGames.ItemsSource = null;
                        _lstGames.ItemsSource = _games;
                    }

                    if (_txtStatus != null) _txtStatus.Text = string.Format(GetResourceString("TxtAddedRefFormat", "Added {0} manually."), newGame.Name);
                }
                catch (Exception ex)
                {
                    await new ConfirmDialog(this, GetResourceString("TxtError", "Error"), ex.Message, isAlert: true).ShowDialog<object>(this);
                }
            }
        }

        private async void BtnBulkInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_games.Count == 0)
            {
                await new ConfirmDialog(
                    this,
                    GetResourceString("TxtNoGames", "No Games"),
                    GetResourceString("TxtNoGamesFound", "No games found. Please scan for games first."),
                    isAlert: true
                ).ShowDialog<bool>(this);
                return;
            }

            var installService = new GameInstallationService();
            var bulkWindow = new BulkInstallWindow(_componentService, installService, _games.ToList());
            await bulkWindow.ShowDialog<object>(this);

            // Refresh game list after bulk install
            if (_lstGames != null)
            {
                _lstGames.ItemsSource = null;
                _lstGames.ItemsSource = _games;
            }
        }

        private async void BtnManage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Game selectedGame)
            {
                var manageWindow = new ManageGameWindow(this, selectedGame);
                await manageWindow.ShowDialog<object>(this);

                var index = _games.IndexOf(selectedGame);
                if (index != -1)
                {
                    _games[index] = selectedGame;
                    _persistenceService.SaveGames(_games);
                }

                if (_lstGames != null)
                {
                    _lstGames.ItemsSource = null;
                    _lstGames.ItemsSource = _games;
                }
            }
        }

        private void BtnFastInstall_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Game game)
            {
                UpdateFastInstallButton(button, game);
            }
        }

        private void UpdateFastInstallButton(Button button, Game game)
        {
            if (game.IsOptiscalerInstalled)
            {
                button.Content = "🗑️ Quick Uninstall";
                button.Foreground = this.FindResource("BrAccentWarm") as IBrush ?? Brushes.Orange;
            }
            else
            {
                button.Content = "✦ Quick Install";
                button.Foreground = this.FindResource("BrAccent") as IBrush ?? Brushes.Purple;
            }
        }

        private async void BtnFastInstall_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Game selectedGame)
            {
                try
                {
                    // Check if OptiScaler is already installed
                    if (selectedGame.IsOptiscalerInstalled)
                    {
                        // Uninstall OptiScaler directly without confirmation
                        var installService = new GameInstallationService();
                        installService.UninstallOptiScaler(selectedGame);
                        
                        // Update game status
                        selectedGame.IsOptiscalerInstalled = false;
                        selectedGame.OptiscalerVersion = null;
                        
                        // Refresh UI
                        if (_lstGames != null)
                        {
                            _lstGames.ItemsSource = null;
                            _lstGames.ItemsSource = _games;
                        }
                        
                        _persistenceService.SaveGames(_games);
                    }
                    else
                    {
                        // Install OptiScaler
                        var installService = new GameInstallationService();
                        
                        // Determine version to install based on beta setting
                        string versionToInstall;
                        
                        if (_componentService.Config.ShowBetaVersions)
                        {
                            // Install latest beta
                            versionToInstall = _componentService.LatestBetaVersion ?? "";
                        }
                        else
                        {
                            // Install latest stable (use the version marked as latest in GitHub)
                            versionToInstall = _componentService.LatestStableVersion ?? "";
                        }
                        
                        if (string.IsNullOrEmpty(versionToInstall))
                        {
                            await new ConfirmDialog(
                                this,
                                GetResourceString("TxtNoVersions", "No Versions Available"),
                                GetResourceString("TxtNoVersionsFound", "No OptiScaler versions are available for installation."),
                                isAlert: true
                            ).ShowDialog<bool>(this);
                            return;
                        }
                        
                        // Get cache paths
                        var optiCacheDir = _componentService.GetOptiScalerCachePath(versionToInstall);
                        
                        // Download OptiScaler if not in cache
                        if (!Directory.Exists(optiCacheDir) || Directory.GetFiles(optiCacheDir, "*.*", SearchOption.AllDirectories).Length == 0)
                        {
                            // Show downloading dialog
                            var downloadDialog = new ConfirmDialog(
                                this,
                                "Downloading OptiScaler",
                                $"Downloading OptiScaler {versionToInstall}...\nPlease wait.",
                                isAlert: true
                            );
                            
                            // Start download in background
                            var downloadTask = _componentService.DownloadOptiScalerAsync(versionToInstall);
                            
                            // Show dialog without blocking
                            var dialogTask = downloadDialog.ShowDialog<bool>(this);
                            
                            try
                            {
                                // Wait for download to complete
                                await downloadTask;
                                
                                // Close dialog after download completes
                                downloadDialog.Close();
                            }
                            catch (Exception downloadEx)
                            {
                                // Close downloading dialog
                                downloadDialog.Close();
                                
                                // Show error dialog
                                await new ConfirmDialog(
                                    this,
                                    GetResourceString("TxtError", "Error"),
                                    $"Failed to download OptiScaler {versionToInstall}: {downloadEx.Message}",
                                    isAlert: true
                                ).ShowDialog<bool>(this);
                                return;
                            }
                        }
                        
                        var fakeCacheDir = _componentService.GetFakenvapiCachePath();
                        var nukemCacheDir = _componentService.GetNukemFGCachePath();
                        
                        // Install with default settings (backup always enabled)
                        // Always install Fakenvapi and NukemFG by default
                        installService.InstallOptiScaler(
                            selectedGame,
                            optiCacheDir,
                            "dxgi.dll",
                            installFakenvapi: true, // Always install Fakenvapi
                            fakenvapiCachePath: fakeCacheDir,
                            installNukemFG: true,  // Always install NukemFG
                            nukemFGCachePath: nukemCacheDir,
                            optiscalerVersion: versionToInstall
                        );
                        
                        // Update game status
                        selectedGame.IsOptiscalerInstalled = true;
                        selectedGame.OptiscalerVersion = versionToInstall;
                        
                        // Refresh UI
                        if (_lstGames != null)
                        {
                            _lstGames.ItemsSource = null;
                            _lstGames.ItemsSource = _games;
                        }
                        
                        _persistenceService.SaveGames(_games);
                    }
                }
                catch (Exception ex)
                {
                    await new ConfirmDialog(
                        this,
                        GetResourceString("TxtError", "Error"),
                        ex.Message,
                        isAlert: true
                    ).ShowDialog<bool>(this);
                }
            }
        }

        private async void BtnRemoveGame_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Game game)
            {
                var title = GetResourceString("TxtRemoveGameTitle", "Remove Game");
                var confirmFormat = GetResourceString("TxtRemoveGameConfirm", "Are you sure you want to remove '{0}' from the list?");
                var message = string.Format(confirmFormat, game.Name);

                var dialog = new ConfirmDialog(this, title, message, false);
                var result = await dialog.ShowDialog<bool>(this); // true if confirmed

                if (result)
                {
                    _games.Remove(game);
                    _persistenceService.SaveGames(_games);
                }
            }
        }

        private async Task LoadGpuInfoAsync()
        {
            try
            {
                if (_txtGpuInfo == null) return;
                
                GpuInfo? gpu;
                if (_lastDetectedGpu != null)
                {
                    gpu = _lastDetectedGpu;
                }
                else
                {
                    _txtGpuInfo!.Text = GetResourceString("TxtDefaultGpu", "Detecting GPU...");
                    gpu = await Task.Run(() =>
                    {
                        if (_gpuService != null)
                        {
                            try
                            {
                                return _gpuService.GetDiscreteGPU() ?? _gpuService.GetPrimaryGPU();
                            }
                            catch { return null; }
                        }
                        return null;
                    });
                    _lastDetectedGpu = gpu;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (gpu != null)
                    {
                        string icon = "⚪";
                        IBrush color = Brushes.Gray;

                        switch (gpu.Vendor)
                        {
                            case GpuVendor.NVIDIA:
                                icon = "🟢"; color = new SolidColorBrush(Color.FromRgb(118, 185, 0)); break;
                            case GpuVendor.AMD:
                                icon = "🔴"; color = new SolidColorBrush(Color.FromRgb(237, 28, 36)); break;
                            case GpuVendor.Intel:
                                icon = "🔵"; color = new SolidColorBrush(Color.FromRgb(0, 113, 197)); break;
                        }

                        _txtGpuInfo!.Text = $"{icon} {gpu.Name}";
                        _txtGpuInfo.Foreground = color;
                        ToolTip.SetTip(_txtGpuInfo, $"{gpu.Name}\nVendor: {gpu.Vendor}\nVRAM: {gpu.VideoMemoryGB}\nDriver: {gpu.DriverVersion}");
                    }
                    else
                    {
                        _txtGpuInfo!.Text = GetResourceString("TxtNoGpu", "⚠️ No GPU detected");
                        _txtGpuInfo.Foreground = Brushes.Orange;
                        ToolTip.SetTip(_txtGpuInfo, GetResourceString("TxtNoGpuTip", "No GPU was detected on this system"));
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_txtGpuInfo != null)
                    {
                        _txtGpuInfo.Text = GetResourceString("TxtGpuFail", "⚠️ GPU detection failed");
                        _txtGpuInfo.Foreground = Brushes.Gray;
                        var format = GetResourceString("TxtGpuFailTipFormat", "Error detecting GPU: {0}");
                        ToolTip.SetTip(_txtGpuInfo, string.Format(format, ex.Message));
                    }
                });
            }
        }

        private void StartScanDotAnimation()
        {
            var dot1 = this.FindControl<Ellipse>("ScanDot1");
            var dot2 = this.FindControl<Ellipse>("ScanDot2");
            var dot3 = this.FindControl<Ellipse>("ScanDot3");
            if (dot1 == null || dot2 == null || dot3 == null) return;

            var t1 = new Avalonia.Media.TranslateTransform();
            var t2 = new Avalonia.Media.TranslateTransform();
            var t3 = new Avalonia.Media.TranslateTransform();
            dot1.RenderTransform = t1;
            dot2.RenderTransform = t2;
            dot3.RenderTransform = t3;

            const double amplitude = 10;
            const double step = 0.25;
            const double phaseOffset = Math.PI * 2 / 3;

            _scanDotPhase = 0;
            _scanDotTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _scanDotTimer.Tick += (s, e) =>
            {
                _scanDotPhase += step;
                t1.Y = -amplitude * Math.Max(0, Math.Sin(_scanDotPhase));
                t2.Y = -amplitude * Math.Max(0, Math.Sin(_scanDotPhase + phaseOffset));
                t3.Y = -amplitude * Math.Max(0, Math.Sin(_scanDotPhase + phaseOffset * 2));
            };
            _scanDotTimer.Start();
        }

        private void StopScanDotAnimation()
        {
            _scanDotTimer?.Stop();
            _scanDotTimer = null;
        }

        #region Window State Persistence

        private void RestoreWindowState()
        {
            var config = _componentService.Config;
            
            // Restore window size
            if (config.WindowWidth > 0 && config.WindowHeight > 0)
            {
                this.Width = config.WindowWidth;
                this.Height = config.WindowHeight;
            }
            
            // Restore window position (only if valid)
            if (!double.IsNaN(config.WindowLeft) && !double.IsNaN(config.WindowTop) &&
                config.WindowLeft >= 0 && config.WindowTop >= 0)
            {
                this.Position = new PixelPoint((int)config.WindowLeft, (int)config.WindowTop);
            }
            
            // Restore maximized state
            if (config.WindowMaximized)
            {
                this.WindowState = WindowState.Maximized;
            }
        }

        private void Window_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            // Save window state on any relevant property change
            SaveWindowState();
        }

        private void SaveWindowState()
        {
            var config = _componentService.Config;
            
            // Save window size
            if (this.WindowState != WindowState.Maximized)
            {
                config.WindowWidth = this.Width;
                config.WindowHeight = this.Height;
            }
            
            // Save window position
            var position = this.Position;
            config.WindowLeft = position.X;
            config.WindowTop = position.Y;
            
            // Save maximized state
            config.WindowMaximized = this.WindowState == WindowState.Maximized;
            
            // Save configuration
            _componentService.SaveConfiguration();
        }

        #endregion

        private string GetResourceString(string key, string fallback)
        {
            return Application.Current?.TryFindResource(key, out var res) == true && res is string str ? str : fallback;
        }
    }
}