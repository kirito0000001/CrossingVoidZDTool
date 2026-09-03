using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool.Services;

internal sealed class ProductionStatusService
{
    private readonly BaseMaterialService _baseMaterialService = new();
    private readonly VoiceMaterialService _voiceMaterialService = new();
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
        var missingBase = baseSections.Sum(section => section.MissingCount);
        var invalidBase = baseSections.Sum(section => section.InvalidCount);
        var extraBase = baseSections.Sum(section => section.ExtraCount);
        var voiceSections = _voiceMaterialService.LoadSections(character);
        var missingVoices = voiceSections.Sum(section => section.MissingCount);
        var invalidVoices = voiceSections.Sum(section => section.InvalidCount);

        var buffs = _buffService.Load(character).Buffs;
        var buffMissingIcon = buffs.Count(buff => string.IsNullOrWhiteSpace(buff.IconPath));

        var skills = _skillsService.Load(character);
        var sequenceSections = _sequenceFrameService.LoadSections(character, skills);
        var sequenceWarnings = sequenceSections.Count(section => section.HasWarning);
        var issues = new List<string>();
        AddIssue(issues, missingBase, count => $"St2 图片缺少 {count} 张");
        AddIssue(issues, invalidBase, count => $"St2 图片不合规 {count} 个");
        AddIssue(issues, extraBase, count => $"St2 额外图片 {count} 张");
        AddIssue(issues, missingVoices, count => $"必填语音缺少 {count} 个");
        AddIssue(issues, invalidVoices, count => $"语音不合规 {count} 个");
        AddIssue(issues, sequenceWarnings, count => $"St5 序列帧待处理动作 {count} 个");
        AddIssue(issues, buffMissingIcon, count => $"St6 缺图标 BUFF {count} 个");
        var issueMessage = string.Join("；", issues);

        if (missingBase > 0 || invalidBase > 0 || extraBase > 0 || missingVoices > 0 || invalidVoices > 0)
        {
            return new ProductionStatus(
                InfoBarSeverity.Warning,
                "角色制作未完成",
                $"{issueMessage}。",
                false,
                false);
        }

        var completionMessage = issues.Count > 0
            ? $"St2 基础素材已正确设置。{issueMessage}。可以先完成制作，后续仍可从角色台继续编辑。"
            : "St2 基础素材已正确设置。可以先完成制作，后续仍可从角色台继续编辑。";
        return new ProductionStatus(
            buffMissingIcon > 0 || sequenceWarnings > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
            "基础素材已通过",
            completionMessage,
            true,
            false);
    }

    private static void AddIssue(List<string> issues, int count, System.Func<int, string> format)
    {
        if (count > 0)
        {
            issues.Add(format(count));
        }
    }
}
