using Microsoft.Win32;
using System.Windows;
using ValheimWorldSync.Core.Localization;
using ValheimWorldSync.Infrastructure.Configuration;
using ValheimWorldSync.Infrastructure.Storage;
using ValheimWorldSync.Platform.Windows.Configuration;
using ValheimWorldSync.Platform.Windows.Credentials;
using ValheimWorldSync.Platform.Windows.Invitations;

namespace ValheimWorldSync.Desktop;

public partial class SettingsWindow : Window
{
    private sealed record LanguageOption(string Code, string Label);
    private readonly ProfileStore store;
    private ProfileCatalog catalog = null!;
    private WorldProfile? selected;
    private readonly bool startImport;
    private string? pendingImportSource;
    public string? PendingImportPath { get; private set; }
    public SettingsWindow(ProfileStore store, bool startImport = false)
    {
        InitializeComponent(); this.store = store; this.startImport = startImport;
        Loaded += async (_, _) => { await Reload(); if (startImport) _ = Dispatcher.BeginInvoke(() => ImportInviteClicked(this, new RoutedEventArgs())); };
    }

    private void SecretChanged(object sender, RoutedEventArgs e)
    {
        if (SecretPlaceholder is not null)
            SecretPlaceholder.Visibility = SecretBox.SecurePassword.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task Reload()
    {
        catalog = await store.LoadAsync();
        PlayerBox.Text = catalog.Settings.PlayerName;
        var language = AppLanguage.Normalize(catalog.Settings.Language);
        var options = new[]
        {
            new LanguageOption(AppLanguage.English, Strings.Get("Settings_LanguageEnglish")),
            new LanguageOption(AppLanguage.Portuguese, Strings.Get("Settings_LanguagePortuguese"))
        };
        LanguageBox.ItemsSource = options;
        LanguageBox.SelectedItem = options.First(option => option.Code == language);
        ProfilesBox.ItemsSource = catalog.Profiles;
        ProfilesBox.SelectedItem = catalog.Selected ?? catalog.Profiles.FirstOrDefault();
        if (catalog.Profiles.Count == 0) ClearForNew();
    }
    private string SelectedLanguage() =>
        (LanguageBox.SelectedItem as LanguageOption)?.Code ?? AppLanguage.Normalize(catalog.Settings.Language);
    private async void ProfileChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        selected = ProfilesBox.SelectedItem as WorldProfile;
        if (selected is null) return;
        var c = selected.Connection; var l = selected.Local;
        var credentials = await store.ReadCredentialsAsync(selected);
        EndpointBox.Text = c.Endpoint; BucketBox.Text = c.Bucket; RetentionBox.Text = c.RetentionCount.ToString();
        AccessBox.Text = credentials?.AccessKeyId ?? ""; SecretBox.Clear(); AliasBox.Text = l.Alias;
        WorldIdBox.Text = c.WorldId; DisplayBox.Text = c.WorldDisplayName; FolderBox.Text = c.WorldFolderName;
        SavesBox.Text = string.IsNullOrWhiteSpace(l.SavesRootOverride) ? ValheimLocations.DefaultWorldsLocal : l.SavesRootOverride;
        ResultText.Text = "";
    }
    private void NewClicked(object sender, RoutedEventArgs e) => ClearForNew();
    private void ChooseWorldClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Escolha a pasta completa de um mundo local do Valheim 1.0" };
        if (dialog.ShowDialog(this) != true) return;
        var folder = new DirectoryInfo(dialog.FolderName);
        FolderBox.Text = folder.Name;
        if (string.IsNullOrWhiteSpace(DisplayBox.Text)) DisplayBox.Text = folder.Name;
        if (string.IsNullOrWhiteSpace(AliasBox.Text)) AliasBox.Text = folder.Name;

