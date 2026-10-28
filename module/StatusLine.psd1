@{
    RootModule = 'StatusLine.dll'
    ModuleVersion = '0.2.0'
    GUID = '5ee48f8d-7f90-4f03-83d9-569c92cb56fa'
    Author = 'trackd'
    Description = 'Shared-block PowerShell prompts and DEC host-writable status lines with background updates.'
    PowerShellVersion = '7.6'
    CompatiblePSEditions = @('Core')
    FunctionsToExport = @()
    CmdletsToExport = @(
        'Enable-StatusLine'
        'Disable-StatusLine'
        'Enable-Prompt'
        'Disable-Prompt'
        'New-StatusLineBlock'
        'Show-Prompt'
        'Show-StatusLine'
        'Measure-Codepoint'
    )
    VariablesToExport = @()
    AliasesToExport = @()
    PrivateData = @{
        PSData = @{
            Tags = @('Prompt', 'Terminal', 'StatusLine', 'CrossPlatform')
            LicenseUri = 'https://github.com/trackd/Statusline/blob/main/LICENSE'
            ProjectUri = 'https://github.com/trackd/Statusline'
            ReleaseNotes = 'Pre-release source: shared prompt/status blocks, explicit lifecycle, previews and Unicode measurement. See the repository for platform acceptance status.'
        }
    }
}
