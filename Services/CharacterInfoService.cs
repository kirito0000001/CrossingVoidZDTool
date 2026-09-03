using System.Collections.Generic;
using System.Linq;

namespace CrossingVoidZDTool.Services;

internal sealed class CharacterInfoService
{
    private readonly CharacterToolboxDataService _toolboxDataService = new();

    public CharacterInfoData Load(CharacterCard character)
    {
        var toolboxData = _toolboxDataService.Load(character);
        var data = Normalize(character, toolboxData.CharacterInfo, out var migratedLegacyTags);
        if (migratedLegacyTags)
        {
            data.UpdatedAt = System.DateTime.Now;
            _toolboxDataService.Update(character, current => current.CharacterInfo = data);
        }

        return data;
    }

    public void Save(CharacterCard character, CharacterInfoData data)
    {
        _toolboxDataService.Update(character, toolboxData =>
        {
            toolboxData.CharacterInfo = Normalize(character, data, out _);
            toolboxData.CharacterInfo.UpdatedAt = System.DateTime.Now;
        });
    }

    public IReadOnlyList<BaseMaterialSection> LoadMaterialSections(CharacterCard character)
    {
        return new BaseMaterialService().LoadSections(character);
    }

    private static CharacterInfoData Normalize(
        CharacterCard character,
        CharacterInfoData? data,
        out bool migratedLegacyTags)
    {
        data ??= CreateDefault(character);
        data.Code = character.Code;

        if (string.IsNullOrWhiteSpace(data.Name))
        {
            data.Name = character.Name;
        }

        data.FormLimit = System.Math.Max(1, data.FormLimit);
        data.KeywordTags ??= [];
        data.KeywordTagGroups ??= new CharacterKeywordTagGroups();
        data.KeywordTagGroups.Works ??= [];
        data.KeywordTagGroups.Periods ??= [];
        data.KeywordTagGroups.AbilityTypes ??= [];
        data.KeywordTagGroups.Affiliations ??= [];
        data.KeywordTagGroups.Aliases ??= [];
        data.KeywordTagGroups.LegacyKeywordTags ??= [];
        migratedLegacyTags = MigrateLegacyKeywordTags(data);

        data.PassiveSkills ??= [];
        return data;
    }

    private static bool MigrateLegacyKeywordTags(CharacterInfoData data)
    {
        var groups = data.KeywordTagGroups!;
        var knownTags = new[] { data.Code, data.Name }
            .Concat(groups.Works)
            .Concat(groups.Periods)
            .Concat(groups.AbilityTypes)
            .Concat(groups.Affiliations)
            .Concat(groups.Aliases)
            .Select(value => (value ?? string.Empty).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var legacyTags = groups.LegacyKeywordTags
            .Concat(data.KeywordTags.Where(tag => !knownTags.Contains((tag ?? string.Empty).Trim())))
            .Select(value => (value ?? string.Empty).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hadLegacyData = groups.LegacyKeywordTags.Count > 0 || legacyTags.Length > 0;
        if (!hadLegacyData)
        {
            return false;
        }

        var aliases = groups.Aliases
            .Select(value => (value ?? string.Empty).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        foreach (var tag in legacyTags)
        {
            if (aliases.Add(tag))
            {
                groups.Aliases.Add(tag);
            }
        }

        groups.LegacyKeywordTags.Clear();
        data.KeywordTags.Clear();
        foreach (var tag in CharacterKeywordTagRules.Flatten(data.Code, data.Name, groups))
        {
            data.KeywordTags.Add(tag);
        }

        return true;
    }

    private static CharacterInfoData CreateDefault(CharacterCard character)
    {
        return new CharacterInfoData
        {
            Code = character.Code,
            Name = character.Name,
            FormLimit = 1,
            PassiveSkills = { string.Empty }
        };
    }
}
