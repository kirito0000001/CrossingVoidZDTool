using System.Text.Json.Serialization;

namespace CrossingVoidZDTool.Services;

[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}
