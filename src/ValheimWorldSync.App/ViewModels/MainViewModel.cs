using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using ValheimWorldSync.Core.Models;
using ValheimWorldSync.Core.Synchronization;
using ValheimWorldSync.Desktop.Game;
using ValheimWorldSync.Infrastructure.Configuration;
using ValheimWorldSync.Infrastructure.Recovery;
using ValheimWorldSync.Infrastructure.Storage;
using ValheimWorldSync.Infrastructure.WorldFiles;

namespace ValheimWorldSync.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer;
    private readonly List<AsyncCommand> commands = [];
    private readonly string dataRoot;
    private readonly string configPath;
    private R2WorldRepository? repository;
    private SyncEngine? engine;
    private AppConfiguration? configuration;
    private bool localWork;
    private string message = "Carregando configuração…";
    private string statusTitle = "Preparando";
    private string worldLabel = "MUNDO COMPARTILHADO";
    public string Message { get => message; private set { message = value; Changed(); } }
    public string StatusTitle { get => statusTitle; private set { statusTitle = value; Changed(); } }
    public string WorldLabel { get => worldLabel; private set { worldLabel = value; Changed(); } }
    public bool IsWorking => localWork || engine?.IsBusy == true;
    public bool CanExit => !IsWorking && !(File.Exists(Path.Combine(dataRoot, "session.json")) && new WindowsGamePlatform().FindProcesses().Count != 0);
    public SyncState State { get; private set; } = SyncState.Idle;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<SyncStatus>? StatusUpdated;
    public AsyncCommand PlayCommand { get; }
    public AsyncCommand ConfigureCommand { get; }
    public AsyncCommand ReloadCommand { get; }
    public AsyncCommand FriendsCommand { get; }
    public AsyncCommand ImportCommand { get; }
    public AsyncCommand RetryCommand { get; }
    public AsyncCommand ExportCommand { get; }
    public AsyncCommand UseCloudCommand { get; }
    public AsyncCommand RecoveryCommand { get; }

    public MainViewModel(Dispatcher dispatcher, string? dataRoot = null)
    {
        this.dispatcher = dispatcher;
        this.dataRoot = dataRoot ?? AppConfiguration.DataRoot;
        configPath = Path.Combine(this.dataRoot, "config.json");
        PlayCommand = Command(() => RunEngine(() => engine!.PlayAsync(lifetime.Token)),
            () => engine is not null && !IsWorking && State is SyncState.Idle or SyncState.Offline);
        ConfigureCommand = Command(OpenConfiguration, () => !IsWorking);
        ReloadCommand = Command(InitializeAsync, () => !IsWorking);
        FriendsCommand = Command(() => { OpenShell("steam://open/friends"); return Task.CompletedTask; }, () => true);
        ImportCommand = Command(ImportAsync, () => !IsWorking);
        RetryCommand = Command(() => RunEngine(() => engine!.RecoverAsync(lifetime.Token)), () => engine is not null && !IsWorking);
        ExportCommand = Command(ExportAsync, () => configuration is not null && !IsWorking);
        UseCloudCommand = Command(UseCloudAsync, () => engine is not null && !IsWorking && State is SyncState.Conflict or SyncState.Pending or SyncState.Error);
        RecoveryCommand = Command(() => { Directory.CreateDirectory(this.dataRoot); OpenShell(this.dataRoot); return Task.CompletedTask; }, () => true);
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timer.Tick += OnTimer;
    }
    public async Task InitializeAsync()
    {
        if (IsWorking) return;
        localWork = true; Refresh();
        try
        {
            repository?.Dispose(); repository = null; engine = null;
            configuration = await AppConfiguration.LoadAsync(configPath, createTemplate: true);
            WorldLabel = string.IsNullOrWhiteSpace(configuration.WorldFolderName) ? "NENHUM MUNDO CONFIGURADO" : configuration.WorldFolderName;
            configuration.Validate();
            repository = new(configuration);
            var game = new PollingGameSession(new WindowsGamePlatform());
            engine = new(repository, new WorldArchive(dataRoot), new FileSessionJournal(dataRoot), game,
                new(configuration.WorldId, configuration.WorldPath, configuration.Endpoint.TrimEnd('/') + "/" + configuration.Bucket,
                    dataRoot, configuration.Player, configuration.InstallationId, configuration.BackupCount));
            engine.StatusChanged += OnStatus;
        }
        catch (Exception e) { Error(e); }
        finally { localWork = false; Refresh(); timer.Start(); }
        if (engine is not null) await RunEngine(() => engine.RecoverAsync(lifetime.Token));
    }
    private async void OnTimer(object? sender, EventArgs args)
    {
        if (IsWorking || engine is null || State is SyncState.Conflict or SyncState.Error) return;
        try { await RunEngine(() => engine.RecoverAsync(lifetime.Token)); }
        catch (Exception e) { Error(e); }
    }
    private async Task RunEngine(Func<Task> action)
    {
        if (IsWorking) return;
        localWork = true; Refresh();
        try { await Task.Run(action, lifetime.Token); }
        finally { localWork = false; Refresh(); }
    }
    private void OnStatus(SyncStatus status)
    {
        if (dispatcher.CheckAccess()) ApplyStatus(status);
        else dispatcher.BeginInvoke(() => ApplyStatus(status));
    }
    private void ApplyStatus(SyncStatus status)
    {
        State = status.State;
        StatusTitle = status.State switch
        {
            SyncState.Idle => "Pronto para a próxima partida",
            SyncState.Checking => "Verificando o mundo",
            SyncState.Acquiring => "Reservando o mundo",
            SyncState.Downloading => "Baixando seu mundo",
            SyncState.Preparing => "Preparando o save",
            SyncState.Launching => "Abrindo o Valheim",
            SyncState.Playing => "Boa aventura",
            SyncState.LocalBackup => "Guardando seu progresso",
            SyncState.Uploading or SyncState.Publishing or SyncState.Releasing => "Sincronizando",
            SyncState.InUse => "Já existe um anfitrião",
            SyncState.Offline => "Sem conexão confirmada",
            SyncState.Pending => "Sincronização pendente",
            SyncState.Conflict => "Progresso precisa de atenção",
            _ => "Verifique a configuração"
        };
        Message = status.Message;
        StatusUpdated?.Invoke(status);
        Refresh();
    }
    private async Task OpenConfiguration()
    {
        if (!File.Exists(configPath)) await AppConfiguration.LoadAsync(configPath, createTemplate: true);
        var start = new ProcessStartInfo("notepad.exe") { UseShellExecute = true };
        start.ArgumentList.Add(configPath);
        using var editor = Process.Start(start);
        Message = "Edite o arquivo, salve e clique em Recarregar. Não compartilhe as credenciais publicamente.";
    }
    private async Task ImportAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Escolha a pasta completa de um mundo local do Valheim 1.0" };
        if (dialog.ShowDialog() != true) return;
        if (File.Exists(Path.Combine(dataRoot, "session.json"))) throw new InvalidDataException("Resolva a sessão pendente antes de alterar o mundo.");
        var folder = new DirectoryInfo(dialog.FolderName);
        if (MessageBox.Show($"Publicar a pasta '{folder.Name}' como primeiro mundo do bucket?\n\nO Valheim deve estar fechado. Mundos antigos precisam ser convertidos no jogo antes da importação.",
            "Importar mundo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        configuration = await AppConfiguration.LoadAsync(configPath, createTemplate: true);
        configuration = configuration with { SavesRoot = folder.Parent!.FullName, WorldFolderName = folder.Name };
        configuration.Validate();
        await DurableJson.WriteAsync(configPath, configuration);
        await InitializeAsync();
        if (engine is not null) await RunEngine(() => engine.ImportAsync(lifetime.Token));
    }
    private async Task ExportAsync()
    {
        if (new WindowsGamePlatform().FindProcesses().Count != 0) throw new IOException("Feche o Valheim antes de exportar.");
        var dialog = new SaveFileDialog { Title = "Exportar progresso para recuperação", Filter = "Snapshot ZIP|*.zip", FileName = $"valheim-recuperacao-{DateTime.Now:yyyyMMdd-HHmmss}.zip" };
        if (dialog.ShowDialog() != true) return;
        localWork = true; Refresh();
        try
        {
            await Task.Run(async () =>
            {
                var journal = new FileSessionJournal(dataRoot);
                var session = await journal.ReadAsync(lifetime.Token);
                var archive = new WorldArchive(dataRoot);
                var snapshot = session?.Snapshot ?? await archive.CreateAsync(configuration!.WorldPath, lifetime.Token);
                await archive.VerifyAsync(snapshot, lifetime.Token);
                if (string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(snapshot.Path), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Escolha um destino fora do snapshot original.");
                File.Copy(snapshot.Path, dialog.FileName, true);
            }, lifetime.Token);
            Message = "Progresso exportado. A pendência original continua preservada.";
        }
        finally { localWork = false; Refresh(); }
    }
    private async Task UseCloudAsync()
    {
        if (MessageBox.Show("Guardar uma cópia local e encerrar esta pendência?\n\nO próximo Jogar baixará a versão da nuvem. As alterações locais NÃO serão mescladas. Você poderá recuperar a cópia pela pasta Recuperação.",
            "Voltar à versão da nuvem", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            await RunEngine(() => engine!.KeepLocalAndUseCloudAsync(lifetime.Token));
    }
    private AsyncCommand Command(Func<Task> action, Func<bool> canExecute)
    {
        var command = new AsyncCommand(action, canExecute, Error);
        commands.Add(command);
        return command;
    }
    private void Error(Exception e)
    {
        ApplyStatus(new(SyncState.Error, e is InvalidDataException or IOException && e is not FileNotFoundException
            ? e.Message : $"Não foi possível concluir ({e.GetType().Name}). Abra Configuração e verifique os campos."));
    }
    private static void OpenShell(string target) { using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
    private void Refresh()
    {
        Changed(nameof(IsWorking)); Changed(nameof(CanExit));
        foreach (var command in commands) command.Refresh();
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose()
    {
        timer.Stop(); lifetime.Cancel(); repository?.Dispose(); lifetime.Dispose();
    }
}
