using System;
using System.Windows;

namespace OptiscalerClient.Views;

public partial class AppUpdateWindow : Window
{
    private readonly Services.AppUpdateService _updateService;

    public AppUpdateWindow(Services.AppUpdateService updateService)
    {
        InitializeComponent();
        _updateService = updateService;

        TxtVersion.Text = $"v{_updateService.LatestVersion}";
        TxtReleaseNotes.Text = string.IsNullOrWhiteSpace(_updateService.ReleaseNotes)
            ? "No release notes provided."
            : _updateService.ReleaseNotes;
    }

    private void BtnSkip_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        PanelInfo.Visibility = Visibility.Collapsed;
        PanelDownloading.Visibility = Visibility.Visible;
        BtnSkip.IsEnabled = false;
        BtnUpdate.IsEnabled = false;

        var progress = new Progress<double>(p =>
        {
            ProgDownload.Value = p;
        });

        try
        {
            await _updateService.DownloadAndPrepareUpdateAsync(progress);
            TxtDownloadStatus.Text = "Restarting...";
            _updateService.FinalizeAndRestart();
        }
        catch (Exception ex)
        {
            new ConfirmDialog("Error", $"Update failed: {ex.Message}", true) { Owner = this }.ShowDialog();
            PanelInfo.Visibility = Visibility.Visible;
            PanelDownloading.Visibility = Visibility.Collapsed;
            BtnSkip.IsEnabled = true;
            BtnUpdate.IsEnabled = true;
        }
    }
}
