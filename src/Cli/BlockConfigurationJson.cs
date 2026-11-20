using System.Text.Json.Serialization;

namespace StatusLine.Cli;

[JsonSourceGenerationOptions(UseStringEnumConverter = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BlockSettings))]
[JsonSerializable(typeof(StatusLineState))]
[JsonSerializable(typeof(PromptOptions))]
internal sealed partial class BlockConfigurationJson : JsonSerializerContext;
