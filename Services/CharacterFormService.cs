namespace CrossingVoidZDTool.Services;

internal sealed class CharacterFormService
{
    private readonly CharacterInfoService _characterInfoService = new();

    public int GetFormLimit(CharacterCard character)
    {
        return System.Math.Max(1, _characterInfoService.Load(character).FormLimit);
    }
}
