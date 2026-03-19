using System.Windows;
using System.Windows.Controls;
using System.Linq;

namespace OptiscalerClient.Views
{
    public partial class SettingsWindow : Window
    {
        private Services.ComponentManagementService _componentService;
        private bool _isInitializing = true;

        public SettingsWindow(Services.ComponentManagementService componentService)
        {
            InitializeComponent();
            _componentService = componentService;

            // Pre-select the correct language in the ComboBox
            var currentLang = App.CurrentLanguage;
            foreach (ComboBoxItem item in CmbLanguage.Items)
            {
                if (item.Tag?.ToString() == currentLang)
                {
                    CmbLanguage.SelectedItem = item;
                    break;
                }
            }

            // Set initial value for App Repo
            if (!string.IsNullOrEmpty(_componentService.Config.App.RepoOwner) && !string.IsNullOrEmpty(_componentService.Config.App.RepoName))
            {
                TxtAppRepo.Text = $"{_componentService.Config.App.RepoOwner}/{_componentService.Config.App.RepoName}";
            }

            _isInitializing = false;
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

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
        }

        private void TxtAppRepo_LostFocus(object sender, RoutedEventArgs e)
        {
            var parts = TxtAppRepo.Text.Split('/');
            if (parts.Length == 2)
            {
                _componentService.Config.App.RepoOwner = parts[0].Trim();
                _componentService.Config.App.RepoName = parts[1].Trim();
                _componentService.SaveConfiguration();
            }
        }
    }
}
