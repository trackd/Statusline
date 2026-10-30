using System.Management.Automation;
using System.Management.Automation.Language;

namespace StatusLine.Commands;

/// <summary>Completes built-in block kinds.</summary>
public sealed class BlockKindCompleter : IArgumentCompleter {
    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        CommandAst commandAst,
        System.Collections.IDictionary fakeBoundParameters
    ) {
        string word = wordToComplete.Trim('\'', '"');
        foreach (string kind in Enum.GetNames<StatusLineBlockKind>()) {
            if (kind.StartsWith(word, StringComparison.OrdinalIgnoreCase)) {
                yield return new CompletionResult(
                    kind,
                    kind,
                    CompletionResultType.ParameterValue,
                    $"Built-in {kind} block"
                );
            }
        }
    }
}
