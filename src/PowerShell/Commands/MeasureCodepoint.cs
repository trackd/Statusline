using System.Management.Automation;

namespace StatusLine;

/// <summary>
/// Cmdlet for measuring grapheme width and cursor movement for a string.
/// </summary>
[Cmdlet(VerbsDiagnostic.Measure, "Codepoint")]
[OutputType(typeof(Unicode.MeasurementResult))]
public sealed class MeasureCodepoint : PSCmdlet {
    /// <summary>
    /// The input string to measure.
    /// </summary>
    [Parameter(
        Mandatory = true,
        Position = 0,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true
    )]
    [AllowEmptyString]
    public string? InputString { get; set; }

    [Parameter]
    public SwitchParameter IncludeEscSequences { get; set; }

    protected override void ProcessRecord() {
        if (InputString is null) {
            return;
        }

        WriteObject(Unicode.Measure(str: InputString, skipVT: !IncludeEscSequences.IsPresent));
    }
}
