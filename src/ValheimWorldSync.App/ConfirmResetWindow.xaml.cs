using System.Windows;
namespace ValheimWorldSync.Desktop;

public partial class ConfirmResetWindow : Window
{
    private readonly string expected;
    public ConfirmResetWindow(string worldName) { InitializeComponent(); expected = worldName; Instruction.Text = $"A versão atual será preservada. Digite '{worldName}' para confirmar."; }
    private void ConfirmClicked(object sender, RoutedEventArgs e)
    { if (BackupCheck.IsChecked != true || NameBox.Text != expected) { ErrorText.Text = "Marque a confirmação e digite o nome exato."; return; } DialogResult = true; }
}
