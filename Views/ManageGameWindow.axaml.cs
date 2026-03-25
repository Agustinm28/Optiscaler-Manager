using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using OptiscalerClient.Models;
using System.Collections.ObjectModel;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using OptiscalerClient.Services;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using OptiscalerClient.Helpers;

namespace OptiscalerClient.Views
{
    public partial class ManageGameWindow : Window
    {
        private readonly Game _game;
        private readonly IGpuDetectionService _gpuService;
        private HashSet<string> _betaVersions = new();

        public bool NeedsScan { get; private set; }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // Avalonia requires an empty parameterless constructor for XAML initialization
        public ManageGameWindow()
        {
            InitializeComponent();
            _game = null!;
            _gpuService = null!;
        }

        public ManageGameWindow(Window owner, Game game)
        {
            InitializeComponent();
            _game = game;

            // Frameless centering logic
            this.Opacity = 0;
            if (owner != null)
            {
                var scaling = owner.DesktopScaling;
                double dialogW = 960 * scaling;
                double dialogH = 540 * scaling;

                var x = owner.Position.X + (owner.Bounds.Width * scaling - dialogW) / 2;
                var y = owner.Position.Y + (owner.Bounds.Height * scaling - dialogH) / 2;
                
                this.Position = new PixelPoint((int)Math.Max(0, x), (int)Math.Max(0, y));
            }

            if (OperatingSystem.IsWindows())
            {
                _gpuService = new WindowsGpuDetectionService();
            }
            else
            {
                _gpuService = null!;
            }

            SetupUI();
            
            // Re-bind TitleBar dragging and Close button
            var titleBar = this.FindControl<Border>("TitleBar");
            if (titleBar != null)
            {
                titleBar.PointerPressed += (s, e) => this.BeginMoveDrag(e);
            }

            this.Opened += (s, e) =>
            {
                this.Opacity = 1;
                var rootPanel = this.FindControl<Panel>("RootPanel");
                if (rootPanel != null)
                {
                    AnimationHelper.SetupPanelTransition(rootPanel);
                    rootPanel.Opacity = 1;
                }
            };

            _ = LoadVersionsAsync();
        }

