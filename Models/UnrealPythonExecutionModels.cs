using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CrossingVoidZDTool;

internal sealed class UnrealRemotePythonJob
{
    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; set; } = 1;

    [JsonPropertyName("engineRoot")]
    public string EngineRoot { get; set; } = string.Empty;

    [JsonPropertyName("projectPath")]
    public string ProjectPath { get; set; } = string.Empty;

    [JsonPropertyName("scriptPath")]
    public string ScriptPath { get; set; } = string.Empty;

    [JsonPropertyName("environment")]
    public Dictionary<string, string> Environment { get; set; } = new();
}
