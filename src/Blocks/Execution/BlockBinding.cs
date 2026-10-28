using System.Drawing;

namespace StatusLine.Blocks.Execution;

// The cache belongs to a compiled selection, not the singleton implementation.
internal sealed class BlockBinding {
    private readonly Lock gate = new();
    private BlockContent? previous;
    private BlockOverrides? appearance;
    private string? lastError;
    private StatusLineAlignment? previousAlignment;
    private StatusLineBlockDefinition? previousDefinition;
    private long definitionVersion;
    private StatusLineBlock? rendered;
    private List<object>? scriptOutput;
    private StatusLineBlock[]? scriptBlocks;
    private BlockOverrides? scriptAppearance;
    private string? scriptError;
    private bool reusableScriptOutput;

    internal StatusLineBlock[] RenderScript(List<object> output, StatusLineBlockDefinition definition,
        BlockOverrides overrides, StatusLineAlignment? alignment, string? error) {
        lock (gate) {
            long version = definition.AppearanceVersion;
            bool sameOutput = ReferenceEquals(scriptOutput, output);
            if (!sameOutput) {
                // Arbitrary returned objects may have a live ToString implementation. Retain
                // that behavior; only known immutable output can reuse its rendered snapshot.
                reusableScriptOutput = output.TrueForAll(static item => item is string or StatusLineBlock);
            }

            if (reusableScriptOutput && scriptBlocks is not null && sameOutput &&
                scriptAppearance == overrides && scriptError == error && previousAlignment == alignment &&
                ReferenceEquals(previousDefinition, definition) && definitionVersion == version) {
                return scriptBlocks;
            }

            (version, DefinitionAppearance current) = definition.CaptureAppearance();
            var blocks = new StatusLineBlock[output.Count];
            for (int index = 0; index < blocks.Length; index++) {
                blocks[index] = BlockEvaluator.RenderScriptItem(output[index], current, overrides, alignment, error);
            }

            scriptBlocks = blocks;
            scriptOutput = output;
            scriptAppearance = overrides;
            scriptError = error;
            previousAlignment = alignment;
            previousDefinition = definition;
            definitionVersion = version;
            return blocks;
        }
    }

    internal StatusLineBlock Render(BlockContent data, BlockOverrides overrides,
        StatusLineBlockDefinition? definition, StatusLineAlignment? alignment, bool duration, string? error, bool shrinkable = false) {
        lock (gate) {
            long version = definition?.AppearanceVersion ?? 0;
            if (rendered is not null && previous == data && appearance == overrides && lastError == error &&
                previousAlignment == alignment && ReferenceEquals(previousDefinition, definition) && definitionVersion == version) {
                return rendered;
            }

            DefinitionAppearance current = default;
            if (definition is not null) {
                (version, current) = definition.CaptureAppearance();
            }

            TerminalColor foreground = overrides.Foreground ?? data.Accent;
            TerminalColor background = overrides.Background ?? Color.Empty;
            StatusLineAlignment placement = overrides.Alignment ?? data.Alignment;
            string padding = current.Padding ?? overrides.Padding;
            string icon = data.Icon;
            foreground = current.Foreground ?? foreground;
            background = current.Background ?? background;
            placement = current.Alignment ?? placement;
            icon = current.Icon ?? icon;
            placement = alignment ?? placement;
            var block = new StatusLineBlock(data.Text, foreground, background, placement, padding,
                current.Bold ?? false, current.Italic ?? false, current.Underline ?? false, icon) {
                PreserveForeground = current.Foreground is not null,
                PreserveBackground = current.Background is not null,
                UseOrdinaryLeadingSpace = duration,
                Shrinkable = shrinkable,
            };
            rendered = error is null ? block : block.AsStale(error);
            previous = data;
            appearance = overrides;
            lastError = error;
            previousAlignment = alignment;
            previousDefinition = definition;
            definitionVersion = version;
            return rendered;
        }
    }
}
