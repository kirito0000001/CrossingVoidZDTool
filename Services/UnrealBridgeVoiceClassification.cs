using System;
using System.Collections.Generic;
using System.Linq;

namespace CrossingVoidZDTool.Services;

internal static class UnrealBridgeVoiceClassification
{
    private static readonly IReadOnlyList<(VoiceMaterialKind Kind, string[] Tokens)> Rules =
    [
        (VoiceMaterialKind.Skill1, ["skill1", "sk1"]),
        (VoiceMaterialKind.Skill2, ["skill2", "sk2"]),
        (VoiceMaterialKind.Ultimate, ["ultimate", "ko"]),
        (VoiceMaterialKind.Support, ["support", "sub"]),
        (VoiceMaterialKind.Combo, ["combo", "link"]),
        (VoiceMaterialKind.Formation, ["formation", "team", "deploy", "voselect"]),
        (VoiceMaterialKind.Click, ["click", "touch"]),
        (VoiceMaterialKind.Hurt, ["hurt", "damage", "ondm", "odnm"]),
        (VoiceMaterialKind.Death, ["death", "dead"]),
        // fail / lost 是照着工程里实际出现过的命名补的：以前只有 failure，
        // Vo_Fail1 这种识别不出来，会掉进待分配。
        (VoiceMaterialKind.Defeat, ["defeat", "lose", "lost", "fail", "failure"]),
        (VoiceMaterialKind.Victory, ["victory", "win"]),
        (VoiceMaterialKind.SoundEffect, ["soundeffect", "se"])
    ];

    /// <summary>
    /// 这些 token 太短，做子串匹配会被角色代号误伤：代号里带 ko 的角色
    /// （Kokona、Nakoruru）整批语音都会被判成终结技，而它排在失败语音前面。
    /// 对它们要求前后是分隔符或字符串边界。
    /// </summary>
    private static readonly HashSet<string> BoundaryTokens =
        new(StringComparer.Ordinal) { "ko", "sub", "se", "sk1", "sk2", "win", "team", "link" };

    public static VoiceMaterialKind Classify(string packagePath, string assetName)
    {
        var normalized = $"/{packagePath}/{assetName}/".Replace('\\', '/').ToLowerInvariant();
        // 去掉分隔符的那一份用于长 token 的宽松匹配（Vo_Sound_Effect 也能命中）；
        // 保留分隔符的那一份用于短 token 的边界匹配。
        var collapsed = normalized
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        foreach (var rule in Rules)
        {
            if (rule.Tokens.Any(token => BoundaryTokens.Contains(token)
                    ? ContainsAtBoundary(normalized, token)
                    : collapsed.Contains(token, StringComparison.Ordinal)))
            {
                return rule.Kind;
            }
        }

        return VoiceMaterialKind.Other;
    }

    /// <summary>token 前后必须是非字母数字，避免命中角色代号里的字母。</summary>
    private static bool ContainsAtBoundary(string text, string token)
    {
        for (var at = text.IndexOf(token, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(token, at + 1, StringComparison.Ordinal))
        {
            var beforeOk = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
            var afterAt = at + token.Length;
            var afterOk = afterAt >= text.Length || !char.IsLetterOrDigit(text[afterAt]);
            if (beforeOk && afterOk)
            {
                return true;
            }
        }

        return false;
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
