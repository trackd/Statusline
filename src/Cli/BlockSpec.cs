using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatusLine.Cli;

[JsonConverter(typeof(BlockSpecConverter))]
internal sealed class BlockSpec(BlockSettings settings) {
    internal BlockSettings Settings { get; } = settings;
    public static implicit operator BlockSpec(string kind) => new(new() { Kind = kind });
}

internal sealed record BlockSettings {
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public string? Executable { get; set; }
    public string[] Arguments { get; set; } = [];
    public string[] Commands { get; set; } = [];
    public string[] DetectFiles { get; set; } = [];
    public int MinimumWidth { get; set; }
    public int MaximumWidth { get; set; }
    public int Priority { get; set; }
    public int MinimumDurationMilliseconds { get; set; }
    public bool SshOnly { get; set; }
    public string? Icon { get; set; }
    public string? Foreground { get; set; }
    public string? Background { get; set; }
    public string? Padding { get; set; }
    public int IntervalMilliseconds { get; set; } = 5000;
    public int? TimeoutMilliseconds { get; set; }
}

internal sealed class BlockSpecConverter : JsonConverter<BlockSpec> {
    public override BlockSpec Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? (BlockSpec)reader.GetString()! :
        new(JsonSerializer.Deserialize(ref reader, BlockConfigurationJson.Default.BlockSettings) ?? throw new JsonException("Expected a block name or definition."));

    public override void Write(Utf8JsonWriter writer, BlockSpec value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Settings, BlockConfigurationJson.Default.BlockSettings);
}
