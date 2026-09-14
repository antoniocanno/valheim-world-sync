using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ValheimWorldSync;
using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Desktop;
using ValheimWorldSync.Desktop.ViewModels;
using ValheimWorldSync.Platform.Windows.Configuration;
using ValheimWorldSync.Platform.Windows.Credentials;

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
        Render(window, Path.Combine(output, "initial.png"), 760, 460);
        window.DataContext = new Preview(model, "Midgard", "Sincronização pendente",
            "Progresso local salvo. Aguardando outro anfitrião liberar o mundo. Você pode exportar uma cópia para recuperação.");
        Render(window, Path.Combine(output, "pending.png"), 820, 540);
        var profileSelector = Descendants<System.Windows.Controls.ComboBox>(window.Content as DependencyObject).Single();
        var selectedText = Descendants<System.Windows.Controls.TextBlock>(profileSelector).Select(text => text.Text).ToArray();
        if (!selectedText.Contains("XARABASKA") || selectedText.Any(text => text.Contains("WorldProfile {")))
            throw new Exception("Profile selector must display only the profile alias.");
        window.DataContext = new Preview(model, "Um mundo com um nome bastante longo para testar o layout",
            "Progresso precisa de atenção", "Outra versão pode ter avançado. Progresso local preservado; exporte para recuperação manual.");
        Render(window, Path.Combine(output, "conflict.png"), 760, 460);
        var settings = new SettingsWindow(new ProfileStore(Path.Combine(output, "settings-profile"), new WindowsCredentialVault()));
        Render(settings, Path.Combine(output, "settings.png"), 744, 1050);
        ((System.Windows.Controls.PasswordBox)settings.FindName("SecretBox")).Password = "preview";
        if (((System.Windows.Controls.TextBlock)settings.FindName("SecretPlaceholder")).Visibility != Visibility.Collapsed) throw new Exception("Secret placeholder overlaps input.");
        ((System.Windows.Controls.PasswordBox)settings.FindName("SecretBox")).Clear();
        if (((System.Windows.Controls.TextBlock)settings.FindName("SecretPlaceholder")).Visibility != Visibility.Visible) throw new Exception("Empty secret placeholder missing.");
        Render(settings, Path.Combine(output, "settings-small.png"), 684, 440);
        ((System.Windows.Controls.ScrollViewer)settings.Content).ScrollToBottom();
        Render(settings, Path.Combine(output, "settings-bottom.png"), 684, 440);
        settings.Close();
        var profile = new WorldProfile("preview", output,
            new ProfileConnection { Endpoint = "https://example.invalid", Bucket = "preview", RemotePrefix = "worlds/preview/", WorldId = "preview", WorldDisplayName = "Midgard", WorldFolderName = "Midgard" },
            new ProfileLocalSettings { CredentialTarget = "preview", Alias = "Midgard" });
        var recovery = new RecoveryWindow(profile, output);
        ((System.Windows.Controls.DataGrid)recovery.FindName("EntriesGrid")).ItemsSource = new[] {
            new { CreatedAt = DateTimeOffset.Now, Player = "desconhecido", Size = "7.0 MB", Origin = "antes-de-instalar" },
            new { CreatedAt = DateTimeOffset.Now.AddDays(-1), Player = "antonio", Size = "6.8 MB", Origin = "cópia local" }
        };
        Render(recovery, Path.Combine(output, "recovery.png"), 804, 480);
        recovery.Close();
        window.AllowClose = true;
        window.Close();
        if (trace.ToString().Contains("Error:", StringComparison.OrdinalIgnoreCase))
            throw new Exception("WPF binding errors: " + trace);
        File.WriteAllText(Path.Combine(output, "result.txt"), "WPF resources, initial view model, command guards and seven layout renders and secret placeholder checks passed. No real R2 or Steam access.");
        Console.WriteLine("WPF smoke passed: " + output);
        return 0;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Render(Window window, string path, double width, double height)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var image = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        image.Render(background);
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
        public SyncState State => title.Contains("pendente") ? SyncState.Pending : SyncState.Conflict;
        public bool IsProgressIndeterminate => false;
        public double ProgressPercent => 65;
        public string TransferDetails => "";
        public string CloudGuidance => "";
        public IReadOnlyList<WorldProfile> AvailableProfiles { get; } = [new WorldProfile("preview", "preview",
            new ProfileConnection { Endpoint = "https://example.invalid", Bucket = "preview", RemotePrefix = "worlds/preview/", WorldId = "preview", WorldDisplayName = "Midgard", WorldFolderName = "Midgard" },
            new ProfileLocalSettings { CredentialTarget = "preview", Alias = "XARABASKA" })];
        public WorldProfile? ActiveProfile { get => AvailableProfiles[0]; set { } }
        public AsyncCommand ResetCommand => commands.ResetCommand;
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
