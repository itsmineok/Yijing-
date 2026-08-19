using System.Windows.Input;

namespace Yijing.Desktop.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    private readonly Func<object?, bool>? _canExecute;
    private int _isExecuting;

    public AsyncRelayCommand(Func<object?, Task> executeAsync, Func<object?, bool>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public event EventHandler<Exception>? ExecutionFailed;

    public bool CanExecute(object? parameter) => Volatile.Read(ref _isExecuting) == 0 &&
        (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        try
        {
            await ExecuteAsync(parameter);
        }
        catch (Exception exception)
        {
            ExecutionFailed?.Invoke(this, exception);
        }
    }

    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0) return;
        try
        {
            NotifyCanExecuteChanged();
            await _executeAsync(parameter);
        }
        finally
        {
            Volatile.Write(ref _isExecuting, 0);
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
