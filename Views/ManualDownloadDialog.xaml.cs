using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Navigation;
using SharpCompress.Common;
using SharpCompress.Archives;

namespace OptiscalerClient.Views
{
    /// <summary>
    /// Dialog that asks the user to manually locate the NukemFG DLL.
    /// Works for both first-install and update flows.
    /// </summary>
    public partial class ManualDownloadDialog : Window
    {
        private const string RequiredDllName = "dlssg_to_fsr3_amd_is_better.dll";

        private readonly string _componentName;
        private readonly string _requiredFileName;
        private readonly string _targetCachePath;
        private readonly bool _isUpdate;

        public bool WasSuccessful { get; private set; }
        public string? SelectedPath { get; private set; }

        /// <param name="componentName">Display name for the component (e.g. "NukemFG").</param>
        /// <param name="requiredFileName">The specific file we need (e.g. "dlssg_to_fsr3_amd_is_better.dll").</param>
        /// <param name="targetCachePath">Folder where the DLL will be stored after confirmation.</param>
        /// <param name="isUpdate">When true, the dialog's text reflects an update rather than a first install.</param>
        public ManualDownloadDialog(string componentName, string requiredFileName, string targetCachePath, bool isUpdate = false)
        {
            InitializeComponent();

            _componentName = componentName;
            _requiredFileName = requiredFileName;
            _targetCachePath = targetCachePath;
            _isUpdate = isUpdate;

            SetupText();
        }

        private void SetupText()
        {
            TxtComponentName.Text = _componentName;
            TxtRequiredFile.Text = _requiredFileName;

            if (_isUpdate)
            {
                TxtTitle.Text = FindResource("TxtManualUpdateTitle") as string ?? "🔄 Manual Update Available";
                TxtMainInstruction.Text = FindResource("TxtManualUpdateInst") as string ??
                    "A new version of NukemFG is available on Nexus Mods.\nPlease download the updated file from the link below and select the DLL (or ZIP):";
                TxtBrowseHint.Text = FindResource("TxtManualUpdateHint") as string ?? "Click 'Browse' to select the DLL, ZIP, or folder with the updated file.";
                BtnConfirm.Content = FindResource("TxtBtnUpdate") as string ?? "Update";
                BtnSkip.Content = FindResource("TxtBtnLater") as string ?? "Later";
            }
            else
            {
                TxtTitle.Text = FindResource("TxtManualReqTitle") as string ?? "⚠️ Manual File Required";
                TxtMainInstruction.Text = FindResource("TxtManualReqInst") as string ??
                    "NukemFG cannot be downloaded automatically.\nPlease download the file from Nexus Mods and select the DLL (or ZIP) below:";
                TxtBrowseHint.Text = FindResource("TxtManualReqHint") as string ?? "Click 'Browse' to select the DLL directly, a ZIP, or the extracted folder.";
                BtnConfirm.Content = FindResource("TxtBtnConfirm") as string ?? "Confirm";
                BtnSkip.Content = FindResource("TxtBtnSkip") as string ?? "Skip";
            }
        }

        private void LnkDownload_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch { }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            // Allow user to select: the DLL directly, a ZIP, or (via folder dialog workaround) a folder.
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select {_requiredFileName}",
                Filter = $"NukemFG DLL ({RequiredDllName})|{RequiredDllName}|ZIP Archive (*.zip)|*.zip|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                var selectedPath = dialog.FileName;

                if (ValidateSelection(selectedPath, out string? errorMsg))
                {
                    TxtSelectedPath.Text = selectedPath;
                    SelectedPath = selectedPath;
                    BtnConfirm.IsEnabled = true;
                }
                else
                {
                    MessageBox.Show(
                        errorMsg,
                        FindResource("TxtError") as string ?? "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    BtnConfirm.IsEnabled = false;
                }
            }
        }

        /// <summary>
        /// Returns true if the selected path contains the required DLL (directly or inside a ZIP).
        /// </summary>
        private bool ValidateSelection(string path, out string? errorMsg)
        {
            errorMsg = null;
            try
            {
                // Direct DLL selection
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    if (Path.GetFileName(path).Equals(RequiredDllName, StringComparison.OrdinalIgnoreCase))
                        return true;

                    errorMsg = "Invalid file selected.";
                    return false;
                }

                // ZIP selection
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var archive = ArchiveFactory.Open(path);
                    bool found = archive.Entries.Any(e =>
                        !e.IsDirectory &&
                        Path.GetFileName(e.Key ?? "").Equals(RequiredDllName, StringComparison.OrdinalIgnoreCase));

                    if (!found)
                        errorMsg = "Required file not found in ZIP.";

                    return found;
                }

                errorMsg = "Unrecognized file type.";
                return false;
            }
            catch (Exception ex)
            {
                errorMsg = $"Failed to read file: {ex.Message}";
                return false;
            }
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedPath))
                return;

            try
            {
                // Ensure the target directory exists and is clean
                if (!Directory.Exists(_targetCachePath))
                    Directory.CreateDirectory(_targetCachePath);

                var destDllPath = Path.Combine(_targetCachePath, RequiredDllName);

                if (SelectedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    // Direct DLL copy
                    File.Copy(SelectedPath, destDllPath, overwrite: true);
                }
                else if (SelectedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract only the required DLL from the ZIP
                    using var archive = ArchiveFactory.Open(SelectedPath);
                    var entry = archive.Entries.FirstOrDefault(e =>
                        !e.IsDirectory &&
                        Path.GetFileName(e.Key ?? "").Equals(RequiredDllName, StringComparison.OrdinalIgnoreCase));

                    if (entry == null)
                        throw new FileNotFoundException("Required file not found in ZIP.");

                    entry.WriteToFile(destDllPath, new ExtractionOptions { Overwrite = true });
                }
                else
                {
                    throw new InvalidOperationException("Unsupported file type.");
                }

                WasSuccessful = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    FindResource("TxtError") as string ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            WasSuccessful = false;
            DialogResult = false;
            Close();
        }
    }
}
