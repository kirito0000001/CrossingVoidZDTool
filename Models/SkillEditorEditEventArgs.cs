namespace CrossingVoidZDTool;

public sealed class SkillEditorEditEventArgs(string fieldName, string value, int? multiplierLevel = null)
{
    public string FieldName { get; } = fieldName;

    public string Value { get; } = value;

    public int? MultiplierLevel { get; } = multiplierLevel;
}
