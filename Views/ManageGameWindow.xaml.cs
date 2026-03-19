using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using OptiscalerManager.Models;
using System.Collections.ObjectModel;

namespace OptiscalerManager.Views
{
    public partial class ManageGameWindow : Window
    {
        private readonly Game _game;
        private readonly Services.GpuDetectionService _gpuService;

        /// <summary>
        /// True if an install or uninstall completed successfully.
        /// MainWindow checks this when the dialog closes to trigger an automatic scan.
        /// </summary>
        public bool NeedsScan { get; private set; }

        public ManageGameWindow(Game game)
        {
            InitializeComponent();
            _game = game;
            _gpuService = new Services.GpuDetectionService();

            SetupUI();
            _ = LoadVersionsAsync();
        }

        private async Task LoadVersionsAsync()
        {
            var componentService = new Services.ComponentManagementService();
            if (componentService.OptiScalerAvailableVersions.Count == 0)
            {
                // Ensures we have the list if user opened manage window immediately on startup
                await componentService.CheckForUpdatesAsync();
            }

            Dispatcher.Invoke(() =>
            {
                var versions = componentService.OptiScalerAvailableVersions;
                CmbOptiVersion.Items.Clear();

                if (versions.Count == 0)
                {
                    CmbOptiVersion.Items.Add(FindResource("TxtNoOptiDetected") as string ?? "No version detected");
                    CmbOptiVersion.SelectedIndex = 0;
                    CmbOptiVersion.IsEnabled = false;
                    return;
                }

                bool isLatestMarked = false;
                int latestIndex = 0;
                int currentIndex = 0;

                foreach (var ver in versions)
                {
                    var display = ver;
                    if (!isLatestMarked && !ver.Contains("nightly", StringComparison.OrdinalIgnoreCase))
                    {
                        var latestStr = FindResource("TxtOptiLatest") as string ?? " (Latest)";
                        display += latestStr;
                        isLatestMarked = true;
                        latestIndex = currentIndex;
                    }
                    CmbOptiVersion.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = display, Tag = ver });
                    currentIndex++;
                }

                CmbOptiVersion.SelectedIndex = latestIndex;
            });
        }

        private void SetupUI()
        {
            TxtGameName.Text = _game.Name;
            TxtInstallPath.Text = _game.InstallPath;

            UpdateStatus();
            LoadComponents();
            ConfigureAdditionalComponents();

            BtnOpenFolder.Click += (s, e) =>
            {
                try
                {
                    string? dirToOpen = null;
                    var installService = new Services.GameInstallationService();
                    var determinedDir = installService.DetermineInstallDirectory(_game);

                    if (!string.IsNullOrEmpty(determinedDir) && Directory.Exists(determinedDir))
                    {
                        dirToOpen = determinedDir;
                    }
                    else if (!string.IsNullOrEmpty(_game.ExecutablePath) && File.Exists(_game.ExecutablePath))
                    {
                        dirToOpen = Path.GetDirectoryName(_game.ExecutablePath);
                    }
                    else if (!string.IsNullOrEmpty(_game.InstallPath) && Directory.Exists(_game.InstallPath))
                    {
                        dirToOpen = _game.InstallPath;
                    }

                    if (string.IsNullOrEmpty(dirToOpen))
                    {
                        new ConfirmDialog("Error", "The installation directory could not be found or no longer exists.", true) { Owner = this }.ShowDialog();
                        return;
                    }

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{dirToOpen.TrimEnd('\\', '/')}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    new ConfirmDialog("Error", $"Could not open folder:\n{ex.Message}", true) { Owner = this }.ShowDialog();
                }
            };

            BtnInstall.Click += BtnInstall_Click;
            BtnInstallManual.Click += async (s, e) => await ExecuteInstallAsync(true);

            BtnUninstall.Click += BtnUninstall_Click;

            // Uninstall Confirmation Modal
            BtnConfirmUninstallYes.Click += BtnConfirmUninstallYes_Click;
            BtnConfirmUninstallNo.Click += BtnConfirmUninstallNo_Click;
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteInstallAsync(false);
        }

        private async Task ExecuteInstallAsync(bool isManualMode)
        {
            try
            {
                var componentService = new Services.ComponentManagementService();
                var installService = new Services.GameInstallationService();

                var selectedVersionItem = CmbOptiVersion.SelectedItem as System.Windows.Controls.ComboBoxItem;
                var optiscalerVersion = selectedVersionItem?.Tag?.ToString();

                if (string.IsNullOrEmpty(optiscalerVersion))
                {
                    new ConfirmDialog("Error", "No OptiScaler version selected.", true) { Owner = this }.ShowDialog();
                    return;
                }

                BtnInstall.IsEnabled = false;
                BtnInstallManual.IsEnabled = false;
                BtnUninstall.IsEnabled = false;
                CmbOptiVersion.IsEnabled = false;

                // Configure Progress
                var progress = new Progress<double>(p =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (BdProgress.Visibility != Visibility.Visible)
                            BdProgress.Visibility = Visibility.Visible;

                        PrgDownload.Value = p;
                        var formatInstalling = FindResource("TxtInstallingFormat") as string ?? "Downloading OptiScaler v{0}... {1}%";
                        TxtProgressState.Text = string.Format(formatInstalling, optiscalerVersion, (int)p);
                    });
                });

                // Check local or download
                string optiCacheDir;
                try
                {
                    optiCacheDir = await componentService.DownloadOptiScalerAsync(optiscalerVersion, progress);
                }
                catch (Exception ex)
                {
                    var msgFormat = FindResource("TxtDownloadErrorPrefix") as string ?? "Failed to download OptiScaler: {0}";
                    var title = FindResource("TxtError") as string ?? "Error";
                    new ConfirmDialog(title, string.Format(msgFormat, ex.Message), true) { Owner = this }.ShowDialog();
                    return;
                }
                finally
                {
                    Dispatcher.Invoke(() =>
                    {
                        BdProgress.Visibility = Visibility.Collapsed;
                        BtnInstall.IsEnabled = true;
                        BtnInstallManual.IsEnabled = true;
                        BtnUninstall.IsEnabled = true;
                        CmbOptiVersion.IsEnabled = true;
                    });
                }

                var fakeCacheDir = componentService.GetFakenvapiCachePath();
                var nukemCacheDir = componentService.GetNukemFGCachePath();

                var selectedItem = CmbInjectionMethod.SelectedItem as System.Windows.Controls.ComboBoxItem;
                var injectionMethod = selectedItem?.Tag?.ToString() ?? "dxgi.dll";

                bool installFakenvapi = ChkInstallFakenvapi.IsChecked == true;
                bool installNukemFG = ChkInstallNukemFG.IsChecked == true;

                // Validate and download Fakenvapi if selected
                if (installFakenvapi && (!Directory.Exists(fakeCacheDir) || Directory.GetFiles(fakeCacheDir).Length == 0))
                {
                    try
                    {
                        // Ensure we have the latest version tags loaded before downloading
                        await componentService.CheckForUpdatesAsync();

                        Dispatcher.Invoke(() =>
                        {
                            BtnInstall.IsEnabled = false;
                            BtnInstallManual.IsEnabled = false;
                            BtnUninstall.IsEnabled = false;
                            CmbOptiVersion.IsEnabled = false;
                            BdProgress.Visibility = Visibility.Visible;
                            TxtProgressState.Text = "Downloading Fakenvapi...";
                            PrgDownload.IsIndeterminate = true;
                        });

                        await componentService.DownloadAndExtractFakenvapiAsync();
                    }
                    catch (Exception ex)
                    {
                        new ConfirmDialog("Error", $"Failed to download Fakenvapi: {ex.Message}", true) { Owner = this }.ShowDialog();
                        return;
                    }
                    finally
                    {
                        Dispatcher.Invoke(() =>
                        {
                            PrgDownload.IsIndeterminate = false;
                            BdProgress.Visibility = Visibility.Collapsed;
                            BtnInstall.IsEnabled = true;
                            BtnInstallManual.IsEnabled = true;
                            BtnUninstall.IsEnabled = true;
                            CmbOptiVersion.IsEnabled = true;
                        });
                    }
                }

                // Validate NukemFG cache if selected
                if (installNukemFG && (!Directory.Exists(nukemCacheDir) || Directory.GetFiles(nukemCacheDir).Length == 0))
                {
                    bool provided = componentService.ProvideNukemFGManually(isUpdate: false);
                    if (!provided || !Directory.Exists(nukemCacheDir) || Directory.GetFiles(nukemCacheDir).Length == 0)
                    {
                        return; // User cancelled or didn't provide it
                    }
                }

                installService.InstallOptiScaler(_game, optiCacheDir, injectionMethod,
                                                installFakenvapi, fakeCacheDir,
                                                installNukemFG, nukemCacheDir,
                                                optiscalerVersion: optiscalerVersion,
                                                isManualMode: isManualMode);

                var installedComponents = "OptiScaler";
                if (installFakenvapi) installedComponents += " + Fakenvapi";
                if (installNukemFG) installedComponents += " + NukemFG";

                NeedsScan = true;
                UpdateStatus();
                LoadComponents();

                var successFormat = FindResource("TxtInstallSuccessFormat") as string ?? "{0} installed successfully!";
                await ShowToastAsync(string.Format(successFormat, installedComponents));
            }
            catch (Exception ex)
            {
                new ConfirmDialog("Error", $"Installation failed: {ex.Message}", true) { Owner = this }.ShowDialog();
            }
        }

        private void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            BdConfirmUninstall.Visibility = Visibility.Visible;
            BtnInstall.IsEnabled = false;
            BtnInstallManual.IsEnabled = false;
            BtnUninstall.IsEnabled = false;
        }

        private void BtnConfirmUninstallNo_Click(object sender, RoutedEventArgs e)
        {
            BdConfirmUninstall.Visibility = Visibility.Collapsed;
            BtnInstall.IsEnabled = true;
            BtnInstallManual.IsEnabled = true;
            BtnUninstall.IsEnabled = true;
        }

        private async void BtnConfirmUninstallYes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BdConfirmUninstall.Visibility = Visibility.Collapsed;
                BtnInstall.IsEnabled = true;
                BtnInstallManual.IsEnabled = true;
                BtnUninstall.IsEnabled = true;

                var installService = new Services.GameInstallationService();
                installService.UninstallOptiScaler(_game);

                NeedsScan = true;
                UpdateStatus();
                LoadComponents();

                var successMsg = FindResource("TxtOptiUninstallSuccess") as string ?? "OptiScaler uninstalled successfully.";
                await ShowToastAsync(successMsg);
            }
            catch (Exception ex)
            {
                var failFormat = FindResource("TxtOptiUninstallFail") as string ?? "Uninstall failed: {0}";
                var titleMsg = FindResource("TxtError") as string ?? "Error";
                new ConfirmDialog(titleMsg, string.Format(failFormat, ex.Message), true) { Owner = this }.ShowDialog();
            }
        }

        private async Task ShowToastAsync(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtToastMessage.Text = message;
                BdToast.Visibility = Visibility.Visible;
                System.Media.SystemSounds.Asterisk.Play();
            });

            await Task.Delay(3500);

            Dispatcher.Invoke(() =>
            {
                BdToast.Visibility = Visibility.Collapsed;
            });
        }

        private void UpdateStatus()
        {
            if (_game.IsOptiscalerInstalled)
            {
                TxtStatus.Text = FindResource("TxtOptiInstalled") as string ?? "OptiScaler Installed";
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(118, 185, 0)); // Green

                if (!string.IsNullOrEmpty(_game.OptiscalerVersion))
                {
                    TxtVersion.Text = $"v{_game.OptiscalerVersion}";
                }

                BtnInstall.Visibility = Visibility.Visible;
                BtnInstallManual.Visibility = Visibility.Visible;
                BtnInstall.Content = FindResource("TxtUpdateOpti") as string ?? "Update / Reinstall";
                BtnInstallManual.Content = FindResource("TxtUpdateOptiManual") as string ?? "Manual Update";
                InstallBtnGroup.Visibility = Visibility.Visible;
                PnlInstallOptions.Visibility = Visibility.Visible;
                BtnUninstall.Visibility = Visibility.Visible;
            }
            else
            {
                TxtStatus.Text = FindResource("TxtOptiNotInstalled") as string ?? "Not Installed";
                StatusIndicator.Fill = new SolidColorBrush(Colors.Gray);
                TxtVersion.Text = "";

                BtnInstall.Visibility = Visibility.Visible;
                BtnInstallManual.Visibility = Visibility.Visible;
                BtnInstall.Content = FindResource("TxtInstallOpti") as string ?? "✦ Auto Install";
                BtnInstallManual.Content = FindResource("TxtBtnManualInstall") as string ?? "✦ Manual Install";
                InstallBtnGroup.Visibility = Visibility.Visible;
                PnlInstallOptions.Visibility = Visibility.Visible;
                BtnUninstall.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadComponents()
        {
            var components = new ObservableCollection<string>();

            // List detected techs
            if (!string.IsNullOrEmpty(_game.DlssVersion)) components.Add($"NVIDIA DLSS: {_game.DlssVersion}");
            if (!string.IsNullOrEmpty(_game.FsrVersion)) components.Add($"AMD FSR: {_game.FsrVersion}");
            if (!string.IsNullOrEmpty(_game.XessVersion)) components.Add($"Intel XeSS: {_game.XessVersion}");

            // List OptiScaler internals if present
            if (_game.IsOptiscalerInstalled)
            {
                // We could re-check files here or trust the analyzer.
                // For now, let's just show what we know or scan briefly.
                // Assuming we want to show if specific files exist.
                string[] keyFiles = { "OptiScaler.ini", "dxgi.dll", "version.dll", "winmm.dll", "optiscaler.log" };
                foreach (var file in keyFiles)
                {
                    if (File.Exists(Path.Combine(_game.InstallPath, file)))
                    {
                        components.Add($"Found: {file}");
                    }
                }
            }

            LstComponents.ItemsSource = components;
        }

        private void ConfigureAdditionalComponents()
        {
            var gpu = _gpuService.GetDiscreteGPU() ?? _gpuService.GetPrimaryGPU();

            // Fakenvapi is only for AMD/Intel GPUs
            if (gpu != null && gpu.Vendor == Services.GpuVendor.NVIDIA)
            {
                ChkInstallFakenvapi.IsEnabled = false;
                ChkInstallFakenvapi.IsChecked = false;
                ChkInstallFakenvapi.ToolTip = "Fakenvapi is not required for NVIDIA GPUs";
            }
            else
            {
                ChkInstallFakenvapi.IsEnabled = true;
                ChkInstallFakenvapi.ToolTip = "Required for AMD/Intel GPUs to enable DLSS FG with Nukem mod";
            }

            // NukemFG can be used with any GPU
            ChkInstallNukemFG.IsEnabled = true;
        }
    }
}
