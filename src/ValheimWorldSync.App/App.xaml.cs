using System.Windows;
using ValheimWorldSync.Desktop.Tray;
using ValheimWorldSync.Desktop.ViewModels;

namespace ValheimWorldSync;

public partial class App : Application
{
    private Mutex? instance;
    private bool ownsMutex;
    private TrayController? tray;
    private MainViewModel? model;
    private MainWindow? window;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\ValheimWorldSync-" + Environment.UserName, out ownsMutex);
        if (!ownsMutex)
        {
            MessageBox.Show("Valheim World Sync já está aberto. Use o ícone na bandeja.", "Valheim World Sync");
            Shutdown(); return;
        }
        model = new MainViewModel(Dispatcher);
        window = new MainWindow { DataContext = model };
        MainWindow = window;
        tray = new(window, model, RequestExit);
        SessionEnding += (_, _) => { if (window is not null) window.AllowClose = true; };
        window.Show();
        try { await model.InitializeAsync(); }
        catch (Exception)
        {
            MessageBox.Show("Não foi possível iniciar a recuperação. Os arquivos locais foram preservados.", "Valheim World Sync");
        }
    }
    private void RequestExit()
    {
        if (model?.CanExit != true)
        {
            if (window is not null) TrayController.Show(window);
            MessageBox.Show("Aguarde a sessão ou sincronização terminar. Fechar a janela mantém o app na bandeja.", "Valheim World Sync");
            return;
        }
        if (window is not null) window.AllowClose = true;
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose(); model?.Dispose();
        if (ownsMutex) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}

