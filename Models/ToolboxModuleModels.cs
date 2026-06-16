namespace CrossingVoidZDTool;

internal enum ToolboxModuleKey
{
    CharacterDesk,
    ActionFrames,
    LineArt,
    UnrealSync,
    Skills,
    SequenceFrames,
    Buffs,
    UnrealProjectSync,
    Settings
}

internal enum ToolboxModuleCategory
{
    Shell,
    Character,
    Frame,
    ImageProcessing,
    Integration,
    Settings
}

internal sealed record ToolboxModuleDefinition(
    ToolboxModuleKey Key,
    string Tag,
    string DisplayName,
    ToolboxModuleCategory Category,
    string Description);
