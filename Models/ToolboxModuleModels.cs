namespace CrossingVoidZDTool;

internal enum ToolboxModuleKey
{
    CharacterDesk,
    ActionFrames,
    LineArt,
    UnrealSync,
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
