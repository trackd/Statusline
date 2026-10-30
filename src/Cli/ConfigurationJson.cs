using System.Text.Json.Serialization;

namespace StatusLine.Cli;

[JsonSourceGenerationOptions(UseStringEnumConverter = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PromptOptions))]
[JsonSerializable(typeof(SessionRequest))]
[JsonSerializable(typeof(SessionResponse))]
[JsonSerializable(typeof(SessionOwner))]
internal sealed partial class ConfigurationJson : JsonSerializerContext;
