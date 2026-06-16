using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class UserOperationHistoryViewModel : ObservableObject
{
    private const int MaxOperationCount = 100;
    private UserOperationEntry? _lastOperation;
    private UserOperationEntry? _lastUndoableOperation;

    public ObservableCollection<UserOperationEntry> Operations { get; } = [];

    public UserOperationEntry? LastOperation
    {
        get => _lastOperation;
        private set
        {
            if (SetProperty(ref _lastOperation, value))
            {
                OnPropertyChanged(nameof(LastOperationModuleTag));
            }
        }
    }

    public UserOperationEntry? LastUndoableOperation
    {
        get => _lastUndoableOperation;
        private set
        {
            if (SetProperty(ref _lastUndoableOperation, value))
            {
                OnPropertyChanged(nameof(CanUndoLastOperation));
            }
        }
    }

    public string? LastOperationModuleTag => LastOperation?.ModuleTag;

    public bool CanUndoLastOperation => LastUndoableOperation is not null;

    public UserOperationEntry Record(
        ToolboxModuleDefinition module,
        string actionName,
        string description,
        string characterCode,
        Func<Task<bool>>? undoAsync = null)
    {
        var entry = new UserOperationEntry(
            Guid.NewGuid(),
            module.Key,
            module.Tag,
            actionName,
            description,
            characterCode,
            DateTime.Now,
            undoAsync);
        Operations.Insert(0, entry);
        while (Operations.Count > MaxOperationCount)
        {
            Operations.RemoveAt(Operations.Count - 1);
        }

        LastOperation = entry;
        LastUndoableOperation = Operations.FirstOrDefault(operation => operation.CanUndo);
        return entry;
    }

    public async Task<bool> UndoLastOperationAsync()
    {
        var operation = LastUndoableOperation;
        if (operation?.UndoAsync is null)
        {
            return false;
        }

        if (!await operation.UndoAsync())
        {
            return false;
        }

        Operations.Remove(operation);
        LastOperation = Operations.FirstOrDefault();
        LastUndoableOperation = Operations.FirstOrDefault(item => item.CanUndo);
        return true;
    }
}
