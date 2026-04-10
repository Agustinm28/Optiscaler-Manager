using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using OptiscalerClient.Models;
using OptiscalerClient.Services;
using OptiscalerClient.Helpers;
using System.Text.RegularExpressions;

namespace OptiscalerClient.Views;

public partial class BulkInstallWindow : Window
{
    private readonly ComponentManagementService _componentService;
    private readonly GameInstallationService _installService;
    private readonly IGpuDetectionService _gpuService;
    private readonly ObservableCollection<BulkGameItem> _gameItems;
    private readonly ObservableCollection<BulkGameItem> _filteredGameItems;
    private List<BulkGameItem> _allGames = new List<BulkGameItem>();
    private bool _isInstalling = false;
    private readonly ProfileManagementService _profileService;
    private Window? _ownerWindow;
    private string? _lastSelectedProfileName;
    private bool _isUpdatingProfiles = false;
    private const string NewProfileTag = "__new_profile__";

    public BulkInstallWindow()
    {
        InitializeComponent();

        // Initialize fields to avoid nullable warnings
        _componentService = null!;
        _profileService = null!;
        _installService = null!;
        _gpuService = null!;
        _gameItems = new ObservableCollection<BulkGameItem>();
        _filteredGameItems = new ObservableCollection<BulkGameItem>();
    }

