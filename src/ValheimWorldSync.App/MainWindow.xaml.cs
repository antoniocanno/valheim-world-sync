using System.Windows;
namespace ValheimWorldSync;

public partial class MainWindow : Window
{
    public bool AllowClose { get; set; }
    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, args) => { if (!AllowClose) { args.Cancel = true; Hide(); } };
    }
}
