using System.Collections.Generic;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class ApplicationViewModel : ObservableObject
{
    private ToolboxModuleKey _selectedModule = ToolboxModuleKey.CharacterDesk;

    public ApplicationViewModel(SettingsViewModel settings, GlobalProgressViewModel globalProgress)
    {
        Settings = settings;
        GlobalProgress = globalProgress;
        CharacterDesk = new CharacterDeskViewModel();
        ActionFrames = new ActionFramesViewModel();
        LineArt = new LineArtViewModel();
        UnrealSync = new UnrealSyncViewModel();
    }

    public SettingsViewModel Settings { get; }

    public GlobalProgressViewModel GlobalProgress { get; }

    public CharacterDeskViewModel CharacterDesk { get; }

    public ActionFramesViewModel ActionFrames { get; }

    public LineArtViewModel LineArt { get; }

    public UnrealSyncViewModel UnrealSync { get; }

    public IReadOnlyList<ToolboxModuleDefinition> Modules { get; } =
    [
        new(ToolboxModuleKey.CharacterDesk, "CharacterDesk", "零境角色台", ToolboxModuleCategory.Character, "角色、动作和帧处理入口"),
        new(ToolboxModuleKey.ActionFrames, "ActionFrames", "动作帧", ToolboxModuleCategory.Frame, "截图序列导入、帧序检查和动作帧整理"),
        new(ToolboxModuleKey.LineArt, "LineArt", "线稿处理", ToolboxModuleCategory.ImageProcessing, "批量边线提取、线稿预览和输出管理"),
        new(ToolboxModuleKey.UnrealSync, "UnrealSync", "虚幻同步", ToolboxModuleCategory.Integration, "角色素材、动作帧和 Unreal 目标目录同步"),
        new(ToolboxModuleKey.Settings, "Settings", "整体设置", ToolboxModuleCategory.Settings, "全局路径和工具箱偏好")
    ];

    public ToolboxModuleKey SelectedModule
    {
        get => _selectedModule;
        set => SetProperty(ref _selectedModule, value);
    }

    public ToolboxModuleDefinition? FindModuleByTag(string tag)
    {
        foreach (var module in Modules)
        {
            if (string.Equals(module.Tag, tag, System.StringComparison.Ordinal))
            {
                return module;
            }
        }

        return null;
    }
}
