using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Desktop.ViewModels;
namespace ValheimWorldSync.Desktop.Tray;

public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly ContextMenuStrip menu;
    private SyncState? lastAlert;
    public TrayController(MainWindow window, MainViewModel model, Action exit)
    {
        menu = new ContextMenuStrip();
        menu.Items.Add("Abrir Valheim World Sync", null, (_, _) => Show(window));
        menu.Items.Add("Jogar", null, (_, _) => model.PlayCommand.Execute(null));
        menu.Items.Add("Tentar sincronizar", null, (_, _) => model.RetryCommand.Execute(null));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => exit());
        icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "Valheim World Sync", Visible = true, ContextMenuStrip = menu };
        icon.DoubleClick += (_, _) => Show(window);
        model.StatusUpdated += status =>
        {
            var title = "Valheim Sync — " + model.StatusTitle;
            icon.Text = title.Length > 63 ? title[..63] : title;
            if (status.State is SyncState.Pending or SyncState.Conflict or SyncState.Error && lastAlert != status.State)
            {
                icon.ShowBalloonTip(5000, model.StatusTitle, status.Message, ToolTipIcon.Warning);
                lastAlert = status.State;
            }
            else if (status.State == SyncState.Idle) lastAlert = null;
        };
    }
    public static void Show(MainWindow window)
    {
        window.Show(); window.WindowState = WindowState.Normal; window.Activate();
    }
    public void Dispose() { icon.Visible = false; icon.Dispose(); menu.Dispose(); }
}