    public BulkInstallWindow(
        ComponentManagementService componentService,
        GameInstallationService installService,
        List<Game> games,
        Window? owner = null)
    {
        InitializeComponent();

        _componentService = componentService;
        _installService = installService;
        _profileService = new ProfileManagementService();
        _ownerWindow = owner;
        _gameItems = new ObservableCollection<BulkGameItem>();
        _filteredGameItems = new ObservableCollection<BulkGameItem>();

        // Initialize GPU service
        if (OperatingSystem.IsWindows())
            _gpuService = new WindowsGpuDetectionService();
        else if (OperatingSystem.IsLinux())
            _gpuService = new LinuxGpuDetectionService();
        else
            _gpuService = null!;

        // Populate games list
        foreach (var game in games.OrderBy(g => g.Name))
        {
            var gameItem = new BulkGameItem
            {
                Game = game,
                Name = game.Name,
                Platform = game.Platform.ToString(),
                CoverPath = game.CoverImageUrl,
                IsInstalled = game.IsOptiscalerInstalled,
                CanInstall = !game.IsOptiscalerInstalled,
                IsSelected = false, // Start with all items unchecked
                OptiscalerVersion = game.OptiscalerVersion,
                IsOptiscalerInstalled = game.IsOptiscalerInstalled
            };

            _gameItems.Add(gameItem);
            _allGames.Add(gameItem);
            _filteredGameItems.Add(gameItem);
        }

        var gamesList = this.FindControl<ItemsControl>("GamesList");
        if (gamesList != null)
        {
            gamesList.ItemsSource = _filteredGameItems;
        }

        // Load versions
        _ = LoadVersionsAsync();

        // Update selection count
        UpdateSelectionCount();

        // Subscribe to selection changes
        foreach (var item in _gameItems)
        {
            item.PropertyChanged += GameItem_PropertyChanged;
        }

        // Setup version selection handler
        var cmbOptiVersion = this.FindControl<ComboBox>("CmbOptiVersion");
        if (cmbOptiVersion != null)
        {
            cmbOptiVersion.SelectionChanged += CmbOptiVersion_SelectionChanged;
        }

        // Initialize injection method selector
        var cmbInjectionMethod = this.FindControl<ComboBox>("CmbInjectionMethod");
        if (cmbInjectionMethod != null)
        {
            cmbInjectionMethod.SelectedIndex = 0; // Default to dxgi.dll
        }

        // Populate FSR4 INT8 versions
        PopulateExtrasComboBox();

        // Populate OptiPatcher versions
        PopulateOptiPatcherComboBox();

        // Populate profile selector
        PopulateProfileSelector();

        // Fade in animation
        var rootPanel = this.FindControl<Panel>("RootPanel");
        if (rootPanel != null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                rootPanel.Transitions = new Avalonia.Animation.Transitions
                {
                    new Avalonia.Animation.DoubleTransition
                    {
                        Property = Panel.OpacityProperty,
                        Duration = TimeSpan.FromMilliseconds(200)
                    }
                };
                rootPanel.Opacity = 1;
            }, DispatcherPriority.Render);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async Task LoadVersionsAsync()
    {
        // Check if we need to fetch versions
        if (_componentService.OptiScalerAvailableVersions.Count == 0)
        {
            await _componentService.CheckForUpdatesAsync();
        }

        Dispatcher.UIThread.Post(() =>
        {
            var allVersions = _componentService.OptiScalerAvailableVersions;
            var betaVersions = _componentService.BetaVersions;
            var latestBeta = _componentService.LatestBetaVersion;

            var cmbOptiVersion = this.FindControl<ComboBox>("CmbOptiVersion");
            if (cmbOptiVersion == null) return;

            cmbOptiVersion.Items.Clear();

            if (allVersions.Count == 0)
            {
                cmbOptiVersion.Items.Add("No versions available");
                cmbOptiVersion.SelectedIndex = 0;
                cmbOptiVersion.IsEnabled = false;
                return;
            }

            var stableVersions = allVersions.Where(v => !betaVersions.Contains(v)).ToList();
            var otherBetas = allVersions.Where(v => betaVersions.Contains(v) && v != latestBeta).ToList();

            int selectedIndex = 0;
            int currentIndex = 0;

            bool hasBeta = !string.IsNullOrEmpty(latestBeta);

            // Add latest beta first - NO LATEST badge for beta
            if (hasBeta && latestBeta != null)
            {
                cmbOptiVersion.Items.Add(BuildVersionItem(latestBeta, isBeta: true, isLatest: false));
                selectedIndex = 0; // Select beta by default
                currentIndex++;
            }

            var latestStable = _componentService.LatestStableVersion;

            // Add stable versions
            bool isLatestStableMarked = false;
            foreach (var ver in stableVersions)
            {
                bool shouldMarkAsLatest = false;

                if (!string.IsNullOrEmpty(latestStable))
                {
                    shouldMarkAsLatest = ver.Equals(latestStable, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    shouldMarkAsLatest = !isLatestStableMarked && !ver.Contains("nightly", StringComparison.OrdinalIgnoreCase);
                }

                if (shouldMarkAsLatest)
                {
                    isLatestStableMarked = true;
                    // If we didn't default to beta, default to this latest stable
                    if (!hasBeta)
                    {
                        selectedIndex = currentIndex;
                    }
                }

                cmbOptiVersion.Items.Add(BuildVersionItem(ver, isBeta: false, isLatest: shouldMarkAsLatest));
                currentIndex++;
            }

            // Add other betas
            foreach (var ver in otherBetas)
            {
                cmbOptiVersion.Items.Add(BuildVersionItem(ver, isBeta: true, isLatest: false));
                currentIndex++;
            }

            // Override with user-configured default version if set
            var configDefault = _componentService.Config.DefaultOptiScalerVersion;
            if (!string.IsNullOrEmpty(configDefault))
            {
                for (int i = 0; i < cmbOptiVersion.Items.Count; i++)
                {
                    if (cmbOptiVersion.Items[i] is ComboBoxItem ci && string.Equals(ci.Tag?.ToString(), configDefault, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            cmbOptiVersion.SelectedIndex = selectedIndex;

            // Refresh OptiPatcher list after versions are loaded
            PopulateOptiPatcherComboBox();
        });
    }

    private static ComboBoxItem BuildVersionItem(string ver, bool isBeta, bool isLatest)
    {
        var stack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = ver, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });

        if (isBeta)
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.Parse("#D4A017")),
                Padding = new Thickness(5, 1),
                Child = new TextBlock { Text = "BETA", FontSize = 10, Foreground = Brushes.White, FontWeight = FontWeight.Bold }
            };
            stack.Children.Add(badge);
        }

        if (isLatest)
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.Parse("#7C3AED")),
                Padding = new Thickness(5, 1),
                Child = new TextBlock { Text = "LATEST", FontSize = 10, Foreground = Brushes.White, FontWeight = FontWeight.Bold }
            };
            stack.Children.Add(badge);
        }

        return new ComboBoxItem { Content = stack, Tag = ver };
    }

    private void GameItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BulkGameItem.IsSelected))
        {
            UpdateSelectionCount();
            UpdateSelectAllCheckbox();
        }
    }

    private void UpdateSelectionCount()
    {
        var selectedCount = _gameItems.Count(g => g.IsSelected && g.CanInstall);
        var txtCount = this.FindControl<TextBlock>("TxtSelectionCount");
        var btnInstall = this.FindControl<Button>("BtnInstall");

        if (txtCount != null)
        {
            txtCount.Text = selectedCount == 1
                ? "1 game selected"
                : $"{selectedCount} games selected";
        }

        if (btnInstall != null)
        {
            btnInstall.Content = selectedCount == 0
                ? "Install Selected"
                : selectedCount == 1
                    ? "Install 1 game"
                    : $"Install {selectedCount} games";
            btnInstall.IsEnabled = selectedCount > 0 && !_isInstalling;
        }
    }

    private void UpdateSelectAllCheckbox()
    {
        var chkSelectAll = this.FindControl<CheckBox>("ChkSelectAll");
        if (chkSelectAll == null) return;

        var selectableGames = _gameItems.Where(g => g.CanInstall).ToList();
        if (selectableGames.Count == 0)
        {
            chkSelectAll.IsChecked = false;
            return;
        }

        var selectedCount = selectableGames.Count(g => g.IsSelected);

        if (selectedCount == 0)
            chkSelectAll.IsChecked = false;
        else if (selectedCount == selectableGames.Count)
            chkSelectAll.IsChecked = true;
        else
            chkSelectAll.IsChecked = null; // Indeterminate state
    }

    private void ChkSelectAll_Click(object? sender, RoutedEventArgs e)
    {
        var chkSelectAll = sender as CheckBox;
        if (chkSelectAll == null) return;

        bool shouldSelect = chkSelectAll.IsChecked == true;

        foreach (var item in _gameItems.Where(g => g.CanInstall))
        {
            item.IsSelected = shouldSelect;
        }
    }

    private async void BtnInstall_Click(object? sender, RoutedEventArgs e)
    {
        var selectedGames = _gameItems.Where(g => g.IsSelected && g.CanInstall).ToList();
        if (selectedGames.Count == 0) return;

        var cmbOptiVersion = this.FindControl<ComboBox>("CmbOptiVersion");
        var cmbInjectionMethod = this.FindControl<ComboBox>("CmbInjectionMethod");
        var cmbExtrasVersion = this.FindControl<ComboBox>("CmbExtrasVersion");
        var cmbOptiPatcher = this.FindControl<ComboBox>("CmbOptiPatcherVersion");
        var chkFakenvapi = this.FindControl<CheckBox>("ChkFakenvapi");
        var chkNukemFG = this.FindControl<CheckBox>("ChkNukemFG");
        var cmbProfile = this.FindControl<ComboBox>("CmbProfile");

        if (cmbOptiVersion?.SelectedItem is not ComboBoxItem selectedItem) return;

        string version = selectedItem.Tag?.ToString() ?? "";
        bool installFakenvapi = chkFakenvapi?.IsChecked == true;
        bool installNukemFG = chkNukemFG?.IsChecked == true;

        // Get injection method
        var injectionItem = cmbInjectionMethod?.SelectedItem as ComboBoxItem;
        string injectionMethod = injectionItem?.Tag?.ToString() ?? "dxgi.dll";

        // Get selected Extras (FSR4 INT8) version
        var selectedExtrasItem = cmbExtrasVersion?.SelectedItem as ComboBoxItem;
        var selectedExtrasVersion = selectedExtrasItem?.Tag?.ToString();
        bool injectExtras = !string.IsNullOrEmpty(selectedExtrasVersion) &&
                            !selectedExtrasVersion.Equals("none", StringComparison.OrdinalIgnoreCase);

        // Get selected OptiPatcher version
        var selectedOptiPatcherItem = cmbOptiPatcher?.SelectedItem as ComboBoxItem;
        var selectedOptiPatcherVersion = selectedOptiPatcherItem?.Tag?.ToString();
        bool installOptiPatcher = !string.IsNullOrEmpty(selectedOptiPatcherVersion) &&
                                  !selectedOptiPatcherVersion.Equals("none", StringComparison.OrdinalIgnoreCase);

        // Get selected profile
        OptiScalerProfile? selectedProfile = null;
        if (cmbProfile?.SelectedItem is ComboBoxItem profileItem && profileItem.Tag is OptiScalerProfile prof)
            selectedProfile = prof;

        _isInstalling = true;

        var btnInstall = this.FindControl<Button>("BtnInstall");
        var btnCancel = this.FindControl<Button>("BtnCancel");
        var progressSection = this.FindControl<Border>("ProgressSection");
        var txtProgressStatus = this.FindControl<TextBlock>("TxtProgressStatus");
        var txtProgressCount = this.FindControl<TextBlock>("TxtProgressCount");
        var progressBar = this.FindControl<ProgressBar>("ProgressBar");

        if (btnInstall != null) btnInstall.IsEnabled = false;
        if (btnCancel != null) btnCancel.IsEnabled = false;
        if (progressSection != null) progressSection.IsVisible = true;

        int totalGames = selectedGames.Count;
        int currentGame = 0;

        foreach (var gameItem in selectedGames)
        {
            currentGame++;

            if (txtProgressStatus != null)
                txtProgressStatus.Text = $"Installing {gameItem.Name}...";

            if (txtProgressCount != null)
                txtProgressCount.Text = $"{currentGame} / {totalGames}";

            if (progressBar != null)
                progressBar.Value = (currentGame - 1) * 100.0 / totalGames;

            try
            {
                // Get cache paths
                var optiCacheDir = _componentService.GetOptiScalerCachePath(version);
                var fakeCacheDir = installFakenvapi ? _componentService.GetFakenvapiCachePath() : "";
                var nukemCacheDir = installNukemFG ? _componentService.GetNukemFGCachePath() : "";

                await Task.Run(() =>
                {
                    _installService.InstallOptiScaler(
                        gameItem.Game,
                        optiCacheDir,
                        injectionMethod, // Use selected injection method
                        installFakenvapi,
                        fakeCacheDir,
                        installNukemFG,
                        nukemCacheDir,
                        optiscalerVersion: version,
                        profile: selectedProfile
                    );
                });

                // ── FSR4 INT8 DLL injection ────────────────────────────────────────
                if (injectExtras && !string.IsNullOrEmpty(selectedExtrasVersion))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (txtProgressStatus != null) txtProgressStatus.Text = $"Downloading FSR4 INT8 v{selectedExtrasVersion} for {gameItem.Name}...";
                        if (progressBar != null) progressBar.IsIndeterminate = false;
                    });

                    string extrasDllPath;
                    try
                    {
                        var extrasProgress = new Progress<double>(p =>
                            Dispatcher.UIThread.Post(() => { if (progressBar != null) progressBar.Value = p; }));

                        extrasDllPath = await _componentService.DownloadExtrasDllAsync(selectedExtrasVersion, extrasProgress);
                    }
                    catch (Exception ex)
                    {
                        DebugWindow.Log($"[BulkInstall] Failed to download FSR4 INT8 v{selectedExtrasVersion}: {ex.Message}");
                        continue; // Skip FSR4 installation but continue with OptiScaler
                    }

                    // Copy FSR4 INT8 DLL to game directory
                    await Task.Run(() =>
                    {
                        var gameDir = _installService.DetermineInstallDirectory(gameItem.Game) ?? gameItem.Game.InstallPath;
                        var destPath = System.IO.Path.Combine(gameDir, "amd_fidelityfx_upscaler_dx12.dll");
                        System.IO.File.Copy(extrasDllPath, destPath, overwrite: true);
                        gameItem.Game.Fsr4ExtraVersion = selectedExtrasVersion;
                        DebugWindow.Log($"[BulkInstall] Copied FSR4 INT8 DLL to {destPath} for {gameItem.Name}");
                    });
                }

                // ── OptiPatcher ────────────────────────────────────────────────────
                if (installOptiPatcher && !string.IsNullOrEmpty(selectedOptiPatcherVersion))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (txtProgressStatus != null) txtProgressStatus.Text = $"Downloading OptiPatcher {selectedOptiPatcherVersion} for {gameItem.Name}...";
                        if (progressBar != null) progressBar.IsIndeterminate = true;
                    });

                    try
                    {
                        var optiPatcherProgress = new Progress<double>(p =>
                            Dispatcher.UIThread.Post(() => { if (progressBar != null) { progressBar.IsIndeterminate = false; progressBar.Value = p; } }));

                        var optiPatcherAsiPath = await _componentService.DownloadOptiPatcherAsync(selectedOptiPatcherVersion, optiPatcherProgress);

                        await Task.Run(() =>
                        {
                            var gameDir = _installService.DetermineInstallDirectory(gameItem.Game) ?? gameItem.Game.InstallPath;

                            // Create plugins folder and copy the .asi
                            var pluginsDir = System.IO.Path.Combine(gameDir, "plugins");
                            System.IO.Directory.CreateDirectory(pluginsDir);
                            var destAsi = System.IO.Path.Combine(pluginsDir, "OptiPatcher.asi");
                            System.IO.File.Copy(optiPatcherAsiPath, destAsi, overwrite: true);
                            DebugWindow.Log($"[BulkInstall][OptiPatcher] Installed to {destAsi}");

                            // Patch OptiScaler.ini: ensure LoadAsiPlugins=true
                            var iniPath = System.IO.Path.Combine(gameDir, "OptiScaler.ini");
                            if (System.IO.File.Exists(iniPath))
                            {
                                var lines = System.IO.File.ReadAllLines(iniPath).ToList();
                                bool found = false;
                                for (int idx = 0; idx < lines.Count; idx++)
                                {
                                    var trimmed = lines[idx].Trim();
                                    if (trimmed.StartsWith("LoadAsiPlugins", StringComparison.OrdinalIgnoreCase) &&
                                        (trimmed.Length == "LoadAsiPlugins".Length || trimmed["LoadAsiPlugins".Length] == '='))
                                    {
                                        lines[idx] = "LoadAsiPlugins=true";
                                        found = true;
                                        break;
                                    }
                                }
                                if (!found) lines.Add("LoadAsiPlugins=true");
                                System.IO.File.WriteAllLines(iniPath, lines);
                                DebugWindow.Log($"[BulkInstall][OptiPatcher] Patched OptiScaler.ini for {gameItem.Name}");
                            }
                        });

                        Dispatcher.UIThread.Post(() => { if (progressBar != null) progressBar.IsIndeterminate = false; });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.UIThread.Post(() => { if (progressBar != null) progressBar.IsIndeterminate = false; });
                        DebugWindow.Log($"[BulkInstall][OptiPatcher] Failed for {gameItem.Name}: {ex.Message}");
                    }
                }

                gameItem.IsInstalled = true;
                gameItem.CanInstall = false;
                gameItem.IsSelected = false;
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[BulkInstall] Failed to install {gameItem.Name}: {ex.Message}");
            }

            await Task.Delay(100); // Small delay between installations
        }

        if (progressBar != null)
            progressBar.Value = 100;

        await Task.Delay(500);

        _isInstalling = false;

        if (progressSection != null) progressSection.IsVisible = false;
        if (btnCancel != null) btnCancel.IsEnabled = true;

        UpdateSelectionCount();

        // Show completion dialog
        var completedCount = totalGames;
        await new ConfirmDialog(
            this,
            "Bulk Installation Complete",
            $"Successfully installed OptiScaler on {completedCount} game{(completedCount != 1 ? "s" : "")}.",
            isAlert: true
        ).ShowDialog<bool>(this);

        Close();
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        if (!_isInstalling)
        {
            Close();
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        if (!_isInstalling)
        {
            Close();
        }
    }

    private void CmbOptiVersion_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateCheckboxStatesForVersion(sender as ComboBox);
    }

    private void UpdateCheckboxStatesForVersion(ComboBox? cmb)
    {
        if (cmb == null) return;

        var selectedTag = (cmb?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        bool isBeta = !string.IsNullOrEmpty(selectedTag) && _componentService.BetaVersions.Contains(selectedTag);

        var chkFakenvapi = this.FindControl<CheckBox>("ChkFakenvapi");
        var chkNukemFG = this.FindControl<CheckBox>("ChkNukemFG");

        // Disable Fakenvapi/NukemFG for any OptiScaler version >= 0.9 regardless of beta
        bool includedInPackage = IsVersionGreaterOrEqual(selectedTag, 0, 9);

        if (includedInPackage)
        {
            if (chkFakenvapi != null)
            {
                chkFakenvapi.IsEnabled = false;
                chkFakenvapi.IsChecked = false;
                ToolTip.SetTip(chkFakenvapi, "Included in OptiScaler 0.9+");
            }
            if (chkNukemFG != null)
            {
                chkNukemFG.IsEnabled = false;
                chkNukemFG.IsChecked = false;
                ToolTip.SetTip(chkNukemFG, "Included in OptiScaler 0.9+");
            }
        }
        else
        {
            if (chkFakenvapi != null)
            {
                chkFakenvapi.IsEnabled = true;
                ToolTip.SetTip(chkFakenvapi, null);
            }
            if (chkNukemFG != null)
            {
                chkNukemFG.IsEnabled = true;
                ToolTip.SetTip(chkNukemFG, null);
            }
        }
    }

    private static bool IsVersionGreaterOrEqual(string? ver, int targetMajor, int targetMinor)
    {
        if (string.IsNullOrEmpty(ver)) return false;

        var m = Regex.Match(ver, "^\\d+(\\.\\d+)*");
        if (!m.Success) return false;

        if (!Version.TryParse(m.Value, out var parsed)) return false;

        if (parsed.Major > targetMajor) return true;
        if (parsed.Major < targetMajor) return false;
        return parsed.Minor >= targetMinor;
    }

    private void PopulateProfileSelector()
    {
        var cmbProfile = this.FindControl<ComboBox>("CmbProfile");
        if (cmbProfile == null) return;

        _isUpdatingProfiles = true;
        cmbProfile.SelectionChanged -= CmbProfile_SelectionChanged;
        cmbProfile.Items.Clear();

        var profiles = _profileService.GetAllProfiles();
        foreach (var profile in profiles)
        {
            var item = new ComboBoxItem { Content = profile.Name, Tag = profile };
            ToolTip.SetTip(item, profile.Description);
            cmbProfile.Items.Add(item);
        }

        cmbProfile.Items.Add(new ComboBoxItem
        {
            Content = "+ New Profile",
            Tag = NewProfileTag
        });

        var defaultName = _profileService.GetDefaultProfile()?.Name;
        var selectedIndex = profiles.FindIndex(p => p.Name == defaultName);
        cmbProfile.SelectedIndex = selectedIndex >= 0 ? selectedIndex : Math.Max(0, profiles.Count - 1);

        if (profiles.Count > 0 && cmbProfile.SelectedIndex >= 0 && cmbProfile.SelectedIndex < profiles.Count)
            _lastSelectedProfileName = profiles[cmbProfile.SelectedIndex].Name;

        cmbProfile.SelectionChanged += CmbProfile_SelectionChanged;
        _isUpdatingProfiles = false;
    }

    private void CmbProfile_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingProfiles) return;
        if (sender is not ComboBox cmbProfile) return;
        if (cmbProfile.SelectedItem is not ComboBoxItem item) return;

        if (item.Tag is OptiScalerProfile profile)
        {
            _lastSelectedProfileName = profile.Name;
            return;
        }

        if (item.Tag is string tag && tag == NewProfileTag)
        {
            var profiles = _profileService.GetAllProfiles();
            var fallbackName = _lastSelectedProfileName ?? _profileService.GetDefaultProfile()?.Name;
            var fallbackIndex = profiles.FindIndex(p => p.Name == fallbackName);

            _isUpdatingProfiles = true;
            cmbProfile.SelectedIndex = fallbackIndex >= 0 ? fallbackIndex : 0;
            _isUpdatingProfiles = false;

            this.Close();
            if (_ownerWindow is MainWindow mainWindow)
                mainWindow.NavigateToProfiles();
        }
    }

    /// <summary>
    /// Populates CmbExtrasVersion with available Extras versions + a "None" option.
    /// Selects the default based on GPU generation: RDNA 4 → None, others → global default or latest.
    /// </summary>
    private void PopulateExtrasComboBox()
    {
        var cmb = this.FindControl<ComboBox>("CmbExtrasVersion");
        if (cmb == null) return;

        cmb.Items.Clear();

        // Add "None" option
        var noneStack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        noneStack.Children.Add(new TextBlock { Text = "None", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        cmb.Items.Add(new ComboBoxItem { Content = noneStack, Tag = "none" });

        // Add available versions
        var versions = _componentService.ExtrasAvailableVersions;
        foreach (var ver in versions)
        {
            var isLatest = ver == _componentService.LatestExtrasVersion;
            var stack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            stack.Children.Add(new TextBlock { Text = ver, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            if (isLatest)
            {
                var badge = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#7C3AED")),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 1),
                    Margin = new Thickness(0, 0, 4, 0),
                    Child = new TextBlock { Text = "LATEST", FontSize = 10, Foreground = Brushes.White, FontWeight = FontWeight.Bold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                };
                stack.Children.Add(badge);
            }
            cmb.Items.Add(new ComboBoxItem { Content = stack, Tag = ver });
        }

        // Determine default selection
        bool isRdna4 = false;
        if (_gpuService != null)
        {
            try
            {
                var gpu = GpuSelectionHelper.GetPreferredGpu(_gpuService, _componentService.Config.DefaultGpuId);
                // RDNA 4 = Radeon RX 9000 series (GPU name contains "RX 9" or similar)
                isRdna4 = gpu != null && gpu.Vendor == GpuVendor.AMD &&
                          (gpu.Name.Contains(" 9", StringComparison.OrdinalIgnoreCase) ||
                           gpu.Name.Contains("RX 9", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) { DebugWindow.Log($"[BulkInstall] GPU detection failed: {ex.Message}"); }
        }

        // Determine target index
        int targetIndex = 0; // Default to None (index 0)
        var globalDefault = _componentService.Config.DefaultExtrasVersion;

        if (!string.IsNullOrEmpty(globalDefault))
        {
            if (globalDefault.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                targetIndex = 0;
            }
            else
            {
                // Global preference exists (e.g. "v1.0.0"), find it in items
                for (int i = 1; i < cmb.Items.Count; i++)
                {
                    var itemVer = (cmb.Items[i] as ComboBoxItem)?.Tag?.ToString();
                    if (itemVer == globalDefault)
                    {
                        targetIndex = i;
                        break;
                    }
                }

                // If not found (e.g. it was an old version), fallback logic:
                if (targetIndex == 0)
                {
                    // Applying same "intelligent" logic if user's favorite version is gone
                    if (!isRdna4 && versions.Count > 0)
                    {
                        targetIndex = 1; // latest
                    }
                }
            }
        }
        else
        {
            // No global default preference set (DefaultExtrasVersion is null/empty)
            // → Use "intelligent" logic
            if (!isRdna4 && versions.Count > 0)
            {
                targetIndex = 1; // Latest
            }
            else
            {
                targetIndex = 0; // None
            }
        }

        cmb.SelectedIndex = targetIndex;
    }

    private void PopulateOptiPatcherComboBox()
    {
        var cmb = this.FindControl<ComboBox>("CmbOptiPatcherVersion");
        if (cmb == null) return;

        cmb.Items.Clear();
        cmb.Items.Add(new ComboBoxItem { Content = "None", Tag = "none" });

        var versions = _componentService.OptiPatcherAvailableVersions;
        foreach (var ver in versions)
        {
            bool isLatest = ver == _componentService.LatestOptiPatcherVersion;
            cmb.Items.Add(BuildVersionItem(ver, isBeta: false, isLatest: isLatest));
        }

        // Respect configured default
        int targetIndex = 0;
        var savedDefault = _componentService.Config.DefaultOptiPatcherVersion;
        if (!string.IsNullOrEmpty(savedDefault) && !savedDefault.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 1; i < cmb.Items.Count; i++)
            {
                if (cmb.Items[i] is ComboBoxItem ci &&
                    string.Equals(ci.Tag?.ToString(), savedDefault, StringComparison.OrdinalIgnoreCase))
                {
                    targetIndex = i;
                    break;
                }
            }
        }

        cmb.SelectedIndex = targetIndex;
    }

    // (Replaced by unified version earlier)

    private void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            ApplyFilter(textBox.Text);
        }
    }

    private void TxtSearch_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            // Clear focus when clicking outside
            this.Focus();
        }
    }

    private void ApplyFilter(string? searchText)
    {
        _filteredGameItems.Clear();

        if (string.IsNullOrWhiteSpace(searchText))
        {
            // Show all games
            foreach (var game in _allGames)
            {
                _filteredGameItems.Add(game);
            }
        }
        else
        {
            // Filter games
            var filtered = _allGames.Where(g =>
                g.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var game in filtered)
            {
                _filteredGameItems.Add(game);
            }
        }
    }
}

public class BulkGameItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isInstalled;
    private bool _canInstall;

    public Game Game { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Platform { get; set; } = "";
    public string? CoverPath { get; set; }
    public string? OptiscalerVersion { get; set; }
    public bool IsOptiscalerInstalled { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled != value)
            {
                _isInstalled = value;
                OnPropertyChanged(nameof(IsInstalled));
            }
        }
    }

    public bool CanInstall
    {
        get => _canInstall;
        set
        {
            if (_canInstall != value)
            {
                _canInstall = value;
                OnPropertyChanged(nameof(CanInstall));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
