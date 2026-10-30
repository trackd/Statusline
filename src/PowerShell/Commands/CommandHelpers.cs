using System.Management.Automation;

namespace StatusLine.Commands;

internal static class CommandHelpers {
    public static string? GetPowerShellVersion(SessionState sessionState) =>
        sessionState.PSVariable.GetValue("PSVersionTable") is System.Collections.IDictionary table
            ? table["PSVersion"]?.ToString()
            : null;

    /// <summary>Resolves block names, reporting unknown names as a terminating parameter error.</summary>
    public static BlockSelector[] ParseSelection(PSCmdlet cmdlet, object[] names) {
        try {
            return BlockSelector.Parse(names);
        }
        catch (ArgumentException exception) {
            cmdlet.ThrowTerminatingError(new ErrorRecord(exception, "UnknownBlockKind", ErrorCategory.InvalidArgument, names));
            throw;
        }
    }

    public static BlockSelector[] ParseGroups(PSCmdlet cmdlet, object[] kinds, object[]? left, object[]? center, object[]? right) {
        var selection = new List<BlockSelector>();
        if (cmdlet.MyInvocation.BoundParameters.ContainsKey("Block") || (left is null && center is null && right is null)) {
            selection.AddRange(ParseSelection(cmdlet, kinds));
        }

        Add(left, StatusLineAlignment.Left);
        Add(center, StatusLineAlignment.Center);
        Add(right, StatusLineAlignment.Right);
        return [.. selection];

        void Add(object[]? names, StatusLineAlignment alignment) {
            if (names is null) {
                return;
            }

            foreach (BlockSelector selector in ParseSelection(cmdlet, names)) {
                selection.Add(selector with {
                    Alignment = alignment
                });
            }
        }
    }
}
