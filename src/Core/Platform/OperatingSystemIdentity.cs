using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace StatusLine.Platform;

/// <summary>Installed product identity, acquired once rather than polled during rendering.</summary>
internal sealed record OperatingSystemIdentity(string DisplayName, string Build) {
    private static readonly Lazy<OperatingSystemIdentity> Cached = new(Read);
    public static OperatingSystemIdentity Current => Cached.Value;

    private static OperatingSystemIdentity Read() {
        string build = Environment.OSVersion.Version.ToString();
        try {
            if (OperatingSystem.IsWindows()) {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                return Windows(key?.GetValue("ProductName") as string,
                    key?.GetValue("InstallationType") as string,
                    key?.GetValue("DisplayVersion") as string,
                    key?.GetValue("CurrentBuildNumber") as string,
                    Convert.ToString(key?.GetValue("UBR"), CultureInfo.InvariantCulture));
            }

            if (OperatingSystem.IsLinux()) {
                // os-release specifies exclusive /etc precedence; never merge vendor data.
                string path = "/etc/os-release";
                if (!File.Exists(path)) {
                    path = "/usr/lib/os-release";
                }

                if (File.Exists(path)) {
                    return new(ParseLinux(File.ReadAllText(path)), build);
                }
            }

            if (OperatingSystem.IsMacOS()) {
                var document = XDocument.Load("/System/Library/CoreServices/SystemVersion.plist");
                XElement? version = document.Descendants("key").FirstOrDefault(element => element.Value == "ProductVersion");
                if (version?.NextNode is XElement value && value.Name == "string") {
                    return new("macOS " + value.Value, build);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or System.Xml.XmlException) {
            // An unavailable identity source must not prevent prompt rendering.
        }

        if (OperatingSystem.IsLinux()) {
            return ReadUname(build);
        }

        return new(RuntimeInformation.OSDescription, build);
    }

    internal static OperatingSystemIdentity Windows(string? product, string? installationType,
        string? release, string? build, string? revision) {
        string name = product ?? "Windows";
        // Windows 11 can retain the Windows 10 registry product name. Never relabel Server.
        if (installationType == "Client" && int.TryParse(build, out int number) && number >= 22000) {
            name = "Windows 11";
        }
        else if (installationType == "Client" && name.StartsWith("Windows 10", StringComparison.Ordinal)) {
            name = "Windows 10";
        }

        if (!string.IsNullOrWhiteSpace(release)) {
            name += " " + release;
        }

        string fullBuild = build ?? Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(revision)) {
            fullBuild += "." + revision;
        }

        return new(name, fullBuild);
    }

    internal static string ParseLinux(string text) {
        Dictionary<string, string> values = ParseAssignments(text);
        if (values.TryGetValue("PRETTY_NAME", out string? pretty) && pretty.Length > 0) {
            return pretty;
        }

        string name = values.GetValueOrDefault("NAME", "Linux");
        string version = values.GetValueOrDefault("VERSION", values.GetValueOrDefault("VERSION_ID", ""));
        return (name + " " + version).Trim();
    }

    private static OperatingSystemIdentity ReadUname(string build) {
        try {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            (int exitCode, string? output, _) = ProcessRunner.RunAsync("uname", ["-s", "-r"], string.Empty, cancellation.Token)
                .GetAwaiter().GetResult();
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output)) {
                return new(output.Trim(), build);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or OperationCanceledException) {
            // Minimal containers may not ship uname either.
        }

        return new(RuntimeInformation.OSDescription, build);
    }

    private static Dictionary<string, string> ParseAssignments(string text) {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ReadOnlySpan<char> sourceLine in text.AsSpan().EnumerateLines()) {
            ReadOnlySpan<char> line = sourceLine.Trim();
            int separator = line.IndexOf('=');
            if (line.IsEmpty || line[0] == '#' || separator <= 0) {
                continue;
            }

            ReadOnlySpan<char> value = line[(separator + 1)..].Trim();
            bool singleQuoted = value.Length >= 2 && value[0] == '\'' && value[^1] == '\'';
            bool doubleQuoted = value.Length >= 2 && value[0] == '"' && value[^1] == '"';
            if (singleQuoted || doubleQuoted) {
                value = value[1..^1];
            }

            var decoded = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++) {
                char character = value[index];
                if (!singleQuoted && character == '\\' && index + 1 < value.Length &&
                    (!doubleQuoted || value[index + 1] is '$' or '`' or '"' or '\\')) {
                    character = value[++index];
                }

                if (!char.IsControl(character)) {
                    decoded.Append(character);
                }
            }

            values[line[..separator].Trim().ToString()] = decoded.ToString();
        }

        return values;
    }
}
