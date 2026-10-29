<#
.SYNOPSIS
Regenerates the pinned terminal width tables from Unicode data.
.DESCRIPTION
Downloads East Asian Width and Emoji Presentation data from the Unicode Consortium and replaces
the two sorted interval tables in CellWidthData.cs. No runtime Unicode download is required.
.PARAMETER UnicodeVersion
Unicode release to use. The generator also updates the source comment.
.PARAMETER InputDirectory
Optional offline directory containing EastAsianWidth.txt and emoji-data.txt.
.PARAMETER OutputPath
Optional candidate output file. Omit to atomically replace the production tables.
.EXAMPLE
./tools/Update-CellWidthData.ps1
.OUTPUTS
None
.NOTES
Requires network access unless InputDirectory is supplied. Generated Unicode data is covered by docs/Unicode-LICENSE.txt.
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$UnicodeVersion = '17.0.0',
    [string]$InputDirectory,
    [string]$OutputPath = (Join-Path $PSScriptRoot '../src/Core/Rendering/Unicode/CellWidthData.cs')
)

$ErrorActionPreference = 'Stop'

function Get-WidthRange {
    param([string]$SourceText, [string[]]$Property)
    $intervals = foreach ($line in $SourceText -split '\r?\n') {
        if ($line -match '^([0-9A-F]+)(?:\.\.([0-9A-F]+))?\s*;\s*(\w+)') {
            if ($Matches[3] -notin $Property) { continue }
            $start = [Convert]::ToInt32($Matches[1], 16)
            $end = if ($Matches[2]) { [Convert]::ToInt32($Matches[2], 16) } else { $start }
            if ($start -gt $end -or $end -gt 0x10FFFF) {
                throw 'Unicode input contains an invalid codepoint range.'
            }
            [pscustomobject]@{ Start = $start; End = $end }
        }
    }
    $merged = [Collections.Generic.List[object]]::new()
    foreach ($interval in $intervals | Sort-Object -Property Start) {
        if ($merged.Count -gt 0 -and $interval.Start -le $merged[$merged.Count - 1].End + 1) {
            $merged[$merged.Count - 1].End = [Math]::Max($merged[$merged.Count - 1].End, $interval.End)
        }
        else { $merged.Add($interval) }
    }
    $merged
}

function ConvertTo-WidthTable {
    param([string]$Name, [object[]]$Range, [string]$NewLine)
    $rows = for ($index = 0; $index -lt $Range.Count; $index += 4) {
        $pairs = for ($offset = $index; $offset -lt [Math]::Min($index + 4, $Range.Count); $offset++) {
            '0x{0:X}, 0x{1:X}' -f $Range[$offset].Start, $Range[$offset].End
        }
        '        ' + ($pairs -join ', ') + ','
    }
    "    private static ReadOnlySpan<int> $Name => [$NewLine" + ($rows -join $NewLine) + "$NewLine    ];"
}

$sourceRoot = "https://www.unicode.org/Public/$UnicodeVersion/ucd/"
if ($InputDirectory) {
    $wideText = Get-Content -LiteralPath (Join-Path $InputDirectory 'EastAsianWidth.txt') -Raw
    $emojiText = Get-Content -LiteralPath (Join-Path $InputDirectory 'emoji-data.txt') -Raw
}
else {
    $wideText = (Invoke-WebRequest -Uri ($sourceRoot + 'EastAsianWidth.txt')).Content
    $emojiText = (Invoke-WebRequest -Uri ($sourceRoot + 'emoji/emoji-data.txt')).Content
}
$sourcePath = Join-Path $PSScriptRoot '../src/Core/Rendering/Unicode/CellWidthData.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw
# Keep the checked-in line endings so regeneration only changes table values.
$newLine = if ($source.Contains("`r`n")) { "`r`n" } else { "`n" }
$wideRanges = @(Get-WidthRange -SourceText $wideText -Property W, F)
$emojiRanges = @(Get-WidthRange -SourceText $emojiText -Property Emoji_Presentation)
if ($wideRanges.Count -eq 0 -or $emojiRanges.Count -eq 0) {
    throw 'Unicode input did not produce both width tables; source was not modified.'
}
$wideTable = ConvertTo-WidthTable -Name Wide -Range $wideRanges -NewLine $newLine
$emojiTable = ConvertTo-WidthTable -Name Emoji -Range $emojiRanges -NewLine $newLine
$widePattern = '(?s)    private static ReadOnlySpan<int> Wide => \[.*?\n    \];'
$emojiPattern = '(?s)    private static ReadOnlySpan<int> Emoji => \[.*?\n    \];'
if ([regex]::Matches($source, $widePattern).Count -ne 1 -or [regex]::Matches($source, $emojiPattern).Count -ne 1) {
    throw 'Expected exactly one Wide and Emoji table; source was not modified.'
}
$source = [regex]::Replace($source, $widePattern, [Text.RegularExpressions.MatchEvaluator]{ $wideTable })
$source = [regex]::Replace($source, $emojiPattern, [Text.RegularExpressions.MatchEvaluator]{ $emojiTable })
$source = $source -replace 'Unicode \d+\.\d+\.\d+', "Unicode $UnicodeVersion"
$source = $source -replace '/Public/\d+\.\d+\.\d+/', "/Public/$UnicodeVersion/"
$outputFile = [IO.Path]::GetFullPath($OutputPath)
$temporaryFile = $outputFile + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporaryFile, $source)
    [IO.File]::Move($temporaryFile, $outputFile, $true)
}
finally {
    if (Test-Path -LiteralPath $temporaryFile) {
        Remove-Item -LiteralPath $temporaryFile -Force
    }
}
