using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

// Input acquisition stays separate from the upstream-derived table generator.
internal static class UnicodeInput {
    internal static async Task EnsureUnicodeFile(string path) {
        using var metadata = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "upstream.json")));
        string version = metadata.RootElement.GetProperty("UnicodeVersion").GetString()!;
        string expectedHash = metadata.RootElement.GetProperty("UnicodeDataSha256").GetString()!;
        if (File.Exists(path)) {
            await VerifyFile(path, expectedHash);
            return;
        }
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            string url = $"https://www.unicode.org/Public/{version}/ucdxml/ucd.nounihan.grouped.zip";
            Console.WriteLine($"Downloading Unicode {version} from {url}");
            using var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            using var content = await response.Content.ReadAsStreamAsync();
            using var archive = new ZipArchive(content, ZipArchiveMode.Read);
            var entry = archive.GetEntry("ucd.nounihan.grouped.xml")
                ?? throw new InvalidDataException("Unicode archive does not contain the expected XML file.");
            using (var input = entry.Open())
            using (var output = File.Create(temporaryPath)) {
                await input.CopyToAsync(output);
            }

            await VerifyFile(temporaryPath, expectedHash);

            // Do not overwrite a file created by another generator invocation.
            File.Move(temporaryPath, fullPath);
        }
        finally {
            if (File.Exists(temporaryPath)) {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task VerifyFile(string path, string expectedHash) {
        using var input = File.OpenRead(path);
        string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(input));
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException("Unicode XML does not match upstream.json. Use the pinned input or update its provenance explicitly.");
        }
    }
}
