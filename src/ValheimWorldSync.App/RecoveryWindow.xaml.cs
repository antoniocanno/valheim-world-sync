using Microsoft.Win32;
using System.Windows;
using ValheimWorldSync.Core.Localization;
using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Infrastructure.Recovery;
using ValheimWorldSync.Infrastructure.WorldFiles;
using ValheimWorldSync.Platform.Windows.Configuration;
using ValheimWorldSync.Platform.Windows.Game;

namespace ValheimWorldSync.Desktop;

public partial class RecoveryWindow : Window
{
    private readonly WorldProfile profile; private readonly RecoveryCatalog catalog; private readonly WorldArchive archive;
    public RecoveryWindow(WorldProfile profile, string dataRoot)
    {
        InitializeComponent(); this.profile = profile; var root = Path.Combine(dataRoot, "recovery", profile.Id);
        catalog = new(root); archive = new(profile.Root, profile.Id, root); Loaded += async (_, _) => await Reload();
    }
    private RecoveryEntry Selected => EntriesGrid.SelectedItem as RecoveryEntry ?? throw new InvalidDataException(Strings.Get("Recovery_SelectCopy"));
    private async Task Reload() => EntriesGrid.ItemsSource = await catalog.ListAsync();
    private async void ExportClicked(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var entry = Selected; var dialog = new SaveFileDialog { Filter = "Snapshot ZIP|*.zip", FileName = $"{Strings.Get("Common_ExportFilePrefix")}{entry.CreatedAt:yyyyMMdd-HHmmss}.zip" };
        if (dialog.ShowDialog(this) == true) await catalog.ExportAsync(entry, dialog.FileName);
    });
    private async void RestoreClicked(object sender, RoutedEventArgs e)
    {
        var entry = Selected; if (new WindowsGamePlatform().FindProcesses().Count != 0) { ResultText.Text = Strings.Get("Recovery_CloseGame"); return; }
        if (MessageBox.Show(Strings.Format("Recovery_RestorePrompt", entry.CreatedAt), Strings.Get("Recovery_RestoreTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await Run(() => archive.InstallAsync(entry.Version, entry.ArchivePath, profile.WorldPath, () => new WindowsGamePlatform().FindProcesses().Count != 0));
    }
    private async void DeleteClicked(object sender, RoutedEventArgs e)
    { var entry = Selected; if (MessageBox.Show(Strings.Get("Recovery_DeletePrompt"), Strings.Get("Recovery_DeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; await Run(async () => { await catalog.DeleteAsync(entry); await Reload(); }); }
    private async Task Run(Func<Task> action) { try { IsEnabled = false; await action(); ResultText.Text = Strings.Get("Recovery_Done"); } catch (Exception e) { ResultText.Text = e.Message; } finally { IsEnabled = true; } }
}
