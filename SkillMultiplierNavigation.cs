namespace CrossingVoidZDTool;

internal enum SkillMultiplierColumn
{
    Physical,
    Energy
}

internal enum SkillMultiplierMoveDirection
{
    Up,
    Down,
    Left,
    Right
}

internal readonly record struct SkillMultiplierCell(int Level, SkillMultiplierColumn Column);

internal static class SkillMultiplierNavigation
{
    public static SkillMultiplierCell? GetTarget(
        SkillMultiplierCell current,
        SkillMultiplierMoveDirection direction,
        int minimumLevel,
        int maximumLevel)
    {
        return direction switch
        {
            SkillMultiplierMoveDirection.Up when current.Level > minimumLevel =>
                current with { Level = current.Level - 1 },
            SkillMultiplierMoveDirection.Down when current.Level < maximumLevel =>
                current with { Level = current.Level + 1 },
            SkillMultiplierMoveDirection.Left when current.Column == SkillMultiplierColumn.Energy =>
                current with { Column = SkillMultiplierColumn.Physical },
            SkillMultiplierMoveDirection.Right when current.Column == SkillMultiplierColumn.Physical =>
                current with { Column = SkillMultiplierColumn.Energy },
            _ => null
        };
    }
}
