using StatusLine.Interop;

namespace StatusLine;

public enum DirectoryDisplayStyle {
    Relative, Full, Home, Leaf, Compact,
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name",
        Justification = "Short refers to Windows short (8.3) paths, not a numeric type; retain the public configuration value.")]
    Short
}

/// <summary>Process-wide directory display preferences; filesystem/provider state is never changed.</summary>
public static class DirectoryDisplay {
    private static string? startupDirectory;
    private static int style;

    /// <summary>Directory captured on the first filesystem module import.</summary>
    public static string StartupDirectory => Volatile.Read(ref startupDirectory) ?? Environment.CurrentDirectory;
    /// <summary>Optional absolute base; null uses StartupDirectory.</summary>
    public static string? BasePath {
        get => Volatile.Read(ref field);
        set {
            if (string.IsNullOrEmpty(value)) {
                value = null;
            }

            if (value is not null && !Path.IsPathFullyQualified(value)) {
                throw new ArgumentException("BasePath must be absolute.");
            }

            Volatile.Write(ref field, value);
        }
    }
    public static DirectoryDisplayStyle Style {
        get => (DirectoryDisplayStyle)Volatile.Read(ref style);
        set {
            if (!Enum.IsDefined(value)) {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Volatile.Write(ref style, (int)value);
        }
    }
    /// <summary>Maximum trailing components in relative paths; zero disables shortening.</summary>
    public static int MaximumComponents {
        get => Volatile.Read(ref field);
        set {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Volatile.Write(ref field, value);
        }
    } = 3;
    public static int MaximumCharacters {
        get => Volatile.Read(ref field);
        set {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 4);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 4096);
            Volatile.Write(ref field, value);
        }
    } = 40;
    internal static void Initialize(string path) => Interlocked.CompareExchange(ref startupDirectory, path, null);

    public static string Format(string path) {
        ArgumentNullException.ThrowIfNull(path);
        if (!Path.IsPathFullyQualified(path)) {
            return path;
        }

        return Style switch {
            DirectoryDisplayStyle.Relative => Relative(path),
            DirectoryDisplayStyle.Home => HomeRelative(path),
            DirectoryDisplayStyle.Leaf => Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } leaf ? leaf : path,
            DirectoryDisplayStyle.Compact => Compact(path),
            DirectoryDisplayStyle.Short => Short(path),
            _ => path,
        };
    }

    private static string Relative(string path) {
        string relative = Path.GetRelativePath(BasePath ?? StartupDirectory, path);
        if (relative == ".") {
            return Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } leaf ? leaf : path;
        }
        // Different Windows volumes cannot be expressed relative to one another.
        if (Path.IsPathRooted(relative)) {
            return relative;
        }

        int maximum = MaximumComponents;
        if (maximum == 0) {
            return relative;
        }

        int separators = 0;
        for (int index = relative.Length - 1; index >= 0; index--) {
            if (relative[index] == Path.DirectorySeparatorChar && ++separators == maximum) {
                return string.Concat("..".AsSpan(), relative.AsSpan(index));
            }
        }

        return relative;
    }

    private static string HomeRelative(string path) {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length == 0) {
            return path;
        }

        string relative = Path.GetRelativePath(home, path);
        if (relative == ".") {
            return "~";
        }

        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) {
            return path;
        }

        return "~" + Path.DirectorySeparatorChar + relative;
    }

    private static string Compact(string path) {
        if (!OperatingSystem.IsWindows()) {
            return TerminalText.Clip(path, MaximumCharacters).Sequence;
        }

        char[] buffer = new char[MaximumCharacters + 1];
        return NativeMethods.PathCompactPathEx(buffer, path, (uint)buffer.Length, 0) ? new string(buffer, 0, Array.IndexOf(buffer, '\0')) : path;
    }

    private static string Short(string path) {
        if (!OperatingSystem.IsWindows()) {
            return path;
        }

        char[] buffer = new char[260];
        uint length = NativeMethods.GetShortPathName(path, buffer, (uint)buffer.Length);
        if (length >= buffer.Length) {
            buffer = new char[checked((int)length + 1)];
            length = NativeMethods.GetShortPathName(path, buffer, (uint)buffer.Length);
        }

        return length > 0 && length < buffer.Length ? new string(buffer, 0, (int)length) : path;
    }
}
