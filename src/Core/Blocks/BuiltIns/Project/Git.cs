using System.Diagnostics;
using System.Globalization;

namespace StatusLine.Blocks.BuiltIns.Project;

/// <summary>Working tree status parsed from <c>git status --porcelain=v2 --branch</c>.</summary>
internal sealed record GitStatus(int Ahead, int Behind, int Staged, int Modified, int Untracked, int Conflicts) {
    public bool IsDirty => Staged + Modified + Untracked + Conflicts > 0;
}

/// <summary>Repository located by walking up from a directory.</summary>
internal sealed record GitRepository(string WorkTree, string GitDirectory) {
    /// <summary>Finds the nearest repository, following <c>gitdir:</c> files used by worktrees and submodules.</summary>
    public static GitRepository? Find(string path) {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent) {
            string gitPath = Path.Combine(directory.FullName, ".git");
            if (File.Exists(gitPath)) {
                string reference = File.ReadAllText(gitPath).Trim();
                if (!reference.StartsWith("gitdir: ", StringComparison.Ordinal)) {
                    return null;
                }

                gitPath = Path.GetFullPath(Path.Combine(directory.FullName, reference[8..]));
            }

            if (Directory.Exists(gitPath)) {
                return new GitRepository(directory.FullName, gitPath);
            }
        }

        return null;
    }

    /// <summary>Reads HEAD directly so the branch is available even when git is missing or slow.</summary>
    public string? ReadBranch() {
        string headPath = Path.Combine(GitDirectory, "HEAD");
        if (!File.Exists(headPath)) {
            return null;
        }

        string head = File.ReadAllText(headPath).Trim();
        if (head.StartsWith("ref: refs/heads/", StringComparison.Ordinal)) {
            return head[16..];
        }

        if (head.Length is >= 40 and <= 64 && head.All(char.IsAsciiHexDigit)) {
            return "detached:" + head[..7];
        }

        return null;
    }
}

/// <summary>
/// Caches git status per repository and refreshes it on the thread pool, so neither the prompt
/// nor the status line thread is blocked by large repositories.
/// </summary>
internal static class GitStatusProvider {
    internal static async Task<GitStatus?> ReadStatusAsync(GitRepository repository, CancellationToken token) {
        var startInfo = new ProcessStartInfo("git") {
            WorkingDirectory = repository.WorkTree,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--no-optional-locks");
        // Explicit options override inherited GIT_DIR/GIT_WORK_TREE without copying the
        // process environment. ArgumentList preserves spaces in worktree/submodule paths.
        startInfo.ArgumentList.Add("--git-dir=" + repository.GitDirectory);
        startInfo.ArgumentList.Add("--work-tree=" + repository.WorkTree);
        startInfo.ArgumentList.Add("status");
        startInfo.ArgumentList.Add("--porcelain=v2");
        startInfo.ArgumentList.Add("--branch");

        try {
            (int ExitCode, string Output, string Error) = await ProcessRunner.RunAsync(startInfo, token).ConfigureAwait(false);
            if (ExitCode == 0) {
                return Parse(Output);
            }

            if (!File.Exists(Path.Combine(repository.GitDirectory, "config")) &&
                !File.Exists(Path.Combine(repository.GitDirectory, "commondir"))) {
                return null;
            }

            throw new IOException($"git status exited with code {ExitCode}.");
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 2) {
            return null;
        }
    }

    internal static GitStatus Parse(string output) {
        int ahead = 0, behind = 0, staged = 0, modified = 0, untracked = 0, conflicts = 0;
        foreach (ReadOnlySpan<char> line in output.AsSpan().EnumerateLines()) {
            if (line.StartsWith("# branch.ab ")) {
                ReadOnlySpan<char> counts = line[12..];
                int separator = counts.IndexOf(' ');
                if (separator > 1 && separator + 2 < counts.Length) {
                    int.TryParse(counts[1..separator], NumberStyles.None, CultureInfo.InvariantCulture, out ahead);
                    int.TryParse(counts[(separator + 2)..], NumberStyles.None, CultureInfo.InvariantCulture, out behind);
                }
            }
            else if (line.Length > 3 && (line[0] == '1' || line[0] == '2') && line[1] == ' ') {
                if (line[2] != '.') {
                    staged++;
                }

                if (line[3] != '.') {
                    modified++;
                }
            }
            else if (line.StartsWith("u ")) {
                conflicts++;
            }
            else if (line.StartsWith("? ")) {
                untracked++;
            }
        }

        return new GitStatus(ahead, behind, staged, modified, untracked, conflicts);
    }
}
