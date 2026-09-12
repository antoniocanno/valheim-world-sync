using System.Windows.Input;
namespace ValheimWorldSync.Desktop.ViewModels;

public sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute, Action<Exception> onError) : ICommand
{
    private bool running;
    public bool CanExecute(object? parameter) => !running && canExecute();
    public event EventHandler? CanExecuteChanged;
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        running = true; Refresh();
        try { await execute(); }
        catch (Exception e) { onError(e); }
        finally { running = false; Refresh(); }
    }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
