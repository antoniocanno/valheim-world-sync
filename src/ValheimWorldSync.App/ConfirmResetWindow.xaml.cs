using System.Windows;
using ValheimWorldSync.Core.Localization;
namespace ValheimWorldSync.Desktop;

public partial class ConfirmResetWindow : Window
{
    private readonly string expected;
    public ConfirmResetWindow(string worldName) { InitializeComponent(); expected = worldName; Instruction.Text = Strings.Format("Reset_Instruction", worldName); }
    private void ConfirmClicked(object sender, RoutedEventArgs e)
    { if (BackupCheck.IsChecked != true || NameBox.Text != expected) { ErrorText.Text = Strings.Get("Reset_Validation"); return; } DialogResult = true; }
}
