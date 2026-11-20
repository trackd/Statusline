using System.Collections.Concurrent;

namespace StatusLine;

internal sealed record EditorContext(string? ViMode) {
    private static readonly ConcurrentDictionary<Guid, EditorContext> Modes = new();

    internal static EditorContext? Get(Guid owner) => Modes.GetValueOrDefault(owner);
    internal static void Clear(Guid owner) => Modes.TryRemove(owner, out _);

    internal static bool Update(Guid owner, string? mode) {
        if (Modes.TryGetValue(owner, out EditorContext? previous) && previous.ViMode == mode) {
            return false;
        }
        Modes[owner] = new(mode);
        return true;
    }
}
