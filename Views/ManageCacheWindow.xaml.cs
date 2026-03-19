using System.Windows;
using System.Windows.Controls;
using OptiscalerClient.Services;
using System;

namespace OptiscalerClient.Views
{
    public partial class ManageCacheWindow : Window
    {
        private ComponentManagementService _componentService;
        private string? _versionToDelete;

        public ManageCacheWindow()
        {
            InitializeComponent();
            _componentService = new ComponentManagementService();
            LoadVersions();

            BtnConfirmDeleteNo.Click += BtnConfirmDeleteNo_Click;
            BtnConfirmDeleteYes.Click += BtnConfirmDeleteYes_Click;
        }

        private void LoadVersions()
        {
            var versions = _componentService.GetDownloadedOptiScalerVersions();
            LstCacheVersions.ItemsSource = versions;

            if (versions.Count == 0)
            {
                TxtNoVersions.Visibility = Visibility.Visible;
                LstCacheVersions.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtNoVersions.Visibility = Visibility.Collapsed;
                LstCacheVersions.Visibility = Visibility.Visible;
            }
        }

        private void BtnDeleteVersion_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var version = button?.Tag as string;

            if (!string.IsNullOrEmpty(version))
            {
                _versionToDelete = version;
                var format = FindResource("TxtConfirmDelFormat") as string ?? "Are you sure you want to delete OptiScaler v{0} from local storage?";
                TxtConfirmDeleteMessage.Text = string.Format(format, version);
                BdConfirmDelete.Visibility = Visibility.Visible;
            }
        }

        private void BtnConfirmDeleteNo_Click(object sender, RoutedEventArgs e)
        {
            BdConfirmDelete.Visibility = Visibility.Collapsed;
            _versionToDelete = string.Empty;
        }

        private async void BtnConfirmDeleteYes_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_versionToDelete)) return;

            try
            {
                BdConfirmDelete.Visibility = Visibility.Collapsed;
                _componentService.DeleteOptiScalerCache(_versionToDelete);
                LoadVersions(); // Refresh list

                var formatSuccess = FindResource("TxtDeletedSuccessFormat") as string ?? "OptiScaler v{0} deleted successfully.";
                await ShowToastAsync(string.Format(formatSuccess, _versionToDelete));
            }
            catch (Exception ex)
            {
                var formatError = FindResource("TxtFailedDeleteFormat") as string ?? "Failed to delete version: {0}";
                var titleMsg = FindResource("TxtError") as string ?? "Error";
                MessageBox.Show(string.Format(formatError, ex.Message), titleMsg, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _versionToDelete = string.Empty;
            }
        }

        private async System.Threading.Tasks.Task ShowToastAsync(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtToastMessage.Text = message;
                BdToast.Visibility = Visibility.Visible;
                System.Media.SystemSounds.Asterisk.Play();
            });

            await System.Threading.Tasks.Task.Delay(3500);

            Dispatcher.Invoke(() =>
            {
                BdToast.Visibility = Visibility.Collapsed;
            });
        }
    }
}
