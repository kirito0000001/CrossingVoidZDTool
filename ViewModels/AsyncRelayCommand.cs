using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CrossingVoidZDTool.ViewModels;

public sealed class AsyncRelayCommand : ObservableObject, ICommand
{
    private readonly Func<object?, CancellationToken, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private readonly bool _allowsConcurrentExecutions;
    private bool _isRunning;
    private CancellationTokenSource? _cancellationTokenSource;

    public AsyncRelayCommand(
        Func<Task> execute,
        Func<bool>? canExecute = null,
        bool allowsConcurrentExecutions = false)
        : this(
            (_, _) => execute(),
            canExecute is null ? null : _ => canExecute(),
            allowsConcurrentExecutions)
    {
    }

    public AsyncRelayCommand(
        Func<CancellationToken, Task> execute,
        Func<bool>? canExecute = null,
        bool allowsConcurrentExecutions = false)
        : this(
            (_, cancellationToken) => execute(cancellationToken),
            canExecute is null ? null : _ => canExecute(),
            allowsConcurrentExecutions)
    {
    }

    public AsyncRelayCommand(
        Func<object?, Task> execute,
        Predicate<object?>? canExecute = null,
        bool allowsConcurrentExecutions = false)
        : this(
            (parameter, _) => execute(parameter),
            canExecute,
            allowsConcurrentExecutions)
    {
    }

    public AsyncRelayCommand(
        Func<object?, CancellationToken, Task> execute,
        Predicate<object?>? canExecute = null,
        bool allowsConcurrentExecutions = false)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _allowsConcurrentExecutions = allowsConcurrentExecutions;
    }

    public event EventHandler? CanExecuteChanged;

    public event EventHandler<Exception>? ExecutionFailed;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanExecute(object? parameter)
    {
        if (!_allowsConcurrentExecutions && IsRunning)
        {
            return false;
        }

        return _canExecute?.Invoke(parameter) ?? true;
    }

    public async void Execute(object? parameter)
    {
        await ExecuteAsync(parameter);
    }

    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        using var cancellationTokenSource = new CancellationTokenSource();
        _cancellationTokenSource = cancellationTokenSource;
        IsRunning = true;

        try
        {
            await _execute(parameter, cancellationTokenSource.Token);
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ExecutionFailed?.Invoke(this, ex);
        }
        finally
        {
            if (ReferenceEquals(_cancellationTokenSource, cancellationTokenSource))
            {
                _cancellationTokenSource = null;
            }

            IsRunning = false;
        }
    }

    public void Cancel()
    {
        _cancellationTokenSource?.Cancel();
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
