using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ValheimWorldSync.Desktop.ViewModels;

namespace ValheimWorldSync.Desktop;

// Explicit developer smoke mode: isolated profile, no credentials, no Steam, no visible window.
internal static class StartupSmoke
{
    public static async Task RunAsync(string output, Dispatcher dispatcher)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        using var model = new MainViewModel(dispatcher, Path.Combine(output, "profile-" + Guid.NewGuid().ToString("N")));
        await model.InitializeAsync();
        if (model.PlayCommand.CanExecute(null) || model.IsWorking) throw new InvalidOperationException("Unconfigured command guard failed.");
        var window = new MainWindow { DataContext = model, AllowClose = true };
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(660, 700));
        content.Arrange(new Rect(0, 0, 660, 700));
        content.UpdateLayout();
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var image = new RenderTargetBitmap(660, 700, 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(Path.Combine(output, "startup.png"))) encoder.Save(file);
        await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            success = true, framework = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            executable = Environment.ProcessPath, model.StatusTitle, model.CanExit,
            networkAccess = false, gameLaunched = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
    }
}
