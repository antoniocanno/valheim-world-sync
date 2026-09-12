using System.Windows;
namespace ValheimWorldSync.Desktop;
public partial class InvitePasswordWindow : Window
{
    public string Password => PasswordBox.Password;
    public InvitePasswordWindow() { InitializeComponent(); }
    private void ContinueClicked(object sender, RoutedEventArgs e)
    {
        if (Password.Length < 12) { ErrorText.Text = "A senha precisa ter pelo menos 12 caracteres."; return; }
        DialogResult = true;
    }
}
