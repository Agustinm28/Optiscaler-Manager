using System.Windows;
using System.Windows.Controls;

namespace OptiscalerClient.Views;

public partial class ComponentInfoWindow : Window
{
    public ComponentInfoWindow(Services.ComponentManagementService service, Window owner)
    {
        InitializeComponent();
        Owner = owner;
        // Resolve named elements via FindName — avoids IDE lag with x:Name generated fields
        Loaded += (_, _) => Populate(service);
    }

    private void Populate(Services.ComponentManagementService svc)
    {
        // Use FindName so the code compiles cleanly regardless of IDE code-gen state
        var txtOpti = FindName("TxtOptiVersion") as TextBlock;
        var bdOpti = FindName("BdOptiUpdate") as Border;
        var txtFake = FindName("TxtFakeVersion") as TextBlock;
        var bdFake = FindName("BdFakeUpdate") as Border;
        var txtNukem = FindName("TxtNukemVersion") as TextBlock;
        var bdNukem = FindName("BdNukemUpdate") as Border;

        // ── OptiScaler ────────────────────────────────────────────
        if (txtOpti != null)
            txtOpti.Text = string.IsNullOrWhiteSpace(svc.OptiScalerVersion)
                ? "Not installed"
                : svc.OptiScalerVersion;
        if (bdOpti != null)
            bdOpti.Visibility = svc.IsOptiScalerUpdateAvailable
                ? Visibility.Visible : Visibility.Collapsed;

        // ── Fakenvapi ─────────────────────────────────────────────
        if (txtFake != null)
            txtFake.Text = string.IsNullOrWhiteSpace(svc.FakenvapiVersion)
                ? "Not installed"
                : svc.FakenvapiVersion;
        if (bdFake != null)
            bdFake.Visibility = svc.IsFakenvapiUpdateAvailable
                ? Visibility.Visible : Visibility.Collapsed;

        // ── NukemFG ───────────────────────────────────────────────
        if (txtNukem != null)
            txtNukem.Text = string.IsNullOrWhiteSpace(svc.NukemFGVersion)
                ? "Not installed"
                : svc.NukemFGVersion;
        if (bdNukem != null)
            bdNukem.Visibility = svc.IsNukemFGUpdateAvailable
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnGuide_Click(object sender, RoutedEventArgs e)
    {
        var guide = new GuideWindow();
        guide.Owner = this;
        guide.ShowDialog();
    }
}
