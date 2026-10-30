#Requires -Version 7.4

<#
.SYNOPSIS
Exercises the adapter's managed event handling without a Contour build.
.DESCRIPTION
Checks exact UTF-8 round trips, unchanged-frame suppression, unrelated variables,
resize requests and clearing. This does not execute the WASM runtime or its load import.
.EXAMPLE
./Test-Adapter.ps1
.OUTPUTS
System.String
.NOTES
Build the Release prototype first. Run it in a fresh PowerShell process; the adapter assembly stays loaded.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$assemblyPath = Join-Path $PSScriptRoot 'bin/Release/net10.0/wasi-wasm/statusline.dll'
[void][Reflection.Assembly]::LoadFrom($assemblyPath)
$frameType = [StatuslineWorld.StatuslineWorldExportsImpl]
$eventType = [StatuslineWorld.wit.Imports.terminalExtensions.statusline.v0_1_0.ITypesImports+Event]
$renderRequestType = [StatuslineWorld.wit.Imports.terminalExtensions.statusline.v0_1_0.ITypesImports+RenderRequest]
$frameText = [string][char]27 + '[38;2;4;0;253m日 🛰 café' + [char]27 + '[0m'
if ($frameType::Update($eventType::UserVar([ValueTuple[string,string]]::new('statusline_frame', $frameText))) -ne $renderRequestType::RENDER) {
    throw 'A new frame did not request rendering.'
}
$firstFrame = $frameType::Render(80, 1)
if ([Text.Encoding]::UTF8.GetString($firstFrame) -cne $frameText) {
    throw 'The adapter changed UTF-8 or SGR content.'
}
if ($frameType::Update($eventType::UserVar([ValueTuple[string,string]]::new('statusline_frame', $frameText))) -ne $renderRequestType::NONE) {
    throw 'An identical frame requested rendering.'
}
if (-not [object]::ReferenceEquals($firstFrame, $frameType::Render(80, 1))) {
    throw 'Rendering allocated a replacement for an unchanged frame.'
}
if ($frameType::Update($eventType::UserVar([ValueTuple[string,string]]::new('another_variable', 'wrong'))) -ne $renderRequestType::NONE) {
    throw 'An unrelated user variable changed the frame.'
}
$size = [StatuslineWorld.wit.Imports.terminalExtensions.statusline.v0_1_0.ITypesImports+Size]::new(40, 1)
if ($frameType::Update($eventType::Resized($size)) -ne $renderRequestType::RENDER) {
    throw 'Resize did not request a redraw.'
}
if ($frameType::Update($eventType::UserVar([ValueTuple[string,string]]::new('statusline_frame', ''))) -ne $renderRequestType::RENDER -or
    $frameType::Render(80, 1).Length -ne 0) {
    throw 'An empty update did not clear the frame.'
}
'Adapter managed checks passed. WASM execution and Contour acceptance remain separate.'
