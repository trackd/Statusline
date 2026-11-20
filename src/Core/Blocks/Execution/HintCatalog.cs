namespace StatusLine.Blocks.Execution;

// Portable presets; editor adapters supply the command being edited.
internal static class HintCatalog {
    internal static readonly string[] Names = ["az", "aws", "kubernetes", "git", "docker"];

    internal static (StatusLineBlockKind Kind, string[] Commands) Resolve(string name) => name.ToLowerInvariant() switch {
        "az" or "azure" => (StatusLineBlockKind.Azure, ["az"]),
        "aws" => (StatusLineBlockKind.Aws, ["aws"]),
        "kubernetes" or "kubectl" => (StatusLineBlockKind.Kubernetes, ["kubectl", "kubectx", "kubens"]),
        "git" => (StatusLineBlockKind.GitDetailed, ["git"]),
        "docker" => (StatusLineBlockKind.Docker, ["docker"]),
        _ => throw new ArgumentException($"Unknown hint '{name}'. Use az, aws, kubernetes, git, docker or a block definition with Commands."),
    };
}
