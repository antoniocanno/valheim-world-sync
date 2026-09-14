using System.Windows;
using ValheimWorldSync.Core.Localization;
namespace ValheimWorldSync.Desktop;

public partial class InvitePasswordWindow : Window
{
    public string Password => PasswordBox.Password;
    public InvitePasswordWindow() { InitializeComponent(); }
    private void ContinueClicked(object sender, RoutedEventArgs e)
    {
        if (Password.Length < 3) { ErrorText.Text = Strings.Get("Invite_TooShort"); return; }
        DialogResult = true;
    }
}
