using System.Windows;
using ValheimWorldSync.Infrastructure.Configuration;
using ValheimWorldSync.Infrastructure.Storage;
using ValheimWorldSync.Platform.Windows.Configuration;
using ValheimWorldSync.Platform.Windows.Credentials;

namespace ValheimWorldSync.Desktop;

public partial class SettingsWindow : Window
{
    private readonly ProfileStore store;
    private ProfileCatalog catalog = null!;
    private WorldProfile? selected;
    public SettingsWindow(ProfileStore store) { InitializeComponent(); this.store = store; Loaded += async (_, _) => await Reload(); }

    private async Task Reload()
    {
        catalog = await store.LoadOrMigrateAsync();
        PlayerBox.Text = catalog.Settings.PlayerName;
        ProfilesBox.ItemsSource = catalog.Profiles;
        ProfilesBox.SelectedItem = catalog.Selected ?? catalog.Profiles.FirstOrDefault();
        if (catalog.Profiles.Count == 0) ClearForNew();
    }
    private async void ProfileChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        selected = ProfilesBox.SelectedItem as WorldProfile;
        if (selected is null) return;
        var c = selected.Connection; var l = selected.Local;
        var credentials = await store.ReadCredentialsAsync(selected);
        EndpointBox.Text = c.Endpoint; BucketBox.Text = c.Bucket; RetentionBox.Text = c.RetentionCount.ToString();
        AccessBox.Text = credentials?.AccessKeyId ?? ""; SecretBox.Clear(); AliasBox.Text = l.Alias;
        WorldIdBox.Text = c.WorldId; DisplayBox.Text = c.WorldDisplayName; FolderBox.Text = c.WorldFolderName;
        SavesBox.Text = l.SavesRootOverride ?? ""; ResultText.Text = "";
    }
    private void NewClicked(object sender, RoutedEventArgs e) => ClearForNew();
    private void ClearForNew()
    {
        selected = null; ProfilesBox.SelectedItem = null; EndpointBox.Clear(); BucketBox.Clear(); AccessBox.Clear(); SecretBox.Clear();
        RetentionBox.Text = "10"; AliasBox.Clear(); WorldIdBox.Text = Guid.NewGuid().ToString("N");
        DisplayBox.Clear(); FolderBox.Clear(); SavesBox.Clear(); ResultText.Text = "Novo perfil.";
    }
    private (ProfileConnection Connection, ProfileLocalSettings Local, R2Credentials? Credentials) Values()
    {
        if (!int.TryParse(RetentionBox.Text, out var retention)) throw new InvalidDataException("Retenção inválida.");
        var worldId = WorldIdBox.Text.Trim();
        var connection = new ProfileConnection
        {
            Endpoint = EndpointBox.Text.Trim(), Bucket = BucketBox.Text.Trim(), WorldId = worldId,
            RemotePrefix = selected?.Connection.RemotePrefix ?? $"worlds/{worldId}/",
            WorldDisplayName = DisplayBox.Text.Trim(), WorldFolderName = FolderBox.Text.Trim(), RetentionCount = retention
        };
        var local = new ProfileLocalSettings
        {
            CredentialTarget = selected?.Local.CredentialTarget ?? Guid.Empty.ToString(), Alias = AliasBox.Text.Trim(),
            SavesRootOverride = string.IsNullOrWhiteSpace(SavesBox.Text) ? null : SavesBox.Text.Trim()
        };
        R2Credentials? credentials = string.IsNullOrEmpty(SecretBox.Password) ? null : new(AccessBox.Text.Trim(), SecretBox.Password);
        return (connection, local, credentials);
    }
    private async void TestClicked(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var (connection, _, entered) = Values();
        var credentials = entered ?? (selected is null ? null : await store.ReadCredentialsAsync(selected))
            ?? throw new InvalidDataException("Informe as credenciais.");
        using var repository = new R2WorldRepository(ToConfiguration(connection, credentials), connection.RemotePrefix);
        await repository.TestConnectionAsync();
        ResultText.Text = "Conexão validada: leitura, escrita e exclusão disponíveis.";
    });
    private async void SaveClicked(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var (connection, local, credentials) = Values();
        WorldProfile profile;
        if (selected is null) profile = await store.CreateAsync(connection, local,
            credentials ?? throw new InvalidDataException("Informe as credenciais."));
        else
        {
            await store.UpdateAsync(selected, connection, local, credentials);
            profile = selected with { Connection = connection, Local = local with { CredentialTarget = selected.Local.CredentialTarget } };
        }
        await store.SaveSettingsAsync(catalog.Settings with
        {
            PlayerName = PlayerBox.Text.Trim(), SelectedProfileId = profile.Id, OnboardingVersion = 1
        });
        DialogResult = true;
    });
    private async void DeleteClicked(object sender, RoutedEventArgs e)
    {
        if (selected is null || MessageBox.Show("Excluir este perfil local? O mundo remoto não será removido.",
            "Excluir perfil", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await Run(async () => { await store.DeleteAsync(selected); await Reload(); });
    }
    private async Task Run(Func<Task> action)
    {
        try { IsEnabled = false; await action(); }
        catch (Exception exception) { ResultText.Text = exception.Message; }
        finally { IsEnabled = true; }
    }
    private static AppConfiguration ToConfiguration(ProfileConnection c, R2Credentials k) => new()
    {
        Endpoint = c.Endpoint, Bucket = c.Bucket, AccessKeyId = k.AccessKeyId, SecretAccessKey = k.SecretAccessKey,
        Player = "local", WorldId = c.WorldId, WorldFolderName = c.WorldFolderName,
        SavesRoot = ValheimLocations.DefaultWorldsLocal, BackupCount = c.RetentionCount
    };
}
