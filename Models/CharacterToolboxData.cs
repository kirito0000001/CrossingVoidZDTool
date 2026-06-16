using System;

namespace CrossingVoidZDTool;

internal sealed class CharacterToolboxData
{
    public CharacterDraftData? Draft { get; set; }

    public CharacterInfoData? CharacterInfo { get; set; }

    public CharacterSkillsData? Skills { get; set; }

    public BuffData? Buffs { get; set; }

    public SequenceFramesData? SequenceFrames { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

internal sealed class CharacterDraftData
{
    public string Text { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
