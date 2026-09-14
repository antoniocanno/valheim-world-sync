using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using ValheimWorldSync.Core.Localization;
using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Desktop.ViewModels;
namespace ValheimWorldSync.Desktop.Tray;

public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon icon;
    private readonly ContextMenuStrip menu;
    private readonly Icon? customIcon;
    private SyncState? lastAlert;
    public TrayController(MainWindow window, MainViewModel model, Action exit)
    {
        menu = new ContextMenuStrip();
        menu.Items.Add(Strings.Get("Tray_Open"), null, (_, _) => Show(window));
        menu.Items.Add(Strings.Get("Main_Play"), null, (_, _) => model.PlayCommand.Execute(null));
        menu.Items.Add(Strings.Get("Main_Retry"), null, (_, _) => model.RetryCommand.Execute(null));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.Get("Tray_Exit"), null, (_, _) => exit());
        var resourceInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/ValheimWorldSync;component/vws.ico", UriKind.Absolute))
            ?? System.Windows.Application.GetResourceStream(new Uri("/vws.ico", UriKind.Relative));
        customIcon = resourceInfo?.Stream is not null
            ? new Icon(resourceInfo.Stream)
            : (File.Exists(Path.Combine(AppContext.BaseDirectory, "vws.ico"))
                ? new Icon(Path.Combine(AppContext.BaseDirectory, "vws.ico"))
                : (Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "") ?? SystemIcons.Application));
        icon = new NotifyIcon { Icon = customIcon, Text = "Valheim World Sync", Visible = true, ContextMenuStrip = menu };
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
    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        menu.Dispose();
        if (customIcon != SystemIcons.Application) customIcon?.Dispose();
    }
}

