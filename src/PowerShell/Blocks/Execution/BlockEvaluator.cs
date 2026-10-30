using System.Management.Automation;

namespace StatusLine.Blocks.Execution;

internal static class BlockEvaluator {
    public static List<StatusLineBlock> Create(IReadOnlyList<BlockSelector> selection,
        BlockContext context, BlockOverrides overrides) {
        var blocks = new List<StatusLineBlock>(selection.Count);
        AppendTo(blocks, selection, context, overrides);
        return blocks;
    }

    internal static void AppendTo(List<StatusLineBlock> blocks, IReadOnlyList<BlockSelector> selection,
        BlockContext context, BlockOverrides overrides, StatusLineAlignment? groupAlignment = null) {
        for (int index = 0; index < selection.Count; index++) {
            BlockSelector selector = selection[index];
            StatusLineAlignment? placement = groupAlignment ?? selector.Alignment;
            if (selector.Implementation is { } implementation) {
                if (BuiltInRefresh.Evaluate(implementation, context, out string? error) is { } data &&
                    (data.Text.Length > 0 || data.Icon.Length > 0)) {
                    blocks.Add(selector.Binding.Render(data, overrides, selector.Definition, placement,
                        implementation.Metadata.OrdinaryLeadingSpace, error,
                        shrinkable: implementation.Metadata.Kind == StatusLineBlockKind.Directory));
                }

                continue;
            }

            if (selector.Definition is not { } definition) {
                StatusLineBlock value = selector.Value!.WithPadding(overrides.Padding);
                if (placement is { } alignment) {
                    value = value.WithAppearance(alignment, value.Foreground, value.Background);
                }

                blocks.Add(value);
                continue;
            }

            (List<object>? output, Refreshing.RefreshSnapshot<List<object>> status) = definition.GetOutput(context.State, context.SlowProviderWait, context.RunspaceId);
            blocks.AddRange(selector.Binding.RenderScript(output, definition, overrides, placement, status.Error));
        }
    }

    internal static StatusLineBlock RenderScriptItem(object item, DefinitionAppearance definition,
        BlockOverrides overrides, StatusLineAlignment? alignment, string? error) {
        StatusLineBlock value;
        if (item is StatusLineBlock block) {
            value = new StatusLineBlock(block.Text,
                definition.Foreground ?? block.Foreground,
                definition.Background ?? block.Background,
                alignment ?? definition.Alignment ?? block.Alignment,
                definition.Padding ?? overrides.Padding,
                definition.Bold ?? block.Bold, definition.Italic ?? block.Italic,
                definition.Underline ?? block.Underline,
                definition.Icon ?? block.Icon) {
                PreserveForeground = definition.Foreground is not null,
                PreserveBackground = definition.Background is not null,
                UseOrdinaryLeadingSpace = block.UseOrdinaryLeadingSpace,
            };
            if (block.IsStale) {
                value = value.AsStale(block.LastError);
            }
        }
        else {
            value = new StatusLineBlock(LanguagePrimitives.ConvertTo<string>(item),
                definition.Foreground ?? default, definition.Background ?? default,
                alignment ?? definition.Alignment ?? StatusLineAlignment.Right, definition.Padding ?? overrides.Padding,
                definition.Bold ?? false, definition.Italic ?? false, definition.Underline ?? false, definition.Icon ?? string.Empty) {
                PreserveForeground = definition.Foreground is not null,
                PreserveBackground = definition.Background is not null,
            };
        }

        return error is null ? value : value.AsStale(error);
    }
}
