using System.Collections.Generic;

namespace CrossingVoidZDTool.Services;

internal sealed class CharacterInfoService
{
    private readonly CharacterToolboxDataService _toolboxDataService = new();

    public CharacterInfoData Load(CharacterCard character)
    {
        var toolboxData = _toolboxDataService.Load(character);
        return Normalize(character, toolboxData.CharacterInfo);
    }

    public void Save(CharacterCard character, CharacterInfoData data)
    {
        _toolboxDataService.Update(character, toolboxData =>
        {
            toolboxData.CharacterInfo = Normalize(character, data);
            toolboxData.CharacterInfo.UpdatedAt = System.DateTime.Now;
        });
    }

    public IReadOnlyList<BaseMaterialSection> LoadMaterialSections(CharacterCard character)
    {
        return new BaseMaterialService().LoadSections(character);
    }

    private static CharacterInfoData Normalize(CharacterCard character, CharacterInfoData? data)
    {
        data ??= CreateDefault(character);
        data.Code = character.Code;

        if (string.IsNullOrWhiteSpace(data.Name))
        {
            data.Name = character.Name;
        }

        data.FormLimit = System.Math.Max(1, data.FormLimit);
        data.KeywordTags ??= [];
        data.PassiveSkills ??= [];
        return data;
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
