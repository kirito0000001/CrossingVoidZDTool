using System;
using System.Collections.Generic;
using System.Linq;

namespace CrossingVoidZDTool.Services;

internal static class UnrealBridgeVoiceClassification
{
    private static readonly IReadOnlyList<(VoiceMaterialKind Kind, string[] Tokens)> Rules =
    [
        (VoiceMaterialKind.Formation, ["formation", "team", "deploy", "voselect"]),
        (VoiceMaterialKind.Click, ["click", "touch"]),
        (VoiceMaterialKind.Hurt, ["hurt", "damage", "ondm", "odnm"]),
        (VoiceMaterialKind.Death, ["death", "dead"]),
        (VoiceMaterialKind.Defeat, ["defeat", "lose", "failure"]),
        (VoiceMaterialKind.Victory, ["victory", "win"])
    ];

    public static VoiceMaterialKind Classify(string packagePath, string assetName)
    {
        var searchable = $"/{packagePath}/{assetName}/"
            .Replace('\\', '/')
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        foreach (var rule in Rules)
        {
            if (rule.Tokens.Any(token => searchable.Contains(token, StringComparison.Ordinal)))
            {
                return rule.Kind;
            }
        }

        return VoiceMaterialKind.Other;
    }

    public static VoiceMaterialKind ClassifySequenceAction(UnrealProjectSyncSequenceActionPreview action)
    {
        if (action.Category.Equals("link", StringComparison.OrdinalIgnoreCase))
        {
            return VoiceMaterialKind.Combo;
        }

        return action.ActionCode.Trim().ToLowerInvariant() switch
        {
            "click" => VoiceMaterialKind.Click,
            "death" or "dead" => VoiceMaterialKind.Death,
            "defeat" => VoiceMaterialKind.Defeat,
            "ondm" or "ondamage" => VoiceMaterialKind.Hurt,
            "victory" => VoiceMaterialKind.Victory,
            "sk1" => VoiceMaterialKind.Skill1,
            "sk2" => VoiceMaterialKind.Skill2,
            "ko" => VoiceMaterialKind.Ultimate,
            "sub" => VoiceMaterialKind.Support,
            _ => VoiceMaterialKind.Other
        };
    }
}
