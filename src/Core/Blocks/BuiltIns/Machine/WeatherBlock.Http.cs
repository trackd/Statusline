using System.Text.RegularExpressions;
namespace StatusLine.Blocks.BuiltIns.Machine;
/// <summary>
/// Current conditions from wttr.in, refreshed at most every 15 minutes on the thread pool.
/// Only queried when the Weather block is requested, because it makes an outbound HTTP call.
/// </summary>
internal sealed partial class WeatherBlock {
    private static readonly HttpClient Client = CreateClient();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    internal static Uri CreateUri(string? location) => new("https://wttr.in/" +
        (string.IsNullOrWhiteSpace(location) ? string.Empty : Uri.EscapeDataString(location.Trim())) + "?format=%25c%25t");

    private static HttpClient CreateClient() {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("curl/8.0");
        return client;
    }

    public static async Task<string?> ReadAsync(string? location, CancellationToken token) {
        string response = await Client.GetStringAsync(CreateUri(location), token).ConfigureAwait(false);
        string text = WhitespaceRegex().Replace(TerminalText.Sanitize(response, keepStyles: false), " ").Trim();
        if (text.Length is > 0 and <= 40 && !text.Contains('<') &&
            !text.Contains("Unknown", StringComparison.OrdinalIgnoreCase)) {
            return text;
        }

        throw new InvalidDataException("Invalid weather response.");
    }
}
