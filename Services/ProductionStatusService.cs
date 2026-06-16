using System.Linq;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.Services;

internal sealed class ProductionStatusService
{
    private readonly BaseMaterialService _baseMaterialService = new();
    private readonly BuffService _buffService = new();
    private readonly CharacterSkillsService _skillsService = new();
    private readonly SequenceFrameService _sequenceFrameService = new();

    public ProductionStatus Evaluate(CharacterCard? character)
    {
        if (character is null)
        {
            return new ProductionStatus(
                InfoBarSeverity.Informational,
                "未选择角色",
                "请先在零境角色台选择当前制作角色。",
                false,
                false);
        }

        if (character.IsCompleted)
        {
            return new ProductionStatus(
                InfoBarSeverity.Success,
                "角色已完成",
                "这个角色已经从 St1 草稿立绘入口移除。后续从角色台选择角色后继续编辑。",
                false,
                true);
        }

        var baseSections = _baseMaterialService.LoadSections(character);
        var missingBase = baseSections.Sum(section => System.Math.Max(0, section.Spec.MinimumCount - section.Items.Count));
        var invalidBase = baseSections.Sum(section => section.Items.Count(item => item.Status == BaseMaterialStatus.Invalid));

        var buffs = _buffService.Load(character).Buffs;
        var buffMissingIcon = buffs.Count(buff => string.IsNullOrWhiteSpace(buff.IconPath));

        var skills = _skillsService.Load(character);
        var sequenceSections = _sequenceFrameService.LoadSections(character, skills);
        var sequenceWarnings = sequenceSections.Count(section => section.HasWarning);

        if (missingBase > 0 || invalidBase > 0)
        {
            return new ProductionStatus(
                InfoBarSeverity.Warning,
                "角色制作未完成",
                $"St2 基础素材缺少 {missingBase} 张，不合规素材 {invalidBase} 个。St5 序列帧待处理动作 {sequenceWarnings} 个，St6 缺图标 BUFF {buffMissingIcon} 个。",
                false,
                false);
        }

        return new ProductionStatus(
            buffMissingIcon > 0 || sequenceWarnings > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
            "基础素材已通过",
            $"St2 基础素材已正确设置。St5 序列帧待处理动作 {sequenceWarnings} 个，St6 缺图标 BUFF {buffMissingIcon} 个。可以先完成制作，后续仍可从角色台继续编辑。",
            true,
            false);
    }
}
