using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using OptiscalerManager;
using OptiscalerManager.Converters;

namespace OptiscalerManager.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Services.GameScannerService _scannerService;
        private readonly Services.GamePersistenceService _persistenceService;
        private System.Collections.ObjectModel.ObservableCollection<Models.Game> _games;

        private readonly Services.ComponentManagementService _componentService;
        private readonly Services.GpuDetectionService _gpuService;

        // Held as a field because FindName from XAML x:Name can lag in IDE analysis
        private System.Windows.Controls.ScrollViewer? _gameListScrollViewer;

        public MainWindow()
        {
            InitializeComponent();
            _scannerService = new Services.GameScannerService();
            _persistenceService = new Services.GamePersistenceService();
            _componentService = new Services.ComponentManagementService();
            App.ChangeLanguage(_componentService.Config.Language);
            _gpuService = new Services.GpuDetectionService();
            _games = new System.Collections.ObjectModel.ObservableCollection<Models.Game>();
            LstGames.ItemsSource = _games;
            CollectionViewSource.GetDefaultView(_games).Filter = FilterGames;

            _componentService.OnStatusChanged += ComponentStatusChanged;

            this.Loaded += MainWindow_Loaded;
        }


        private void ComponentStatusChanged()
        {
            // Intentionally left empty since the NukemFG update button was removed.
        }

        private void UpdateSearchPlaceholderVisibility()
        {
            if (TxtSearchPlaceholder == null || TxtSearch == null) return;

            if (TxtSearch.IsFocused)
            {
                TxtSearchPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSearchPlaceholderVisibility();
            if (_games != null) CollectionViewSource.GetDefaultView(_games).Refresh();
        }

        private void TxtSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            UpdateSearchPlaceholderVisibility();
        }

        private void TxtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdateSearchPlaceholderVisibility();
        }

        private bool FilterGames(object item)
        {
            if (string.IsNullOrWhiteSpace(TxtSearch.Text)) return true;
            if (item is Models.Game game)
            {
                return game.Name.Contains(TxtSearch.Text, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Resolve the outer ScrollViewer once — avoids x:Name generation lag in IDE
            _gameListScrollViewer = FindName("GameListScrollViewer") as System.Windows.Controls.ScrollViewer;

            bool hadSavedGames = LoadSavedGames();
            LoadGpuInfo();
            _ = CheckUpdatesOnStartupAsync();   // fire-and-forget, updates status bar + version chip
            ComponentStatusChanged();

            if (!hadSavedGames)
            {
                BtnScan_Click(BtnScan, new RoutedEventArgs());
            }
        }

        /// <summary>
        /// WPF quirk: the ListBox has its own internal ScrollViewer that eats MouseWheel events
        /// even when VerticalScrollBarVisibility="Disabled". Intercepting PreviewMouseWheel
        /// (tunnel event — fires before the internal ScrollViewer sees it) and redirecting
        /// the delta to the named outer ScrollViewer is the only reliable fix.
        /// </summary>
        private void LstGames_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (_gameListScrollViewer is not null)
                _gameListScrollViewer.ScrollToVerticalOffset(
                    _gameListScrollViewer.VerticalOffset - e.Delta);
        }

        private void BtnGuide_Click2(object sender, RoutedEventArgs e)
        {
            var guide = new GuideWindow();
            guide.Owner = this;
            guide.ShowDialog();
        }

        private async Task CheckUpdatesOnStartupAsync()
        {
            try
            {
                TxtStatus.Text = FindResource("TxtCheckingUpdates") as string ?? "Checking for updates…";
                await _componentService.CheckForUpdatesAsync();

                var appUpdateService = new Services.AppUpdateService(_componentService);
                bool hasAppUpdate = await appUpdateService.CheckForAppUpdateAsync();
                if (hasAppUpdate)
                {
                    var updateWindow = new AppUpdateWindow(appUpdateService);
                    updateWindow.Owner = this;
                    updateWindow.ShowDialog();
                }
            }
            catch
            {
                // Silent failure on startup — status bar will still show Ready
            }
            finally
            {
                ComponentStatusChanged();
                TxtStatus.Text = FindResource("TxtReady") as string ?? "Ready";
            }
        }



        private bool _isInitializingLanguage = true;

        // View Toggles
        private void NavGames_Click(object sender, RoutedEventArgs e)
        {
            ViewGames.Visibility = Visibility.Visible;
            ViewSettings.Visibility = Visibility.Collapsed;
            ViewHelp.Visibility = Visibility.Collapsed;
        }

        private void NavHelp_Click(object sender, RoutedEventArgs e)
        {
            ViewGames.Visibility = Visibility.Collapsed;
            ViewSettings.Visibility = Visibility.Collapsed;
            ViewHelp.Visibility = Visibility.Visible;
            PopulateHelpContent();
        }

        private void NavSettings_Click(object sender, RoutedEventArgs e)
        {
            ViewGames.Visibility = Visibility.Collapsed;
            ViewSettings.Visibility = Visibility.Visible;
            ViewHelp.Visibility = Visibility.Collapsed;

            _isInitializingLanguage = true;
            var currentLang = App.CurrentLanguage;
            foreach (ComboBoxItem item in CmbLanguage.Items)
            {
                if (item.Tag?.ToString() == currentLang)
                {
                    CmbLanguage.SelectedItem = item;
                    break;
                }
            }
            _isInitializingLanguage = false;
        }

        // Settings Logic
        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingLanguage) return;

            if (CmbLanguage.SelectedItem is ComboBoxItem selectedItem)
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

        private void BtnManageCache_Click(object sender, RoutedEventArgs e)
        {
            var cacheWindow = new ManageCacheWindow();
            cacheWindow.Owner = this;
            cacheWindow.ShowDialog();

            // Re-check available versions just in case settings deleted them
            _ = _componentService.CheckForUpdatesAsync();
        }

        // Help Logic
        private void PopulateHelpContent()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version?.ToString(3) ?? "0.1.0";
                TxtAppVersion.Text = $"v{version}";

                var buildDate = System.IO.File.GetLastWriteTime(assembly.Location);
                TxtBuildDate.Text = buildDate.ToString("yyyy-MM-dd");
            }
            catch
            {
                TxtAppVersion.Text = "v0.1.0";
                TxtBuildDate.Text = "Unknown";
            }

            TxtOptiVersion.Text = string.IsNullOrWhiteSpace(_componentService.OptiScalerVersion)
                ? "Not installed"
                : _componentService.OptiScalerVersion;
            BdOptiUpdate.Visibility = _componentService.IsOptiScalerUpdateAvailable
                ? Visibility.Visible : Visibility.Collapsed;

            TxtFakeVersion.Text = string.IsNullOrWhiteSpace(_componentService.FakenvapiVersion)
                ? "Not installed"
                : _componentService.FakenvapiVersion;

            if (_componentService.IsNukemFGInstalled)
            {
                var ver = _componentService.NukemFGVersion;
                TxtNukemVersion.Text = (string.IsNullOrWhiteSpace(ver) || ver == "manual") ? "Available" : ver;
                BtnUpdateNukemFG.Content = "Update";
            }
            else
            {
                TxtNukemVersion.Text = "Not installed";
                BtnUpdateNukemFG.Content = "Install";
            }
        }

        private async void BtnUpdateFakenvapi_Click(object sender, RoutedEventArgs e)
        {
            BtnUpdateFakenvapi.IsEnabled = false;
            var originalContent = BtnUpdateFakenvapi.Content;
            BtnUpdateFakenvapi.Content = "Checking...";
            try
            {
                await _componentService.CheckForUpdatesAsync();
                
                if (_componentService.IsFakenvapiUpdateAvailable || string.IsNullOrEmpty(_componentService.FakenvapiVersion))
                {
                    BtnUpdateFakenvapi.Content = "Downloading...";
                    await _componentService.DownloadAndExtractFakenvapiAsync();
                    new ConfirmDialog("Success", "Fakenvapi downloaded successfully.", true) { Owner = this }.ShowDialog();
                    PopulateHelpContent();
                }
                else
                {
                    new ConfirmDialog("Up to date", "You already have the latest version of Fakenvapi.", true) { Owner = this }.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                new ConfirmDialog("Error", $"Error updating Fakenvapi: {ex.Message}", true) { Owner = this }.ShowDialog();
            }
            finally
            {
                BtnUpdateFakenvapi.Content = originalContent;
                BtnUpdateFakenvapi.IsEnabled = true;
            }
        }

        private void BtnUpdateNukemFG_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isUpdate = _componentService.IsNukemFGInstalled;
                bool success = _componentService.ProvideNukemFGManually(isUpdate);
                if (success)
                {
                    PopulateHelpContent();
                    new ConfirmDialog("Success", "NukemFG installed successfully.", true) { Owner = this }.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                new ConfirmDialog("Error", $"Error installing NukemFG: {ex.Message}", true) { Owner = this }.ShowDialog();
            }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdates.IsEnabled = false;
            try
            {
                await _componentService.CheckForUpdatesAsync();
                PopulateHelpContent();

                var appUpdateService = new Services.AppUpdateService(_componentService);
                bool hasAppUpdate = await appUpdateService.CheckForAppUpdateAsync();

                if (hasAppUpdate)
                {
                    var updateWindow = new AppUpdateWindow(appUpdateService);
                    updateWindow.Owner = this;
                    updateWindow.ShowDialog();
                }
                else
                {
                    new ConfirmDialog("Updates", "No new updates found for the application.", true) { Owner = this }.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                new ConfirmDialog("Error", $"Error checking for updates: {ex.Message}", true) { Owner = this }.ShowDialog();
            }
            finally
            {
                BtnCheckUpdates.IsEnabled = true;
            }
        }

        private void BtnGithub_Click(object sender, RoutedEventArgs e)
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
                new ConfirmDialog("Error", $"Could not open browser: {ex.Message}", true) { Owner = this }.ShowDialog();
            }
        }


        private bool LoadSavedGames()
        {
            var savedGames = _persistenceService.LoadGames();
            _games.Clear();
            foreach (var game in savedGames)
            {
                _games.Add(game);
            }
            var loadedFormat = FindResource("TxtLoadedGamesFormat") as string ?? "Loaded {0} games.";
            TxtStatus.Text = string.Format(loadedFormat, savedGames.Count);

            // Re-analyze in background to refresh OptiScaler state from the manifest on disk.
            // This keeps the list accurate even if files were changed outside the app.
            if (savedGames.Count > 0)
            {
                _ = Task.Run(() =>
                {
                    var analyzer = new Services.GameAnalyzerService();
                    var metadata = new Services.GameMetadataService();

                    foreach (var game in savedGames)
                    {
                        try { analyzer.AnalyzeGame(game); }
                        catch { /* Skip games with inaccessible paths */ }

                        if (string.IsNullOrEmpty(game.CoverImageUrl))
                        {
                            // Fetch the image synchonously inside this background task
                            game.CoverImageUrl = metadata.FetchCoverImageUrlAsync(game.Name).GetAwaiter().GetResult();
                        }
                    }

                    // Force WPF list to refresh bindings (Game doesn't implement INPC,
                    // so we replace each item to trigger CollectionChanged).
                    Dispatcher.Invoke(() =>
                    {
                        for (int i = 0; i < _games.Count; i++)
                            _games[i] = _games[i]; // Triggers CollectionChanged on each item
                        _persistenceService.SaveGames(_games); // Persist refreshed state
                    });
                });
            }

            return savedGames.Count > 0;
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            BtnScan.IsEnabled = false;
            TxtStatus.Text = FindResource("TxtScanningShort") as string ?? "Scanning for games...";
            OverlayScanning.Visibility = Visibility.Visible;

            try
            {
                var scanResults = await _scannerService.ScanAllGamesAsync();

                // Preserve existing manual games
                var manualGames = _games.Where(g => g.Platform == Models.GamePlatform.Manual).ToList();

                // Also keep any specific settings/state if we had any on scanned games (future proofing)
                // For now, we will replace scanned games with fresh scan results to ensure paths are correct if moved

                _games.Clear();

                // Re-add manual games and re-analyze them
                var analyzer = new Services.GameAnalyzerService();
                foreach (var manualGame in manualGames)
                {
                    analyzer.AnalyzeGame(manualGame);
                    _games.Add(manualGame);
                }

                // Add scanned games, avoiding duplicates if any manual game matches (unlikely but possible)
                var metadataService = new Services.GameMetadataService();
                foreach (var scannedGame in scanResults)
                {
                    // Check if already exists (by path mainly)
                    var exists = _games.Any(g => g.InstallPath.Equals(scannedGame.InstallPath, StringComparison.OrdinalIgnoreCase));
                    if (!exists)
                    {
                        if (string.IsNullOrEmpty(scannedGame.CoverImageUrl))
                        {
                            scannedGame.CoverImageUrl = await metadataService.FetchCoverImageUrlAsync(scannedGame.Name);
                        }
                        _games.Add(scannedGame);
                    }
                }

                // Save updated list
                _persistenceService.SaveGames(_games);

                var scanCompleteFormat = FindResource("TxtScanCompleteFormat") as string ?? "Scan complete. Total games: {0}";
                TxtStatus.Text = string.Format(scanCompleteFormat, _games.Count);
            }
            catch (Exception ex)
            {
                var errorFormat = FindResource("TxtErrorFormat") as string ?? "Error: {0}";
                TxtStatus.Text = string.Format(errorFormat, ex.Message);
                new ConfirmDialog("Error", ex.Message, true) { Owner = this }.ShowDialog();
            }
            finally
            {
                BtnScan.IsEnabled = true;
                OverlayScanning.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnAddManual_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe",
                Title = FindResource("TxtSelectExe") as string ?? "Select Game Executable"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var filePath = openFileDialog.FileName;
                var directory = System.IO.Path.GetDirectoryName(filePath);
                var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);

                // Check if already added
                if (_games.Any(g => g.ExecutablePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    var msg = FindResource("TxtGameDuplicate") as string ?? "This game is already in the list.";
                    var title = FindResource("TxtDuplicateTitle") as string ?? "Duplicate Game";
                    new ConfirmDialog(title, msg, true) { Owner = this }.ShowDialog();
                    return;
                }

                var newGame = new Models.Game
                {
                    Name = fileName,
                    InstallPath = directory ?? string.Empty,
                    ExecutablePath = filePath,
                    Platform = Models.GamePlatform.Manual,
                    AppId = "Manual_" + Guid.NewGuid().ToString() // Unique ID for manual games
                };

                // Analyze the manual game
                var analyzer = new Services.GameAnalyzerService();
                analyzer.AnalyzeGame(newGame);

                var metadata = new Services.GameMetadataService();
                newGame.CoverImageUrl = await metadata.FetchCoverImageUrlAsync(newGame.Name);

                _games.Add(newGame);
                _persistenceService.SaveGames(_games); // Save immediately
                var manualFormat = FindResource("TxtAddedRefFormat") as string ?? "Added {0} manually.";
                TxtStatus.Text = string.Format(manualFormat, fileName);
            }
        }
        private void BtnManage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Models.Game game)
            {
                var manageWindow = new ManageGameWindow(game);
                manageWindow.Owner = this;
                manageWindow.ShowDialog();

                // Refresh the specific item in the list and persist its state
                var index = _games.IndexOf(game);
                if (index != -1)
                {
                    _games[index] = game;
                    _persistenceService.SaveGames(_games);
                }

                // Force WPF to update the UI bindings for the modified game
                LstGames.Items.Refresh();
            }
        }

        private void BtnRemoveGame_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Models.Game game)
            {
                var title = FindResource("TxtRemoveGameTitle") as string ?? "Remove Game";
                var confirmFormat = FindResource("TxtRemoveGameConfirm") as string ?? "Are you sure you want to remove '{0}' from the list?";
                var message = string.Format(confirmFormat, game.Name);

                var dialog = new ConfirmDialog(title, message);
                dialog.Owner = Window.GetWindow(this);

                if (dialog.ShowDialog() == true)
                {
                    _games.Remove(game);
                    _persistenceService.SaveGames(_games);
                }
            }
        }

        private void LoadGpuInfo()
        {
            try
            {
                var gpu = _gpuService.GetDiscreteGPU() ?? _gpuService.GetPrimaryGPU();

                if (gpu != null)
                {
                    // Get vendor-specific icon and color
                    string icon;
                    System.Windows.Media.Brush color;

                    switch (gpu.Vendor)
                    {
                        case Services.GpuVendor.NVIDIA:
                            icon = "🟢";
                            color = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(118, 185, 0)); // NVIDIA Green
                            break;
                        case Services.GpuVendor.AMD:
                            icon = "🔴";
                            color = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(237, 28, 36)); // AMD Red
                            break;
                        case Services.GpuVendor.Intel:
                            icon = "🔵";
                            color = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 113, 197)); // Intel Blue
                            break;
                        default:
                            icon = "⚪";
                            color = Brushes.Gray;
                            break;
                    }

                    TxtGpuInfo.Text = $"{icon} {gpu.Name}";
                    TxtGpuInfo.Foreground = color;
                    TxtGpuInfo.ToolTip = $"{gpu.Name}\nVendor: {gpu.Vendor}\nVRAM: {gpu.VideoMemoryGB}\nDriver: {gpu.DriverVersion}";
                }
                else
                {
                    TxtGpuInfo.Text = FindResource("TxtNoGpu") as string ?? "⚠️ No GPU detected";
                    TxtGpuInfo.Foreground = Brushes.Orange;
                    TxtGpuInfo.ToolTip = FindResource("TxtNoGpuTip") as string ?? "No GPU was detected on this system";
                }
            }
            catch (Exception ex)
            {
                TxtGpuInfo.Text = FindResource("TxtGpuFail") as string ?? "⚠️ GPU detection failed";
                TxtGpuInfo.Foreground = Brushes.Gray;

                var format = FindResource("TxtGpuFailTipFormat") as string ?? "Error detecting GPU: {0}";
                TxtGpuInfo.ToolTip = string.Format(format, ex.Message);
            }
        }
    }
}
