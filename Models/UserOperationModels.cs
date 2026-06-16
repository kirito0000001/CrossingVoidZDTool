using System;
using System.Threading.Tasks;

namespace CrossingVoidZDTool;

internal sealed record UserOperationEntry(
    Guid Id,
    ToolboxModuleKey ModuleKey,
    string ModuleTag,
    string ActionName,
    string Description,
    string CharacterCode,
    DateTime CreatedAt,
    Func<Task<bool>>? UndoAsync)
{
    public bool CanUndo => UndoAsync is not null;
}
