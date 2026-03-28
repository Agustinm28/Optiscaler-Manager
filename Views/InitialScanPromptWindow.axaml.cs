using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using OptiscalerClient.Helpers;
using OptiscalerClient.Models;
using OptiscalerClient.Services;

namespace OptiscalerClient.Views
{
    public sealed class InitialScanOptions
    {
        public ScanSourcesConfig ScanSources { get; }
        public List<string> DriveRoots { get; }

        public InitialScanOptions(ScanSourcesConfig scanSources, List<string> driveRoots)
        {
            ScanSources = scanSources;
            DriveRoots = driveRoots;
        }
    }

    public partial class InitialScanPromptWindow : Window
    {
        private readonly ComponentManagementService _componentService;
        private readonly List<DriveToggle> _driveToggles = new();

        public InitialScanPromptWindow()
        {
            InitializeComponent();
            _componentService = new ComponentManagementService();
        }

        public InitialScanPromptWindow(Window owner, ComponentManagementService componentService, bool isFirstTime = true)
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

            ApplyTitleText(isFirstTime);
            LoadCurrentSettings();
            PopulateDrives();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void LoadCurrentSettings()
        {
            var config = _componentService.Config.ScanSources;

            var tglSteam = this.FindControl<ToggleSwitch>("TglSteam");
            var tglEpic = this.FindControl<ToggleSwitch>("TglEpic");
            var tglGOG = this.FindControl<ToggleSwitch>("TglGOG");
            var tglXbox = this.FindControl<ToggleSwitch>("TglXbox");
            var tglEA = this.FindControl<ToggleSwitch>("TglEA");
            var tglUbisoft = this.FindControl<ToggleSwitch>("TglUbisoft");

            if (tglSteam != null) tglSteam.IsChecked = config.ScanSteam;
            if (tglEpic != null) tglEpic.IsChecked = config.ScanEpic;
            if (tglGOG != null) tglGOG.IsChecked = config.ScanGOG;
            if (tglXbox != null) tglXbox.IsChecked = config.ScanXbox;
            if (tglEA != null) tglEA.IsChecked = config.ScanEA;
            if (tglUbisoft != null) tglUbisoft.IsChecked = config.ScanUbisoft;
        }

        private void ApplyTitleText(bool isFirstTime)
        {
            var title = this.FindControl<TextBlock>("TxtTitle");
            var desc = this.FindControl<TextBlock>("TxtDesc");

            var titleKey = isFirstTime ? "TxtInitialScanTitle" : "TxtScanSetupTitle";
            var descKey = isFirstTime ? "TxtInitialScanDesc" : "TxtScanSetupDesc";

            title?.SetValue(TextBlock.TextProperty, GetResourceText(titleKey, title?.Text ?? ""));
            desc?.SetValue(TextBlock.TextProperty, GetResourceText(descKey, desc?.Text ?? ""));
        }

        private static string GetResourceText(string key, string fallback)
        {
            return Application.Current?.FindResource(key) as string ?? fallback;
        }

        private void PopulateDrives()
        {
            var pnlDriveList = this.FindControl<StackPanel>("PnlDriveList");
            var txtNoDrives = this.FindControl<TextBlock>("TxtNoDrives");
            if (pnlDriveList == null) return;

            pnlDriveList.Children.Clear();
            _driveToggles.Clear();

            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed ||
                                          d.DriveType == DriveType.Removable ||
                                          d.DriveType == DriveType.Network))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (drives.Count == 0)
            {
                if (txtNoDrives != null)
                    txtNoDrives.IsVisible = true;
                return;
            }

            if (txtNoDrives != null)
                txtNoDrives.IsVisible = false;

            foreach (var drive in drives)
            {
                var label = SafeGetVolumeLabel(drive);
                var name = string.IsNullOrWhiteSpace(label) ? drive.Name : $"{drive.Name} ({label})";

                var toggle = new ToggleSwitch
                {
                    IsChecked = true,
                    OnContent = "",
                    OffContent = ""
                };

                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto")
                };

                var text = new TextBlock
                {
                    Text = name,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Foreground = Application.Current?.FindResource("BrTextPrimary") as IBrush ?? Brushes.White
                };

                row.Children.Add(text);
                Grid.SetColumn(text, 0);

                row.Children.Add(toggle);
                Grid.SetColumn(toggle, 1);

                pnlDriveList.Children.Add(row);
                _driveToggles.Add(new DriveToggle(drive.Name, toggle));
            }
        }

        private static string SafeGetVolumeLabel(DriveInfo drive)
        {
            try
            {
                return drive.VolumeLabel ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void BtnStartScan_Click(object? sender, RoutedEventArgs e)
        {
            var tglSteam = this.FindControl<ToggleSwitch>("TglSteam");
            var tglEpic = this.FindControl<ToggleSwitch>("TglEpic");
            var tglGOG = this.FindControl<ToggleSwitch>("TglGOG");
            var tglXbox = this.FindControl<ToggleSwitch>("TglXbox");
            var tglEA = this.FindControl<ToggleSwitch>("TglEA");
            var tglUbisoft = this.FindControl<ToggleSwitch>("TglUbisoft");

            var sources = new ScanSourcesConfig
            {
                ScanSteam = tglSteam?.IsChecked ?? true,
                ScanEpic = tglEpic?.IsChecked ?? true,
                ScanGOG = tglGOG?.IsChecked ?? true,
                ScanXbox = tglXbox?.IsChecked ?? true,
                ScanEA = tglEA?.IsChecked ?? true,
                ScanUbisoft = tglUbisoft?.IsChecked ?? true,
                CustomFolders = _componentService.Config.ScanSources.CustomFolders.ToList()
            };

            var selectedDrives = _driveToggles
                .Where(d => d.Toggle.IsChecked ?? false)
                .Select(d => d.Root)
                .ToList();

            Close(new InitialScanOptions(sources, selectedDrives));
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }

        private sealed class DriveToggle
        {
            public string Root { get; }
            public ToggleSwitch Toggle { get; }

            public DriveToggle(string root, ToggleSwitch toggle)
            {
                Root = root;
                Toggle = toggle;
            }
        }
    }
}
