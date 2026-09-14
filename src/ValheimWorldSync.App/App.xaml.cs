using System.Globalization;
using System.Text.Json;
using System.Windows;
using ValheimWorldSync.Core.Localization;
using ValheimWorldSync.Desktop.Tray;
using ValheimWorldSync.Desktop.ViewModels;
using ValheimWorldSync.Infrastructure.Configuration;
using ValheimWorldSync.Platform.Windows.Configuration;

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
        ApplySavedCulture();
        if (e.Args is ["--smoke-test", var output])
        {
            try { await Desktop.StartupSmoke.RunAsync(output, Dispatcher); Shutdown(0); }
            catch (Exception error)
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
                Shutdown(1);
            }
            return;
        }
        instance = new Mutex(true, "Local\\ValheimWorldSync-" + Environment.UserName, out ownsMutex);
        if (!ownsMutex)
        {
            MessageBox.Show(Strings.Get("App_AlreadyOpen"), "Valheim World Sync");
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
            MessageBox.Show(Strings.Get("App_RecoveryFailed"), "Valheim World Sync");
        }
    }
    private static void ApplySavedCulture() => ApplyLanguage(ReadSavedLanguage());
    public static void ApplyLanguage(string language)
    {
        var culture = CultureInfo.GetCultureInfo(Platform.Windows.Configuration.AppLanguage.Normalize(language));
        Strings.SetLanguage(culture);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
    private static string ReadSavedLanguage()
    {
        try
        {
            var path = Path.Combine(AppConfiguration.DataRoot, "settings.json");
            if (!File.Exists(path)) return AppLanguage.Default;
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), AppConfiguration.JsonOptions);
            return AppLanguage.Normalize(settings?.Language);
        }
        catch { return AppLanguage.Default; }
    }
    private void RequestExit()
    {
        if (model?.CanExit != true)
        {
            if (window is not null) TrayController.Show(window);
            MessageBox.Show(Strings.Get("App_WaitSession"), "Valheim World Sync");
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