        private static ComboBoxItem BuildVersionItem(string ver, bool isBeta, bool isLatest)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = ver, VerticalAlignment = VerticalAlignment.Center });

            if (isBeta)
            {
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.Parse("#D4A017")),
                    Padding = new Thickness(5, 1),
                    Child = new TextBlock { Text = "BETA", FontSize = 10, Foreground = Brushes.White, FontWeight = Avalonia.Media.FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center }
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
                    Child = new TextBlock { Text = "LATEST", FontSize = 10, Foreground = Brushes.White, FontWeight = Avalonia.Media.FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center }
                };
                stack.Children.Add(badge);
            }

            return new ComboBoxItem { Content = stack, Tag = ver };
        }

        private async Task LoadVersionsAsync()
        {
            var componentService = new ComponentManagementService();
            if (componentService.OptiScalerAvailableVersions.Count == 0)
            {
                await componentService.CheckForUpdatesAsync();
            }

            Dispatcher.UIThread.Post(() =>
            {
                var allVersions = componentService.OptiScalerAvailableVersions;
                var betaVersions = componentService.BetaVersions;
                var latestBeta = componentService.LatestBetaVersion;
                var showBetaVersions = componentService.Config.ShowBetaVersions;

                var cmbOptiVersion = this.FindControl<ComboBox>("CmbOptiVersion");
                if (cmbOptiVersion == null) return;

                cmbOptiVersion.Items.Clear();

                if (allVersions.Count == 0)
                {
                    cmbOptiVersion.Items.Add(GetResourceString("TxtNoOptiDetected", "No version detected"));
                    cmbOptiVersion.SelectedIndex = 0;
                    cmbOptiVersion.IsEnabled = false;
                    return;
                }

                _betaVersions = betaVersions;

                var stableVersions = allVersions.Where(v => !betaVersions.Contains(v)).ToList();
                var otherBetas = allVersions.Where(v => betaVersions.Contains(v) && v != latestBeta).ToList();

                int selectedIndex = 0;
                int currentIndex = 0;

                // 1. Latest beta at top (if present)
                if (!string.IsNullOrEmpty(latestBeta))
                {
                    cmbOptiVersion.Items.Add(BuildVersionItem(latestBeta, isBeta: true, isLatest: false));
                    currentIndex++;
                }

                // 2. Stable versions — default selection based on user preference
                bool isLatestStableMarked = false;
                foreach (var ver in stableVersions)
                {
                    bool isFirstStable = !isLatestStableMarked && !ver.Contains("nightly", StringComparison.OrdinalIgnoreCase);
                    
                    // Mark as latest stable if this is the first stable version
                    if (isFirstStable)
                    {
                        isLatestStableMarked = true;
                    }
                    
                    // Select default version based on user preference
                    if (showBetaVersions && !string.IsNullOrEmpty(latestBeta))
                    {
                        // User prefers latest beta - select the latest beta (index 0)
                        selectedIndex = 0;
                    }
                    else if (isFirstStable)
                    {
                        // User prefers stable - select the first stable version
                        selectedIndex = currentIndex;
                    }
                    
                    cmbOptiVersion.Items.Add(BuildVersionItem(ver, isBeta: false, isLatest: isFirstStable));
                    currentIndex++;
                }

                // 3. Remaining betas at end
                foreach (var ver in otherBetas)
                {
                    cmbOptiVersion.Items.Add(BuildVersionItem(ver, isBeta: true, isLatest: false));
                    currentIndex++;
                }

                cmbOptiVersion.SelectedIndex = selectedIndex;

                // Wire SelectionChanged here so it only fires on user interaction, not during init
                cmbOptiVersion.SelectionChanged += CmbOptiVersion_SelectionChanged;
            });
        }

        private void SetupUI()
        {
            var txtGameName = this.FindControl<TextBlock>("TxtGameName");
            var txtInstallPath = this.FindControl<TextBlock>("TxtInstallPath");
            
            if (txtGameName != null) txtGameName.Text = _game.Name;
            if (txtInstallPath != null) txtInstallPath.Text = _game.InstallPath;

            UpdateStatus();
            LoadComponents();
            ConfigureAdditionalComponents();
        }

        private bool _isAnimatingClose = false;

        private void BtnClose_Click(object sender, RoutedEventArgs e) => _ = CloseAnimated();

        private async Task CloseAnimated()
        {
            if (_isAnimatingClose) return;
            _isAnimatingClose = true;
            var rootPanel = this.FindControl<Panel>("RootPanel");
            if (rootPanel != null) rootPanel.Opacity = 0;
            await Task.Delay(220);
            this.Close();
        }
        
        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? dirToOpen = null;
                var installService = new GameInstallationService();
                var determinedDir = installService.DetermineInstallDirectory(_game);

                if (!string.IsNullOrEmpty(determinedDir) && Directory.Exists(determinedDir))
                    dirToOpen = determinedDir;
                else if (!string.IsNullOrEmpty(_game.InstallPath) && Directory.Exists(_game.InstallPath))
                    dirToOpen = _game.InstallPath;
                else if (!string.IsNullOrEmpty(_game.ExecutablePath))
                    dirToOpen = System.IO.Path.GetDirectoryName(_game.ExecutablePath);

                if (string.IsNullOrEmpty(dirToOpen) || !Directory.Exists(dirToOpen))
                {
                    _ = new ConfirmDialog(this, "Error", "The installation directory could not be found.").ShowDialog<object>(this);
                    return;
                }

                // Robust way on Windows
                Process.Start("explorer.exe", $"\"{dirToOpen}\"");
            }
            catch (Exception ex)
            {
                _ = new ConfirmDialog(this, "Error", $"Could not open folder:\n{ex.Message}").ShowDialog<object>(this);
            }
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteInstallAsync(false);
        }

        private async void BtnInstallManual_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteInstallAsync(true);
        }

        private async Task ExecuteInstallAsync(bool isManualMode)
        {
            var btnInstall = this.FindControl<Button>("BtnInstall");
            var btnInstallManual = this.FindControl<Button>("BtnInstallManual");
            var btnUninstall = this.FindControl<Button>("BtnUninstall");
            var cmbOptiVersion = this.FindControl<ComboBox>("CmbOptiVersion");
            var bdProgress = this.FindControl<Border>("BdProgress");
            var prgDownload = this.FindControl<ProgressBar>("PrgDownload");
            var txtProgressState = this.FindControl<TextBlock>("TxtProgressState");
            var cmbInjectionMethod = this.FindControl<ComboBox>("CmbInjectionMethod");
            var chkInstallFakenvapi = this.FindControl<CheckBox>("ChkInstallFakenvapi");
            var chkInstallNukemFG = this.FindControl<CheckBox>("ChkInstallNukemFG");

            try
            {
                var componentService = new ComponentManagementService();
                var installService = new GameInstallationService();

                var selectedVersionItem = cmbOptiVersion?.SelectedItem as ComboBoxItem;
                var optiscalerVersion = selectedVersionItem?.Tag?.ToString();

                if (string.IsNullOrEmpty(optiscalerVersion))
                {
                    await new ConfirmDialog(this, "Error", "No OptiScaler version selected.").ShowDialog<object>(this);
                    return;
                }

                string? overrideGameDir = null;
                if (isManualMode)
                {
                    var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
                    {
                        Title = "Select Game Executable (Main .exe)",
                        AllowMultiple = false,
                        FileTypeFilter = new[] 
                        { 
                            new FilePickerFileType("Executable Files (*.exe)") 
                            { 
                                Patterns = new[] { "*.exe" } 
                            },
                            new FilePickerFileType("All files")
                            {
                                Patterns = new[] { "*.*" }
                            }
                        }
                    });

                    if (files == null || !files.Any()) return; // User cancelled
                    overrideGameDir = System.IO.Path.GetDirectoryName(files[0].Path.LocalPath);
                }

                if (btnInstall != null) btnInstall.IsEnabled = false;
                if (btnInstallManual != null) btnInstallManual.IsEnabled = false;
                if (btnUninstall != null) btnUninstall.IsEnabled = false;
                if (cmbOptiVersion != null) cmbOptiVersion.IsEnabled = false;

                bool isDownloadingOpti = true;
                var progress = new Progress<double>(p =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!isDownloadingOpti) return;

                        if (bdProgress != null && bdProgress.IsVisible != true)
                            bdProgress.IsVisible = true;

                        if (prgDownload != null) prgDownload.Value = p;
                        var formatInstalling = GetResourceString("TxtInstallingFormat", "Downloading OptiScaler v{0}... {1}%");
                        if (txtProgressState != null) txtProgressState.Text = string.Format(formatInstalling, optiscalerVersion, (int)p);
                    });
                });

                string optiCacheDir;
                try
                {
                    optiCacheDir = await componentService.DownloadOptiScalerAsync(optiscalerVersion, progress);
                    isDownloadingOpti = false;
                    
                    // Hide after download finishes
                    Dispatcher.UIThread.Post(() => {
                        if (bdProgress != null) bdProgress.IsVisible = false;
                    });
                }
                catch (Exception ex)
                {
                    isDownloadingOpti = false;
                    Dispatcher.UIThread.Post(() => {
                        if (bdProgress != null) bdProgress.IsVisible = false;
                    });
                    var msgFormat = GetResourceString("TxtDownloadErrorPrefix", "Failed to download OptiScaler: {0}");
                    var title = GetResourceString("TxtError", "Error");
                    await new ConfirmDialog(this, title, string.Format(msgFormat, ex.Message)).ShowDialog<object>(this);
                    return;
                }
                finally
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (btnInstall != null) btnInstall.IsEnabled = true;
                        if (btnInstallManual != null) btnInstallManual.IsEnabled = true;
                        if (btnUninstall != null) btnUninstall.IsEnabled = true;
                        if (cmbOptiVersion != null) cmbOptiVersion.IsEnabled = true;
                    });
                }

                var fakeCacheDir = componentService.GetFakenvapiCachePath();
                var nukemCacheDir = componentService.GetNukemFGCachePath();

                var selectedItem = cmbInjectionMethod?.SelectedItem as ComboBoxItem;
                var injectionMethod = selectedItem?.Tag?.ToString() ?? "dxgi.dll";

                bool installFakenvapi = chkInstallFakenvapi?.IsChecked == true;
                bool installNukemFG = chkInstallNukemFG?.IsChecked == true;

                if (installFakenvapi && (!Directory.Exists(fakeCacheDir) || Directory.GetFiles(fakeCacheDir).Length == 0))
                {
                    try
                    {
                        await componentService.CheckForUpdatesAsync();

                        Dispatcher.UIThread.Post(() =>
                        {
                            if (btnInstall != null) btnInstall.IsEnabled = false;
                            if (btnInstallManual != null) btnInstallManual.IsEnabled = false;
                            if (btnUninstall != null) btnUninstall.IsEnabled = false;
                            if (cmbOptiVersion != null) cmbOptiVersion.IsEnabled = false;
                            if (bdProgress != null) bdProgress.IsVisible = true;
                            if (txtProgressState != null) txtProgressState.Text = "Downloading Fakenvapi...";
                            if (prgDownload != null) prgDownload.IsIndeterminate = true;
                        });

                        await componentService.DownloadAndExtractFakenvapiAsync();
                    }
                    catch (Exception ex)
                    {
                        await new ConfirmDialog(this, "Error", $"Failed to download Fakenvapi: {ex.Message}").ShowDialog<object>(this);
                        return;
                    }
                    finally
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (prgDownload != null) prgDownload.IsIndeterminate = false;
                            if (bdProgress != null) bdProgress.IsVisible = false;
                            if (btnInstall != null) btnInstall.IsEnabled = true;
                            if (btnInstallManual != null) btnInstallManual.IsEnabled = true;
                            if (btnUninstall != null) btnUninstall.IsEnabled = true;
                            if (cmbOptiVersion != null) cmbOptiVersion.IsEnabled = true;
                        });
                    }
                }

                if (installNukemFG && (!Directory.Exists(nukemCacheDir) || Directory.GetFiles(nukemCacheDir).Length == 0))
                {
                    bool provided = await componentService.ProvideNukemFGManuallyAsync(isUpdate: false);
                    if (!provided || !Directory.Exists(nukemCacheDir) || Directory.GetFiles(nukemCacheDir).Length == 0)
                    {
                        return; 
                    }
                }

                // Show extraction status
                Dispatcher.UIThread.Post(() =>
                {
                    if (bdProgress != null) bdProgress.IsVisible = true;
                    if (txtProgressState != null)
                    {
                        var extractFormat = GetResourceString("TxtExtractingFormat", "Extracting and installing v{0}...");
                        txtProgressState.Text = string.Format(extractFormat, optiscalerVersion);
                    }
                    if (prgDownload != null) prgDownload.IsIndeterminate = true;
                });

                await Task.Run(() => {
                    installService.InstallOptiScaler(_game, optiCacheDir, injectionMethod,
                                                    installFakenvapi, fakeCacheDir,
                                                    installNukemFG, nukemCacheDir,
                                                    optiscalerVersion: optiscalerVersion,
                                                    overrideGameDir: overrideGameDir);
                });

                var installedComponents = "OptiScaler";
                if (installFakenvapi) installedComponents += " + Fakenvapi";
                if (installNukemFG) installedComponents += " + NukemFG";

                NeedsScan = true;
                UpdateStatus();
                LoadComponents();

                // Explicitly hide progress
                Dispatcher.UIThread.Post(() =>
                {
                    if (bdProgress != null) bdProgress.IsVisible = false;
                });

                var successFormat = GetResourceString("TxtInstallSuccessFormat", "{0} installed successfully!");
                await ShowToastAsync(string.Format(successFormat, installedComponents));
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (bdProgress != null) bdProgress.IsVisible = false;
                });
                await new ConfirmDialog(this, "Error", $"Installation failed: {ex.Message}").ShowDialog<object>(this);
            }
        }

        private void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            var bdConfirmUninstall = this.FindControl<Grid>("BdConfirmUninstall");
            if (bdConfirmUninstall != null) bdConfirmUninstall.IsVisible = true;
            
            var btnInstall = this.FindControl<Button>("BtnInstall");
            var btnInstallManual = this.FindControl<Button>("BtnInstallManual");
            var btnUninstall = this.FindControl<Button>("BtnUninstall");
            
            if (btnInstall != null) btnInstall.IsEnabled = false;
            if (btnInstallManual != null) btnInstallManual.IsEnabled = false;
            if (btnUninstall != null) btnUninstall.IsEnabled = false;
        }

        private void BtnConfirmUninstallNo_Click(object sender, RoutedEventArgs e)
        {
            var bdConfirmUninstall = this.FindControl<Grid>("BdConfirmUninstall");
            if (bdConfirmUninstall != null) bdConfirmUninstall.IsVisible = false;
            
            var btnInstall = this.FindControl<Button>("BtnInstall");
            var btnInstallManual = this.FindControl<Button>("BtnInstallManual");
            var btnUninstall = this.FindControl<Button>("BtnUninstall");
            
            if (btnInstall != null) btnInstall.IsEnabled = true;
            if (btnInstallManual != null) btnInstallManual.IsEnabled = true;
            if (btnUninstall != null) btnUninstall.IsEnabled = true;
        }

        private async void BtnConfirmUninstallYes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var bdConfirmUninstall = this.FindControl<Grid>("BdConfirmUninstall");
                if (bdConfirmUninstall != null) bdConfirmUninstall.IsVisible = false;
            
                var btnInstall = this.FindControl<Button>("BtnInstall");
                var btnInstallManual = this.FindControl<Button>("BtnInstallManual");
                var btnUninstall = this.FindControl<Button>("BtnUninstall");
            
                if (btnInstall != null) btnInstall.IsEnabled = true;
                if (btnInstallManual != null) btnInstallManual.IsEnabled = true;
                if (btnUninstall != null) btnUninstall.IsEnabled = true;

                var installService = new GameInstallationService();
                installService.UninstallOptiScaler(_game);

                NeedsScan = true;
                UpdateStatus();
                LoadComponents();

                var successMsg = GetResourceString("TxtOptiUninstallSuccess", "OptiScaler uninstalled successfully.");
                await ShowToastAsync(successMsg);
            }
            catch (Exception ex)
            {
                var failFormat = GetResourceString("TxtOptiUninstallFail", "Uninstall failed: {0}");
                var titleMsg = GetResourceString("TxtError", "Error");
                await new ConfirmDialog(this, titleMsg, string.Format(failFormat, ex.Message)).ShowDialog<object>(this);
            }
        }

        private async Task ShowToastAsync(string message)
        {
            var txtToastMessage = this.FindControl<TextBlock>("TxtToastMessage");
            var bdToast = this.FindControl<Border>("BdToast");

            Dispatcher.UIThread.Post(() =>
            {
                if (txtToastMessage != null) txtToastMessage.Text = message;
                if (bdToast != null) bdToast.IsVisible = true;
            });

            await Task.Delay(3500);

            Dispatcher.UIThread.Post(() =>
            {
                if (bdToast != null) bdToast.IsVisible = false;
            });
        }

        private void UpdateStatus()
        {
            var txtStatus = this.FindControl<TextBlock>("TxtStatus");
            var statusIndicator = this.FindControl<Ellipse>("StatusIndicator");
            var txtVersion = this.FindControl<TextBlock>("TxtVersion");
            
            var btnInstall = this.FindControl<Button>("BtnInstall");
            var btnInstallManual = this.FindControl<Button>("BtnInstallManual");
            var btnUninstall = this.FindControl<Button>("BtnUninstall");
            var installBtnGroup = this.FindControl<StackPanel>("InstallBtnGroup");
            var pnlInstallOptions = this.FindControl<StackPanel>("PnlInstallOptions");

            if (_game.IsOptiscalerInstalled)
            {
                if (txtStatus != null) txtStatus.Text = GetResourceString("TxtOptiInstalled", "OptiScaler Installed");
                if (statusIndicator != null) statusIndicator.Fill = new SolidColorBrush(Color.FromRgb(118, 185, 0)); 

                if (txtVersion != null)
                {
                    if (!string.IsNullOrEmpty(_game.OptiscalerVersion))
                        txtVersion.Text = $"v{_game.OptiscalerVersion}";
                    else
                        txtVersion.Text = "";
                }

                if (btnInstall != null)
                {
                    btnInstall.IsVisible = true;
                    btnInstall.Content = GetResourceString("TxtUpdateOpti", "Update / Reinstall");
                }
                if (btnInstallManual != null)
                {
                    btnInstallManual.IsVisible = true;
                    btnInstallManual.Content = GetResourceString("TxtUpdateOptiManual", "Manual Update");
                }
                
                if (installBtnGroup != null) installBtnGroup.IsVisible = true;
                if (pnlInstallOptions != null) pnlInstallOptions.IsVisible = true;
                if (btnUninstall != null) btnUninstall.IsVisible = true;
            }
            else
            {
                if (txtStatus != null) txtStatus.Text = GetResourceString("TxtOptiNotInstalled", "Not Installed");
                if (statusIndicator != null) statusIndicator.Fill = new SolidColorBrush(Colors.Gray);
                if (txtVersion != null) txtVersion.Text = "";

                if (btnInstall != null)
                {
                    btnInstall.IsVisible = true;
                    btnInstall.Content = GetResourceString("TxtInstallOpti", "✦ Auto Install");
                }
                if (btnInstallManual != null)
                {
                    btnInstallManual.IsVisible = true;
                    btnInstallManual.Content = GetResourceString("TxtBtnManualInstall", "✦ Manual Install");
                }
                
                if (installBtnGroup != null) installBtnGroup.IsVisible = true;
                if (pnlInstallOptions != null) pnlInstallOptions.IsVisible = true;
                if (btnUninstall != null) btnUninstall.IsVisible = false;
            }
        }

        private void LoadComponents()
        {
            var components = new ObservableCollection<string>();

            if (!string.IsNullOrEmpty(_game.DlssVersion)) components.Add($"NVIDIA DLSS: {_game.DlssVersion}");
            if (!string.IsNullOrEmpty(_game.FsrVersion)) components.Add($"AMD FSR: {_game.FsrVersion}");
            if (!string.IsNullOrEmpty(_game.XessVersion)) components.Add($"Intel XeSS: {_game.XessVersion}");

            if (_game.IsOptiscalerInstalled)
            {
                string[] keyFiles = { "OptiScaler.ini", "dxgi.dll", "version.dll", "winmm.dll", "optiscaler.log" };
                foreach (var file in keyFiles)
                {
                    if (File.Exists(System.IO.Path.Combine(_game.InstallPath, file)))
                    {
                        components.Add($"Found: {file}");
                    }
                }

                if (File.Exists(System.IO.Path.Combine(_game.InstallPath, "nvapi64.dll")))
                    components.Add("Fakenvapi: installed");

                if (File.Exists(System.IO.Path.Combine(_game.InstallPath, "dlssg_to_fsr3_amd_is_better.dll")))
                    components.Add("NukemFG: installed");
            }

            var lstComponents = this.FindControl<ListBox>("LstComponents");
            if (lstComponents != null) lstComponents.ItemsSource = components;
        }

        private void CmbOptiVersion_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var cmb = sender as ComboBox;
            var selectedTag = (cmb?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            bool isBeta = !string.IsNullOrEmpty(selectedTag) && _betaVersions.Contains(selectedTag);

            var chkFakenvapi = this.FindControl<CheckBox>("ChkInstallFakenvapi");
            var chkNukemFG = this.FindControl<CheckBox>("ChkInstallNukemFG");

            if (isBeta)
            {
                if (chkFakenvapi != null)
                {
                    chkFakenvapi.IsEnabled = false;
                    chkFakenvapi.IsChecked = false;
                    ToolTip.SetTip(chkFakenvapi, "Included in beta version");
                }
                if (chkNukemFG != null)
                {
                    chkNukemFG.IsEnabled = false;
                    chkNukemFG.IsChecked = false;
                    ToolTip.SetTip(chkNukemFG, "Included in beta version");
                }
            }
            else
            {
                ConfigureAdditionalComponents();
            }
        }

        private void ConfigureAdditionalComponents()
        {
            GpuInfo? gpu = null;
            if (OperatingSystem.IsWindows() && _gpuService != null)
            {
                gpu = _gpuService.GetDiscreteGPU() ?? _gpuService.GetPrimaryGPU();
            }
            var chkInstallFakenvapi = this.FindControl<CheckBox>("ChkInstallFakenvapi");
            var chkInstallNukemFG = this.FindControl<CheckBox>("ChkInstallNukemFG");

            if (gpu != null && gpu.Vendor == GpuVendor.NVIDIA)
            {
                if (chkInstallFakenvapi != null)
                {
                    chkInstallFakenvapi.IsEnabled = false;
                    chkInstallFakenvapi.IsChecked = false;
                    ToolTip.SetTip(chkInstallFakenvapi, "Fakenvapi is not required for NVIDIA GPUs");
                }
            }
            else
            {
                if (chkInstallFakenvapi != null)
                {
                    chkInstallFakenvapi.IsEnabled = true;
                    ToolTip.SetTip(chkInstallFakenvapi, "Required for AMD/Intel GPUs to enable DLSS FG with Nukem mod");
                }
            }

            if (chkInstallNukemFG != null) chkInstallNukemFG.IsEnabled = true;
        }

        private string GetResourceString(string key, string fallback)
        {
            return Application.Current?.TryFindResource(key, out var res) == true && res is string str ? str : fallback;
        }
    }
}
