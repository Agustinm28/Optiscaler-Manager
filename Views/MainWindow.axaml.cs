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
using System.Threading;
using Avalonia.VisualTree;
using OptiscalerClient.Helpers;

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
        private ScrollViewer? _gameGridScrollViewer;
        private bool _isInitializingLanguage = true;
        private bool _isGridView = false;
        private DispatcherTimer? _scanDotTimer;
        private double _scanDotPhase = 0;
        private readonly Dictionary<Button, DispatcherTimer> _quickInstallDotTimers = new();
        private readonly Dictionary<Button, double> _quickInstallDotPhases = new();
        private readonly Dictionary<Button, double> _quickInstallOriginalMinWidths = new();
        private readonly CancellationTokenSource _windowLifetimeCts = new();

        private readonly GameAnalyzerService _analyzerService = new();
        private GameMetadataService _metadataService = null!;

        private ListBox? _lstGames;
        private ListBox? _lstGamesGrid;
        private TextBlock? _txtStatus;
        private Button? _btnScan;
        private Button? _btnViewList;
        private Button? _btnViewGrid;
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
            if (OperatingSystem.IsWindows())
            {
                _scannerService = new GameScannerService();
            }
            else
            {
                _scannerService = null!; // TODO: Implement Linux game scanner
            }
            _persistenceService = new GamePersistenceService();
            _componentService = new ComponentManagementService();
            _metadataService = new GameMetadataService(_componentService);
            App.ChangeLanguage(_componentService.Config.Language);
            if (OperatingSystem.IsWindows())
            {
                _gpuService = new WindowsGpuDetectionService();
            }
            else
            {
                _gpuService = null!; // TODO: Implement Linux GPU detection
            }
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
            this.Closed += MainWindow_Closed;
            
            // Restore window state
            RestoreWindowState();
            
            // Handle window state changes
            this.PropertyChanged += Window_PropertyChanged;
        }

        private void ComponentStatusChanged()
        {
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            if (!_windowLifetimeCts.IsCancellationRequested)
            {
                _windowLifetimeCts.Cancel();
            }

            _windowLifetimeCts.Dispose();
            _componentService.OnStatusChanged -= ComponentStatusChanged;
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            _gameListScrollViewer = this.FindControl<ScrollViewer>("GameListScrollViewer");
            _gameGridScrollViewer = this.FindControl<ScrollViewer>("GameGridScrollViewer");
            _lstGames = this.FindControl<ListBox>("LstGames");
            _lstGamesGrid = this.FindControl<ListBox>("LstGamesGrid");
            _txtStatus = this.FindControl<TextBlock>("TxtStatus");
            _btnScan = this.FindControl<Button>("BtnScan");
            _btnViewList = this.FindControl<Button>("BtnViewList");
            _btnViewGrid = this.FindControl<Button>("BtnViewGrid");
            _overlayScanning = this.FindControl<Grid>("OverlayScanning");
            _txtSearch = this.FindControl<TextBox>("TxtSearch");
            _txtSearchPlaceholder = this.FindControl<TextBlock>("TxtSearchPlaceholder");
            _txtGpuInfo = this.FindControl<TextBlock>("TxtGpuInfo");

            if (_lstGames != null) _lstGames.ItemsSource = _games;
            if (_lstGamesGrid != null) _lstGamesGrid.ItemsSource = _games;

            _isGridView = _componentService.Config.PreferGridView;
            ApplyGameViewMode();

            bool hadSavedGames = LoadSavedGames(_windowLifetimeCts.Token);
            _ = LoadGpuInfoAsync();
            _ = ScheduleStartupUpdatesAsync(_windowLifetimeCts.Token);
            
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

        private void RefreshGameLists()
        {
            if (_lstGames != null)
            {
                _lstGames.ItemsSource = null;
                _lstGames.ItemsSource = _games;
            }

            if (_lstGamesGrid != null)
            {
                _lstGamesGrid.ItemsSource = null;
                _lstGamesGrid.ItemsSource = _games;
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
            if (sender is ScrollViewer scrollViewer && e.Delta.Y != 0)
            {
                e.Handled = true;
                // Move manually with boost
                var currentOffset = scrollViewer.Offset;
                var newY = currentOffset.Y - (e.Delta.Y * 120); // 120 for fast and fluid
                scrollViewer.Offset = new Vector(currentOffset.X, Math.Max(0, newY));
            }
        }

        private void GameGridCard_PointerEntered(object? sender, PointerEventArgs e)
        {
            if (sender is Border card)
            {
                ToggleGridCardHover(card, true);
            }
        }

        private void GameGridCard_PointerExited(object? sender, PointerEventArgs e)
        {
            if (sender is Border card)
            {
                ToggleGridCardHover(card, false);
            }
        }

        private void ToggleGridCardHover(Border card, bool isVisible)
        {
            var overlay = card.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(x => x.Name == "GridCardHoverOverlay");

            var actions = card.GetVisualDescendants()
                .OfType<Panel>()
                .FirstOrDefault(x => x.Name == "GridCardHoverActions");

            if (overlay == null || actions == null) return;

            bool animationsEnabled = _componentService.Config.AnimationsEnabled;

            if (!animationsEnabled)
            {
                overlay.IsVisible = isVisible;
                actions.IsVisible = isVisible;
                overlay.Opacity = isVisible ? 1 : 0;
                actions.Opacity = isVisible ? 1 : 0;
                actions.IsHitTestVisible = isVisible;
                return;
            }

            EnsureHoverOpacityTransition(overlay);
            EnsureHoverOpacityTransition(actions);

            overlay.IsVisible = true;
            actions.IsVisible = true;
            actions.IsHitTestVisible = isVisible;
            overlay.Opacity = isVisible ? 1 : 0;
            actions.Opacity = isVisible ? 1 : 0;

            if (!isVisible)
            {
                _ = HideGridCardHoverAfterFadeAsync(overlay, actions);
            }
        }

        private static void EnsureHoverOpacityTransition(Visual visual)
        {
            if (visual.Transitions == null)
            {
                visual.Transitions = new Avalonia.Animation.Transitions();
            }

            if (!visual.Transitions.OfType<Avalonia.Animation.DoubleTransition>()
                .Any(t => t.Property == Visual.OpacityProperty))
            {
                visual.Transitions.Add(new Avalonia.Animation.DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(150),
                    Easing = new Avalonia.Animation.Easings.CubicEaseOut()
                });
            }
        }

        private static async Task HideGridCardHoverAfterFadeAsync(Border overlay, Panel actions)
        {
            await Task.Delay(170);

            if (overlay.Opacity <= 0.01)
            {
                overlay.IsVisible = false;
            }

            if (actions.Opacity <= 0.01)
            {
                actions.IsVisible = false;
                actions.IsHitTestVisible = false;
            }
        }

        private void BtnViewList_Click(object sender, RoutedEventArgs e)
        {
            _isGridView = false;
            _componentService.Config.PreferGridView = false;
            _componentService.SaveConfiguration();
            ApplyGameViewMode();
        }

        private void BtnViewGrid_Click(object sender, RoutedEventArgs e)
        {
            _isGridView = true;
            _componentService.Config.PreferGridView = true;
            _componentService.SaveConfiguration();
            ApplyGameViewMode();
        }

        private void ApplyGameViewMode()
        {
            if (_gameListScrollViewer != null)
            {
                _gameListScrollViewer.IsVisible = !_isGridView;
                _gameListScrollViewer.IsHitTestVisible = !_isGridView;
            }

            if (_gameGridScrollViewer != null)
            {
                _gameGridScrollViewer.IsVisible = _isGridView;
                _gameGridScrollViewer.IsHitTestVisible = _isGridView;
            }

            var activeBg = this.FindResource("BrBgCard") as IBrush ?? Brushes.DimGray;
            var inactiveBg = Brushes.Transparent;
            var activeFg = this.FindResource("BrTextPrimary") as IBrush ?? Brushes.White;
            var inactiveFg = this.FindResource("BrTextSecondary") as IBrush ?? Brushes.Gray;

            if (_btnViewList != null)
            {
                _btnViewList.Background = _isGridView ? inactiveBg : activeBg;
                _btnViewList.Foreground = _isGridView ? inactiveFg : activeFg;
            }

            if (_btnViewGrid != null)
            {
                _btnViewGrid.Background = _isGridView ? activeBg : inactiveBg;
                _btnViewGrid.Foreground = _isGridView ? activeFg : inactiveFg;
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
            var txtSteamGridApiKey = this.FindControl<TextBox>("TxtSteamGridApiKey");
            if (txtSteamGridApiKey != null)
            {
                txtSteamGridApiKey.Text = _componentService.Config.SteamGridDBApiKey ?? string.Empty;
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

            PopulateDefaultGpuComboBox();
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

        private void PopulateDefaultGpuComboBox()
        {
            var cmb = this.FindControl<ComboBox>("CmbDefaultGpu");
            if (cmb == null) return;

            _isInitializingLanguage = true;
            cmb.Items.Clear();

            var autoItem = new ComboBoxItem { Content = "Auto (Recommended)", Tag = "auto" };
            cmb.Items.Add(autoItem);

            if (OperatingSystem.IsWindows() && _gpuService != null)
            {
                var gpus = _gpuService.DetectGPUs();
                foreach (var gpu in gpus)
                {
                    var label = $"{gpu.Vendor} - {gpu.Name}";
                    var id = GpuSelectionHelper.BuildGpuId(gpu);
                    cmb.Items.Add(new ComboBoxItem { Content = label, Tag = id });
                }
            }

            var saved = _componentService.Config.DefaultGpuId;
            cmb.SelectedIndex = 0;
            if (!string.IsNullOrEmpty(saved))
            {
                for (int i = 1; i < cmb.Items.Count; i++)
                {
                    if ((cmb.Items[i] as ComboBoxItem)?.Tag?.ToString() == saved)
                    {
                        cmb.SelectedIndex = i;
                        break;
                    }
                }
            }

            _isInitializingLanguage = false;
        }

        private void CmbDefaultGpu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingLanguage) return;
            if (sender is ComboBox cmb && cmb.SelectedItem is ComboBoxItem item)
            {
                var id = item.Tag?.ToString();
                _componentService.Config.DefaultGpuId = (id == "auto") ? null : id;
                _componentService.SaveConfiguration();
                _lastDetectedGpu = null;
                _ = LoadGpuInfoAsync();
            }
        }

        private void TxtSteamGridApiKey_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            _componentService.Config.SteamGridDBApiKey = (textBox.Text ?? string.Empty).Trim();
            _componentService.SaveConfiguration();
        }

        private async void BtnSteamGridApiGuide_Click(object sender, RoutedEventArgs e)
        {
            var guideWindow = new SteamGridApiGuideWindow(this);
            await guideWindow.ShowDialog(this);
        }

        private void SettingsBackground_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Source is not Visual visual)
                return;

            if (visual.FindAncestorOfType<TextBox>() != null)
                return;
            if (visual.FindAncestorOfType<Button>() != null)
                return;
            if (visual.FindAncestorOfType<ComboBox>() != null)
                return;
            if (visual.FindAncestorOfType<ToggleSwitch>() != null)
                return;

            var focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
            focusManager?.ClearFocus();
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

        private async Task ScheduleStartupUpdatesAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                await CheckUpdatesOnStartupAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        private async Task CheckUpdatesOnStartupAsync(CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_txtStatus != null) _txtStatus.Text = GetResourceString("TxtCheckingUpdates", "Checking for updates...");
                await _componentService.CheckForUpdatesAsync();
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                ComponentStatusChanged();
                if (!cancellationToken.IsCancellationRequested && _txtStatus != null)
                    _txtStatus.Text = GetResourceString("TxtReady", "Ready");
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

        private bool LoadSavedGames(CancellationToken cancellationToken)
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
                    using var coverSemaphore = new SemaphoreSlim(2, 2);
                    var coverTasks = new List<Task>();
                    var analyzedCount = 0;

                    foreach (var game in savedGames)
                    {
                        if (cancellationToken.IsCancellationRequested) return;

                        try { _analyzerService.AnalyzeGame(game); }
                        catch { }

                        if (string.IsNullOrEmpty(game.CoverImageUrl) || game.CoverImageUrl.StartsWith("http"))
                        {
                            var appIdKey = !string.IsNullOrEmpty(game.AppId) ? game.AppId :
                                         !string.IsNullOrEmpty(game.Name) ? game.Name : Guid.NewGuid().ToString();

                            await coverSemaphore.WaitAsync(cancellationToken);
                            coverTasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    game.CoverImageUrl = await _metadataService.FetchAndCacheCoverImageAsync(game.Name, appIdKey);
                                }
                                catch
                                {
                                }
                                finally
                                {
                                    coverSemaphore.Release();
                                }
                            }, cancellationToken));
                        }

                        analyzedCount++;
                        if (analyzedCount % 4 == 0)
                        {
                            await Task.Delay(1, cancellationToken);
                        }
                    }

                    try
                    {
                        await Task.WhenAll(coverTasks);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (cancellationToken.IsCancellationRequested) return;

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (cancellationToken.IsCancellationRequested) return;

                        RefreshGameLists();
                        _persistenceService.SaveGames(savedGames);
                    });
                }, cancellationToken);
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
                if (OperatingSystem.IsWindows() && _scannerService != null)
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

                    RefreshGameLists();

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
            RefreshGameLists();
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

                RefreshGameLists();
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
                button.Content = GetResourceString("TxtQuickUninstall", "🗑️ Quick Uninstall");
                button.Foreground = this.FindResource("BrAccentWarm") as IBrush ?? Brushes.Orange;
            }
            else
            {
                button.Content = GetResourceString("TxtQuickInstall", "✦ Quick Install");
                button.Foreground = this.FindResource("BrAccent") as IBrush ?? Brushes.Purple;
            }
        }

        private void SetQuickInstallLoading(Button button)
        {
            bool isGridButton = string.Equals(button.Name, "BtnFastInstallGrid", StringComparison.Ordinal);

            if (!_quickInstallOriginalMinWidths.ContainsKey(button))
            {
                _quickInstallOriginalMinWidths[button] = button.MinWidth;
            }

            var minWidth = button.Bounds.Width;
            if (minWidth <= 0) minWidth = isGridButton ? 128 : 140;
            button.MinWidth = Math.Max(button.MinWidth, minWidth);

            var spinner = new ProgressBar
            {
                IsIndeterminate = true,
                Width = 26,
                Height = 6,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Background = Brushes.Transparent
            };

            var dot1 = new Ellipse { Width = 5, Height = 5, Fill = this.FindResource("BrAccent") as IBrush ?? Brushes.Purple, Margin = new Thickness(2, 0) };
            var dot2 = new Ellipse { Width = 5, Height = 5, Fill = this.FindResource("BrAccent") as IBrush ?? Brushes.Purple, Margin = new Thickness(2, 0) };
            var dot3 = new Ellipse { Width = 5, Height = 5, Fill = this.FindResource("BrAccent") as IBrush ?? Brushes.Purple, Margin = new Thickness(2, 0) };

            var t1 = new Avalonia.Media.TranslateTransform();
            var t2 = new Avalonia.Media.TranslateTransform();
            var t3 = new Avalonia.Media.TranslateTransform();
            dot1.RenderTransform = t1;
            dot2.RenderTransform = t2;
            dot3.RenderTransform = t3;

            var dots = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            dots.Children.Add(dot1);
            dots.Children.Add(dot2);
            dots.Children.Add(dot3);

            var stack = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = isGridButton ? 6 : 8,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            if (isGridButton)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "✦",
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Foreground = this.FindResource("BrAccent") as IBrush ?? Brushes.Purple
                });
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "✦",
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Foreground = this.FindResource("BrAccent") as IBrush ?? Brushes.Purple
                });
            }
            stack.Children.Add(dots);

            button.Content = stack;
            button.IsEnabled = false;
            button.Foreground = this.FindResource("BrTextSecondary") as IBrush ?? Brushes.Gray;

            if (_quickInstallDotTimers.TryGetValue(button, out var existing))
            {
                existing.Stop();
            }

            _quickInstallDotPhases[button] = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += (s, e) =>
            {
                if (!_quickInstallDotPhases.ContainsKey(button)) return;
                _quickInstallDotPhases[button] += 0.25;
                var phase = _quickInstallDotPhases[button];
                const double amplitude = 6;
                const double phaseOffset = Math.PI * 2 / 3;
                t1.Y = -amplitude * Math.Max(0, Math.Sin(phase));
                t2.Y = -amplitude * Math.Max(0, Math.Sin(phase + phaseOffset));
                t3.Y = -amplitude * Math.Max(0, Math.Sin(phase + phaseOffset * 2));
            };
            _quickInstallDotTimers[button] = timer;
            timer.Start();
        }

        private void ClearQuickInstallLoading(Button button, Game game)
        {
            button.IsEnabled = true;
            UpdateFastInstallButton(button, game);

            if (_quickInstallDotTimers.TryGetValue(button, out var timer))
            {
                timer.Stop();
                _quickInstallDotTimers.Remove(button);
            }
            _quickInstallDotPhases.Remove(button);

            if (_quickInstallOriginalMinWidths.TryGetValue(button, out var originalMinWidth))
            {
                button.MinWidth = originalMinWidth;
                _quickInstallOriginalMinWidths.Remove(button);
            }
        }

        private async void BtnFastInstall_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Game selectedGame)
            {
                try
                {
                    // Always refresh install state from disk before deciding install/uninstall path.
                    GameAnalyzerService.InvalidateCacheForPath(selectedGame.InstallPath);
                    _analyzerService.AnalyzeGame(selectedGame, forceRefresh: true);

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
                        RefreshGameLists();
                        
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

                        if (ComponentManagementService.IsOptiScalerDownloadActive(versionToInstall))
                        {
                            ShowSecondaryToast($"Ya hay una descarga en curso para v{versionToInstall}.");
                            return;
                        }

                        // Get cache paths
                        var optiCacheDir = _componentService.GetOptiScalerCachePath(versionToInstall);

                        // Download OptiScaler if not in cache
                        if (!Directory.Exists(optiCacheDir) || Directory.GetFiles(optiCacheDir, "*.*", SearchOption.AllDirectories).Length == 0)
                        {
                            SetQuickInstallLoading(button);
                            ShowToast($"Descargando OptiScaler {versionToInstall}... 0%", showProgress: true, progressPercent: 0);

                            try
                            {
                                var progress = new Progress<double>(p =>
                                {
                                    UpdateToastProgress($"Descargando OptiScaler {versionToInstall}... {(int)p}%", p);
                                });

                                await _componentService.DownloadOptiScalerAsync(versionToInstall, progress);
                                ShowToast($"Instalando OptiScaler {versionToInstall}...", showProgress: true, progressPercent: null);
                            }
                            catch (Exception downloadEx)
                            {
                                if (downloadEx is VersionUnavailableException vex &&
                                    vex.Message.Contains("Download already in progress", StringComparison.OrdinalIgnoreCase))
                                {
                                    ShowSecondaryToast($"Ya hay una descarga en curso para v{vex.Version}.");
                                    return;
                                }

                                HideToast();
                                
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
                        SetQuickInstallLoading(button);
                        await Task.Run(() =>
                        {
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
                        });
                        
                        // Update game status
                        selectedGame.IsOptiscalerInstalled = true;
                        selectedGame.OptiscalerVersion = versionToInstall;
                        
                        // Refresh UI
                        RefreshGameLists();
                        
                        _persistenceService.SaveGames(_games);
                        await HideToastAfterAsync(1200);
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
                finally
                {
                    ClearQuickInstallLoading(button, selectedGame);
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
                        if (OperatingSystem.IsWindows() && _gpuService != null)
                        {
                            try
                            {
                                return GpuSelectionHelper.GetPreferredGpu(_gpuService, _componentService.Config.DefaultGpuId);
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

        private void ShowToast(string message, bool showProgress = false, double? progressPercent = null)
        {
            var txtToastMessage = this.FindControl<TextBlock>("TxtToastMessage");
            var bdToast = this.FindControl<Border>("BdToast");
            var prgToast = this.FindControl<ProgressBar>("PrgToast");

            Dispatcher.UIThread.Post(() =>
            {
                if (txtToastMessage != null) txtToastMessage.Text = message;
                if (bdToast != null) bdToast.IsVisible = true;
                if (prgToast != null)
                {
                    prgToast.IsVisible = showProgress;
                    prgToast.IsIndeterminate = !progressPercent.HasValue;
                    if (progressPercent.HasValue) prgToast.Value = progressPercent.Value;
                }
            });
        }

        private void UpdateToastProgress(string message, double progressPercent)
        {
            ShowToast(message, showProgress: true, progressPercent: progressPercent);
        }

        private void HideToast()
        {
            var bdToast = this.FindControl<Border>("BdToast");
            var prgToast = this.FindControl<ProgressBar>("PrgToast");

            Dispatcher.UIThread.Post(() =>
            {
                if (bdToast != null) bdToast.IsVisible = false;
                if (prgToast != null) prgToast.IsVisible = false;
            });
        }

        private async Task HideToastAfterAsync(int delayMs)
        {
            await Task.Delay(delayMs);
            HideToast();
        }

        private void ShowSecondaryToast(string message)
        {
            var txtToast = this.FindControl<TextBlock>("TxtToastSecondaryMessage");
            var bdToast = this.FindControl<Border>("BdToastSecondary");

            Dispatcher.UIThread.Post(() =>
            {
                if (txtToast != null) txtToast.Text = message;
                if (bdToast != null) bdToast.IsVisible = true;
            });

            _ = HideSecondaryToastAfterAsync(1500);
        }

        private async Task HideSecondaryToastAfterAsync(int delayMs)
        {
            await Task.Delay(delayMs);
            var bdToast = this.FindControl<Border>("BdToastSecondary");
            Dispatcher.UIThread.Post(() =>
            {
                if (bdToast != null) bdToast.IsVisible = false;
            });
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

