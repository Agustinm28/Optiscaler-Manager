using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OptiscalerClient.Helpers;
using System.Diagnostics;
using OptiscalerClient.Services;

namespace OptiscalerClient.Views
{
    public partial class ManageDefaultVersionsWindow : Window
    {
        private readonly ComponentManagementService _componentService;
        private bool _optiDefaultShowingBeta;

        public ManageDefaultVersionsWindow()
        {
            InitializeComponent();
            _componentService = new ComponentManagementService();
        }

        public ManageDefaultVersionsWindow(Window owner, ComponentManagementService componentService)
        {
            InitializeComponent();
            _componentService = componentService;

            this.Opacity = 0;

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

            LoadCurrentSettings();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void LoadCurrentSettings()
        {
            // Determine if saved OptiScaler default is beta
            var savedOptiDefault = _componentService.Config.DefaultOptiScalerVersion;
            bool savedIsBeta = !string.IsNullOrEmpty(savedOptiDefault) &&
                               _componentService.BetaVersions.Contains(savedOptiDefault);
            _optiDefaultShowingBeta = savedIsBeta;

            UpdateOptiDefaultChannelButtons();
            PopulateDefaultOptiScalerVersionCombo(showBeta: savedIsBeta, restoreSaved: true);
            PopulateDefaultExtrasCombo();
            PopulateDefaultOptiPatcherCombo();
        }

        // ── OptiScaler Version ──────────────────────────────────────────────

        private void PopulateDefaultOptiScalerVersionCombo(bool showBeta, bool restoreSaved)
        {
            var cmb = this.FindControl<ComboBox>("CmbDefaultOptiScalerVersion");
            if (cmb == null) return;

            cmb.Items.Clear();

            var allVersions = _componentService.OptiScalerAvailableVersions;
            var betaSet = _componentService.BetaVersions;
            var latestStable = _componentService.LatestStableVersion;
            var latestBeta = _componentService.LatestBetaVersion;

            foreach (var ver in allVersions)
            {
                bool isBeta = betaSet.Contains(ver);
                if (isBeta != showBeta) continue;

                bool isLatestInChannel = showBeta
                    ? ver == latestBeta
                    : ver == latestStable;

                ComboBoxItem cbi;
                if (isLatestInChannel)
                {
                    var stack = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    stack.Children.Add(new TextBlock { Text = ver, VerticalAlignment = VerticalAlignment.Center });
                    stack.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(Color.Parse("#7C3AED")),
                        Padding = new Thickness(5, 1),
                        Child = new TextBlock
                        {
                            Text = "LATEST",
                            FontSize = 10,
                            Foreground = Brushes.White,
                            FontWeight = FontWeight.Bold,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    });
                    cbi = new ComboBoxItem { Content = stack, Tag = ver };
                }
                else
                {
                    cbi = new ComboBoxItem { Content = ver, Tag = ver };
                }
                cmb.Items.Add(cbi);
            }

            if (cmb.Items.Count == 0)
            {
                cmb.Items.Add(new ComboBoxItem { Content = "No Versions Available", Tag = "auto" });
                cmb.SelectedIndex = 0;
                cmb.IsEnabled = false;
                return;
            }

            cmb.IsEnabled = true;
            cmb.SelectedIndex = 0;

            if (restoreSaved)
            {
                var saved = _componentService.Config.DefaultOptiScalerVersion;
                if (!string.IsNullOrEmpty(saved) && !saved.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < cmb.Items.Count; i++)
                    {
                        if ((cmb.Items[i] as ComboBoxItem)?.Tag?.ToString() == saved)
                        {
                            cmb.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }
        }

        private void BtnOptiDefaultStable_Click(object? sender, RoutedEventArgs e)
        {
            if (!_optiDefaultShowingBeta) return;
            _optiDefaultShowingBeta = false;
            UpdateOptiDefaultChannelButtons();
            PopulateDefaultOptiScalerVersionCombo(showBeta: false, restoreSaved: false);
        }

        private void BtnOptiDefaultBeta_Click(object? sender, RoutedEventArgs e)
        {
            if (_optiDefaultShowingBeta) return;
            _optiDefaultShowingBeta = true;
            UpdateOptiDefaultChannelButtons();
            PopulateDefaultOptiScalerVersionCombo(showBeta: true, restoreSaved: false);
        }

        private void UpdateOptiDefaultChannelButtons()
        {
            var btnStable = this.FindControl<Button>("BtnOptiDefaultStable");
            var btnBeta = this.FindControl<Button>("BtnOptiDefaultBeta");
            if (btnStable == null || btnBeta == null) return;

            if (_optiDefaultShowingBeta)
            {
                btnStable.Classes.Remove("BtnPrimary"); btnStable.Classes.Add("BtnSecondary");
                btnBeta.Classes.Remove("BtnSecondary"); btnBeta.Classes.Add("BtnPrimary");
            }
            else
            {
                btnStable.Classes.Remove("BtnSecondary"); btnStable.Classes.Add("BtnPrimary");
                btnBeta.Classes.Remove("BtnPrimary"); btnBeta.Classes.Add("BtnSecondary");
            }
        }

        // ── FSR4 INT8 Extras ────────────────────────────────────────────────

        private void PopulateDefaultExtrasCombo()
        {
            var cmb = this.FindControl<ComboBox>("CmbDefaultExtrasVersion");
            if (cmb == null) return;

            cmb.Items.Clear();

            if (_componentService.ExtrasAvailableVersions.Count == 0)
            {
                cmb.Items.Add(new ComboBoxItem { Content = "No Versions Available", Tag = "none" });
                cmb.SelectedIndex = 0;
                cmb.IsEnabled = false;
                return;
            }

            cmb.IsEnabled = true;
            cmb.Items.Add(new ComboBoxItem { Content = "None", Tag = "none" });

            foreach (var ver in _componentService.ExtrasAvailableVersions)
            {
                bool isLatest = ver == _componentService.LatestExtrasVersion;
                ComboBoxItem cbi;
                if (isLatest)
                {
                    var stack = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    stack.Children.Add(new TextBlock { Text = ver, VerticalAlignment = VerticalAlignment.Center });
                    stack.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(Color.Parse("#7C3AED")),
                        Padding = new Thickness(5, 1),
                        Child = new TextBlock
                        {
                            Text = "LATEST",
                            FontSize = 10,
                            Foreground = Brushes.White,
                            FontWeight = FontWeight.Bold,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    });
                    cbi = new ComboBoxItem { Content = stack, Tag = ver };
                }
                else
                {
                    cbi = new ComboBoxItem { Content = ver, Tag = ver };
                }
                cmb.Items.Add(cbi);
            }

            // Restore saved
            var saved = _componentService.Config.DefaultExtrasVersion;
            cmb.SelectedIndex = 0; // None
            if (!string.IsNullOrEmpty(saved) && !saved.Equals("none", StringComparison.OrdinalIgnoreCase))
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
        }

        // ── OptiPatcher ─────────────────────────────────────────────────────

        private void PopulateDefaultOptiPatcherCombo()
        {
            var cmb = this.FindControl<ComboBox>("CmbDefaultOptiPatcherVersion");
            if (cmb == null) return;

            cmb.Items.Clear();

            cmb.Items.Add(new ComboBoxItem { Content = "None", Tag = "none" });

            var versions = _componentService.OptiPatcherAvailableVersions;
            if (versions.Count == 0)
            {
                cmb.SelectedIndex = 0;
                return;
            }

            foreach (var ver in versions)
            {
                bool isLatest = ver == _componentService.LatestOptiPatcherVersion;
                ComboBoxItem cbi;
                if (isLatest)
                {
                    var stack = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    stack.Children.Add(new TextBlock { Text = ver, VerticalAlignment = VerticalAlignment.Center });
                    stack.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(Color.Parse("#7C3AED")),
                        Padding = new Thickness(5, 1),
                        Child = new TextBlock
                        {
                            Text = "LATEST",
                            FontSize = 10,
                            Foreground = Brushes.White,
                            FontWeight = FontWeight.Bold,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    });
                    cbi = new ComboBoxItem { Content = stack, Tag = ver };
                }
                else
                {
                    cbi = new ComboBoxItem { Content = ver, Tag = ver };
                }
                cmb.Items.Add(cbi);
            }

            // Restore saved
            var saved = _componentService.Config.DefaultOptiPatcherVersion;
            cmb.SelectedIndex = 0; // None
            if (!string.IsNullOrEmpty(saved) && !saved.Equals("none", StringComparison.OrdinalIgnoreCase))
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
        }

