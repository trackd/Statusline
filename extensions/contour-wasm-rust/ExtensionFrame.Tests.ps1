BeforeAll {
    if (-not ('StatusLine.ContourPrototype.ExtensionFrame' -as [type])) {
        Add-Type -Path (Join-Path $PSScriptRoot 'ExtensionFrame.cs')
    }
}

Describe 'Unshipped Contour frame prototype' {
    It 'round trips UTF-8 and embedded controls in a single OSC message' {
        $text = "`e[31m" + [char]0x65e5 + " café`e[0m`a"
        $sequence = [StatusLine.ContourPrototype.ExtensionFrame]::CreateSequence($text)
        $sequence | Should -Match '^\x1b\]1337;SetUserVar=statusline_frame=([A-Za-z0-9+/=]*)\x1b\\$'
        $encoded = [regex]::Match($sequence, 'statusline_frame=([A-Za-z0-9+/=]*)').Groups[1].Value
        [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded)) | Should -BeExactly $text
    }

    It 'rejects oversized UTF-8 frames' {
        { [StatusLine.ContourPrototype.ExtensionFrame]::CreateSequence(([string][char]0x65e5) * 21846) } | Should -Throw
        [StatusLine.ContourPrototype.ExtensionFrame]::CreateSequence('') |
            Should -BeExactly "`e]1337;SetUserVar=statusline_frame=`e\"
    }
}
