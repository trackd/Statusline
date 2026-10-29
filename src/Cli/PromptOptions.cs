using System.Text.Json.Serialization;

namespace StatusLine.Cli;

internal sealed class PromptOptions {
    public string[] Left { get; set; } = ["Directory", "Git", "Status", "Duration"];
    public string[] Right { get; set; } = [];
    public string[] Center { get; set; } = [];
    public SegmentStyle Style { get; set; } = SegmentStyle.Powerline;
    public IconSet IconSet { get; set; } = IconSet.NerdFont;
    public string Padding { get; set; } = " ";
    public string Symbol { get; set; } = " > ";
    public bool Transient { get; set; }
    public string TransientSymbol { get; set; } = " > ";
    public ShellIntegration ShellIntegration { get; set; } = ShellIntegration.Auto;
    public PromptOptions? StatusLine { get; set; }
    public StatusLineTarget? Target { get; set; }
    public int IdleTimeoutSeconds { get; set; } = 60;
    public bool UpdateDuringCommand { get; set; }
    public int Rows { get; set; } = 1;
    public bool BlockSeparator { get; set; }
    public bool BlendBackground { get; set; }
    public TextContrastMode TextContrast { get; set; } = TextContrastMode.Automatic;
    public string? StartColor { get; set; }
    public string? EndColor { get; set; }
    public string? WeatherLocation { get; set; }
    public int BlockTimeoutMilliseconds { get; set; } = 2000;
    public DirectoryDisplayStyle DirectoryStyle { get; set; } = DirectoryDisplayStyle.Home;
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PromptOptions))]
[JsonSerializable(typeof(SessionRequest))]
[JsonSerializable(typeof(SessionResponse))]
[JsonSerializable(typeof(SessionOwner))]
internal sealed partial class ConfigurationJson : JsonSerializerContext;
