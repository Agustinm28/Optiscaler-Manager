using System.Windows;

namespace OptiscalerManager.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, bool isAlert = false)
    {
        InitializeComponent();
        this.Title = title;
        TxtTitle.Text = title;
        TxtMessage.Text = message;

        if (isAlert)
        {
            BtnCancel.Visibility = Visibility.Collapsed;
            BtnConfirm.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "TxtGotIt");
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        this.Close();
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = true;
        this.Close();
    }
}
