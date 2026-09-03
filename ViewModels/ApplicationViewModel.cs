using System.Collections.Generic;
using CrossingVoidZDTool.Services;

namespace CrossingVoidZDTool.ViewModels;

internal sealed class ApplicationViewModel : ObservableObject
{
    private ToolboxModuleKey _selectedModule = ToolboxModuleKey.CharacterDesk;

    public ApplicationViewModel(
        SettingsViewModel settings,
        GlobalProgressViewModel globalProgress,
        BaseMaterialService baseMaterialService,
        VoiceMaterialService voiceMaterialService)
    {
        Settings = settings;
        GlobalProgress = globalProgress;
        UserOperations = new UserOperationHistoryViewModel();
        ProductionStatus = new ProductionStatusViewModel();
        CharacterDesk = new CharacterDeskViewModel(new CharacterWorkspaceService());
        ActionFrames = new ActionFramesViewModel();
        LineArt = new LineArtViewModel(baseMaterialService, voiceMaterialService);
        UnrealSync = new UnrealSyncViewModel(new CharacterInfoService());
        Skills = new SkillsViewModel(new CharacterSkillsService());
        SequenceFrames = new SequenceFramesViewModel(new SequenceFrameService(), new CharacterSkillsService());
        Buffs = new BuffsViewModel(new BuffService());
        UnrealProjectSync = new UnrealProjectSyncViewModel(new UnrealProjectSyncService());
    }

    public SettingsViewModel Settings { get; }

    public GlobalProgressViewModel GlobalProgress { get; }

    public UserOperationHistoryViewModel UserOperations { get; }

    public ProductionStatusViewModel ProductionStatus { get; }

    public CharacterDeskViewModel CharacterDesk { get; }

    public ActionFramesViewModel ActionFrames { get; }

    public LineArtViewModel LineArt { get; }

    public UnrealSyncViewModel UnrealSync { get; }

    public SkillsViewModel Skills { get; }

    public SequenceFramesViewModel SequenceFrames { get; }

    public BuffsViewModel Buffs { get; }

    public UnrealProjectSyncViewModel UnrealProjectSync { get; }

    public IReadOnlyList<ToolboxModuleDefinition> Modules { get; } =
    [
        new(ToolboxModuleKey.CharacterDesk, "CharacterDesk", "零境角色台", ToolboxModuleCategory.Character, "角色卡总览、创建和当前制作角色选择"),
        new(ToolboxModuleKey.ActionFrames, "ActionFrames", "St1-设计理念", ToolboxModuleCategory.Frame, "当前角色立绘入口、设计草稿和参考图"),
        new(ToolboxModuleKey.LineArt, "LineArt", "St2-基础素材", ToolboxModuleCategory.ImageProcessing, "立绘、技能图、序列帧、声音等基础素材入口"),
        new(ToolboxModuleKey.UnrealSync, "UnrealSync", "St3-角色信息", ToolboxModuleCategory.Integration, "角色信息、编号和后续制作规则入口"),
        new(ToolboxModuleKey.Skills, "Skills", "St4-技能", ToolboxModuleCategory.Character, "角色技能、倍率、状态和连携技"),
        new(ToolboxModuleKey.SequenceFrames, "SequenceFrames", "St5-序列帧", ToolboxModuleCategory.Frame, "角色基础动作和技能序列帧"),
        new(ToolboxModuleKey.Buffs, "Buffs", "St6-BUFF", ToolboxModuleCategory.Character, "角色特殊效果和 BUFF 草稿"),
        new(ToolboxModuleKey.UnrealProjectSync, "UnrealProjectSync", "虚幻同步台", ToolboxModuleCategory.Integration, "检测虚幻引擎、项目和目标内容目录"),
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