        // ── Save / Cancel ───────────────────────────────────────────────────

        private void BtnSave_Click(object? sender, RoutedEventArgs e)
        {
            // Save OptiScaler version
            var cmbOpti = this.FindControl<ComboBox>("CmbDefaultOptiScalerVersion");
            if (cmbOpti?.SelectedItem is ComboBoxItem optiItem)
            {
                var ver = optiItem.Tag?.ToString();
                _componentService.Config.DefaultOptiScalerVersion = string.IsNullOrEmpty(ver) ? null : ver;
            }

            // Save Extras version
            var cmbExtras = this.FindControl<ComboBox>("CmbDefaultExtrasVersion");
            if (cmbExtras?.SelectedItem is ComboBoxItem extrasItem)
            {
                var ver = extrasItem.Tag?.ToString() ?? "none";
                _componentService.Config.DefaultExtrasVersion =
                    ver.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : ver;
            }

            // Save OptiPatcher version
            var cmbPatcher = this.FindControl<ComboBox>("CmbDefaultOptiPatcherVersion");
            if (cmbPatcher?.SelectedItem is ComboBoxItem patcherItem)
            {
                var ver = patcherItem.Tag?.ToString() ?? "none";
                _componentService.Config.DefaultOptiPatcherVersion =
                    ver.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : ver;
            }

            _componentService.SaveConfiguration();
            Close(true);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private void BtnOptiPatcherSupportLink_Click(object? sender, RoutedEventArgs e)
        {
            var url = "https://github.com/optiscaler/OptiPatcher/blob/main/GameSupport.md";
            try
            {
                var psi = new ProcessStartInfo(url) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch
            {
                // ignore failures to open browser
            }
        }
    }
}