        var savesRoot = SavesBox.Text.Trim();
        var expectedParent = string.IsNullOrWhiteSpace(savesRoot)
            ? ValheimLocations.DefaultWorldsLocal
            : Path.GetFullPath(savesRoot);
        var actualParent = folder.Parent?.FullName ?? string.Empty;
        var isInsideSaves = string.Equals(
            Path.GetFullPath(actualParent).TrimEnd(Path.DirectorySeparatorChar),
            expectedParent.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

        if (isInsideSaves)
        {
            pendingImportSource = null;
            ResultText.Text = $"Mundo detectado dentro de '{expectedParent}'. Nenhuma cópia necessária ao salvar.";
        }
        else
        {
            pendingImportSource = folder.FullName;
            ResultText.Text = $"Mundo selecionado fora da pasta de saves. Ao salvar, '{folder.Name}' será copiado para '{expectedParent}'.";
        }
    }
    private void ChooseSavesRootClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Escolha a pasta raiz de saves locais do Valheim (ex: worlds_local)",
            InitialDirectory = Directory.Exists(SavesBox.Text) ? SavesBox.Text : ValheimLocations.DefaultWorldsLocal
        };
        if (dialog.ShowDialog(this) == true) SavesBox.Text = dialog.FolderName;
    }
    private void ClearForNew()
    {
        selected = null; ProfilesBox.SelectedItem = null; EndpointBox.Clear(); BucketBox.Clear(); AccessBox.Clear(); SecretBox.Clear();
        RetentionBox.Text = "10"; AliasBox.Clear(); WorldIdBox.Text = Guid.NewGuid().ToString("N");
        DisplayBox.Clear(); FolderBox.Clear(); SavesBox.Text = ValheimLocations.DefaultWorldsLocal; ResultText.Text = "Novo perfil.";
    }
    private (ProfileConnection Connection, ProfileLocalSettings Local, R2Credentials? Credentials) Values()
    {
        if (!int.TryParse(RetentionBox.Text, out var retention)) throw new InvalidDataException("Retenção inválida.");
        var worldId = WorldIdBox.Text.Trim();
        var connection = new ProfileConnection
        {
            Endpoint = EndpointBox.Text.Trim(),
            Bucket = BucketBox.Text.Trim(),
            WorldId = worldId,
            RemotePrefix = selected?.Connection.RemotePrefix ?? $"worlds/{worldId}/",
            WorldDisplayName = DisplayBox.Text.Trim(),
            WorldFolderName = FolderBox.Text.Trim(),
            RetentionCount = retention
        };
        var local = new ProfileLocalSettings
        {
            CredentialTarget = selected?.Local.CredentialTarget ?? Guid.Empty.ToString(),
            Alias = AliasBox.Text.Trim(),
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

        if (pendingImportSource is not null)
        {
            var savesRoot = string.IsNullOrWhiteSpace(local.SavesRootOverride)
                ? ValheimLocations.DefaultWorldsLocal
                : Path.GetFullPath(local.SavesRootOverride);
            var destination = Path.Combine(savesRoot, connection.WorldFolderName);
            if (MessageBox.Show(
                $"Copiar '{pendingImportSource}' para a pasta de saves do Valheim?\n\nDestino: {destination}\n\nA origem será preservada. Se o destino existir, será sobrescrito.",
                "Copiar mundo para pasta de saves",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                throw new InvalidDataException("Operação cancelada pelo usuário.");
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(pendingImportSource))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            PendingImportPath = destination;
            pendingImportSource = null;
        }

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
            PlayerName = PlayerBox.Text.Trim(),
            SelectedProfileId = profile.Id,
            OnboardingVersion = 1,
            Language = SelectedLanguage()
        });
        if (!string.Equals(AppLanguage.Normalize(catalog.Settings.Language), SelectedLanguage(), StringComparison.OrdinalIgnoreCase))
            MessageBox.Show(Strings.Get("Settings_RestartNote"), "Valheim World Sync");
        DialogResult = true;
    });
    private async void DeleteClicked(object sender, RoutedEventArgs e)
    {
        if (selected is null || MessageBox.Show("Excluir este perfil local? O mundo remoto não será removido.",
            "Excluir perfil", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await Run(async () => { await store.DeleteAsync(selected); await Reload(); });
    }
    private async void ExportInviteClicked(object sender, RoutedEventArgs e)
    {
        if (selected is null) { ResultText.Text = "Selecione um perfil."; return; }
        var password = AskPassword(); if (password is null) return;
        var dialog = new SaveFileDialog { Filter = "Convite Valheim World Sync|*.vwsinvite", FileName = selected.Connection.WorldDisplayName + ".vwsinvite" };
        if (dialog.ShowDialog(this) != true) return;
        await Run(async () =>
        {
            var credentials = await store.ReadCredentialsAsync(selected) ?? throw new InvalidDataException("Credenciais não encontradas.");
            var c = selected.Connection;
            await new InvitationCodec().WriteAsync(dialog.FileName, new(c.Endpoint, c.Bucket, c.RemotePrefix, c.WorldId,
                c.WorldDisplayName, c.WorldFolderName, c.RetentionCount, credentials), password);
            ResultText.Text = "Convite protegido exportado. Envie a senha separadamente.";
        });
    }
    private async void ImportInviteClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Convite Valheim World Sync|*.vwsinvite" };
        if (dialog.ShowDialog(this) != true) return;
        var password = AskPassword(); if (password is null) return;
        await Run(async () =>
        {
            var p = await new InvitationCodec().ReadAsync(dialog.FileName, password);
            var c = new ProfileConnection
            {
                Endpoint = p.Endpoint,
                Bucket = p.Bucket,
                RemotePrefix = p.RemotePrefix,
                WorldId = p.WorldId,
                WorldDisplayName = p.WorldDisplayName,
                WorldFolderName = p.WorldFolderName,
                RetentionCount = p.RetentionCount
            };
            using (var repository = new R2WorldRepository(ToConfiguration(c, p.Credentials), c.RemotePrefix)) await repository.TestConnectionAsync();
            var profile = await store.CreateAsync(c, new ProfileLocalSettings { CredentialTarget = "pending", Alias = p.WorldDisplayName }, p.Credentials);
            await store.SaveSettingsAsync(catalog.Settings with { SelectedProfileId = profile.Id, PlayerName = PlayerBox.Text.Trim(), OnboardingVersion = 1 });
            DialogResult = true;
        });
    }
    private string? AskPassword()
    {
        var window = new InvitePasswordWindow { Owner = this };
        return window.ShowDialog() == true ? window.Password : null;
    }
    private async Task Run(Func<Task> action)
    {
        try { IsEnabled = false; await action(); }
        catch (Exception exception) { ResultText.Text = exception.Message; }
        finally { IsEnabled = true; }
    }
    private static AppConfiguration ToConfiguration(ProfileConnection c, R2Credentials k) => new()
    {
        Endpoint = c.Endpoint,
        Bucket = c.Bucket,
        AccessKeyId = k.AccessKeyId,
        SecretAccessKey = k.SecretAccessKey,
        Player = "local",
        WorldId = c.WorldId,
        WorldFolderName = c.WorldFolderName,
        SavesRoot = ValheimLocations.DefaultWorldsLocal,
        BackupCount = c.RetentionCount
    };
}
