using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ValheimWorldSync;
using ValheimWorldSync.Desktop.ViewModels;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length == 1 ? args[0] : "artifacts/ui-smoke");
        Directory.CreateDirectory(output);
        var trace = new StringWriter();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(trace));
        var app = new App();
        app.InitializeComponent(); // Load the production resource dictionary without starting the production app.
        using var model = new MainViewModel(Dispatcher.CurrentDispatcher, Path.Combine(output, "profile"));
        var initialization = model.InitializeAsync();
        var frame = new DispatcherFrame();

        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = initialization.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        initialization.GetAwaiter().GetResult();
        if (model.IsWorking || model.PlayCommand.CanExecute(null)) throw new Exception("Unconfigured app must not enable playing.");
        var window = new MainWindow { DataContext = model };
        Render(window, Path.Combine(output, "initial.png"), 600, 620);
        window.DataContext = new Preview(model, "Midgard", "Sincronização pendente",
            "Progresso local salvo. Aguardando outro anfitrião liberar o mundo. Você pode exportar uma cópia para recuperação.");
        Render(window, Path.Combine(output, "pending.png"), 660, 680);
        window.DataContext = new Preview(model, "Um mundo com um nome bastante longo para testar o layout",
            "Progresso precisa de atenção", "Outra versão pode ter avançado. Progresso local preservado; exporte para recuperação manual.");
        Render(window, Path.Combine(output, "conflict.png"), 600, 620);
        window.AllowClose = true;
        window.Close();
        if (trace.ToString().Contains("Error:", StringComparison.OrdinalIgnoreCase))
            throw new Exception("WPF binding errors: " + trace);
        File.WriteAllText(Path.Combine(output, "result.txt"), "WPF resources, initial view model, command guards and three layout renders passed. No real R2 or Steam access.");
        Console.WriteLine("WPF smoke passed: " + output);
        return 0;
    }
    private static void Render(MainWindow window, string path, double width, double height)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var image = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }
    private sealed class Preview(MainViewModel commands, string world, string title, string message)
    {
        public string WorldLabel => world;
        public string StatusTitle => title;
        public string Message => message;
        public bool IsWorking => false;
        public AsyncCommand PlayCommand => commands.PlayCommand;
        public AsyncCommand ConfigureCommand => commands.ConfigureCommand;
        public AsyncCommand ReloadCommand => commands.ReloadCommand;
        public AsyncCommand FriendsCommand => commands.FriendsCommand;
        public AsyncCommand ImportCommand => commands.ImportCommand;
        public AsyncCommand RetryCommand => commands.RetryCommand;
        public AsyncCommand ExportCommand => commands.ExportCommand;
        public AsyncCommand UseCloudCommand => commands.UseCloudCommand;
        public AsyncCommand RecoveryCommand => commands.RecoveryCommand;
    }
}
