# C# Contour statusline prototype

This is an isolated compatibility experiment, **not a supported or bundled extension yet**.
The production module, public cmdlets, output targets and active Contour config are unchanged.
No Contour checkout or terminal executable is built by these scripts.

## Current result

- Release C# compilation succeeds with zero warnings/errors.
- Output is a plain WASI preview-1 core module, not a component.
- Required canonical exports and signatures are present, including `cabi_realloc`.
- The initial linear memory is 1.25 MiB (within Contour's 32 MiB limit).
- Preflight fails: the .NET runtime imports `wasi_snapshot_preview1/poll_oneoff`.
  Contour explicitly rejects that blocking function even if the adapter never calls sleep.
- The selected runtime links `libstandalonegc-disabled.a`. Long-running allocation/reclamation
  must be investigated before a managed adapter is suitable for continuous updates.

Do not remove Contour's import restrictions or replace blocking imports with success stubs.
The emitted runtime must genuinely work within the host contract. A tiny Rust/C adapter is
still the simpler compatible fallback; no language switch has been made here.

## Build and inspect

Requires .NET SDK 10.0.402 (latest patch allowed). Packages are pinned in the project.
The first build downloads the experimental LLVM AOT compiler, WIT generator and WASI SDK 29.
The SDK archive is approximately 535 MB and is cached outside this repository.

```powershell
pwsh -NoProfile -File ./extensions/contour-wasm-dotnet/build.ps1
pwsh -NoProfile -File ./extensions/contour-wasm-dotnet/Test-Adapter.ps1
```

Build output: `bin/Release/net10.0/wasi-wasm/publish/statusline.wasm` within this directory.
The build script reports preflight as structured data; currently `Compatible = False`.
`Test-ContourModule.ps1 -RequireCompatible` makes an incompatible report a terminating error.
This preflight checks declarations, not runtime behavior, deadlines or sustained memory usage.
Managed tests cover UTF-8/SGR content, duplicate suppression, unrelated variables, resize and clear.
They do not execute `load`'s WASM host import or run the module in Contour.

The ordinary module build does not build, copy or install this prototype.

## Adapter design

Based on the PR's `agent-status` example: `load` reads initial host user variables;
`update` accepts changes to `statusline_frame`; `render` returns cached UTF-8 bytes.
No commands, filesystem, network, polling timers or background process are needed in WASM.
Repeated unchanged frames avoid UTF-8 encoding; render reuses the stored byte array.

After compatibility is established, the module would publish rendered text/SGR using
OSC 1337 SetUserVar. It must not include DECSSDT/DECSASD wrappers: the extension owns
an isolated surface. OSC publishing still needs output serialization with prompt activity.
Resize redraws currently replay the frame; producer-side layout must be refreshed for the
new column count. Structured block layout in WASM is deliberately outside this prototype.

## Installation once compatible

Ship the validated `.wasm` with the PowerShell module. An explicit setup helper can copy
it to a stable user-owned location beside Contour configuration, then update a selected
profile with a backup. Importing the module must not silently edit configuration.
That helper and production transport are deferred until the compatibility gate passes.

Intended configuration, **not ready for use with this artifact**:

```yaml
profiles:
    main:
        extensions:
            statusline:
                path: /stable/path/statusline.wasm
                run_command: deny
                config:
                    variable: statusline_frame
        status_line:
            display: indicator
            extension: statusline
```

## Pinned references

Contour branch `feature/wasm-statusline-extensions` resolved to
`5413eb93439c86fcdfbd5cfff5dbbafd02739146` during this review.

- https://github.com/contour-terminal/contour/blob/5413eb93439c86fcdfbd5cfff5dbbafd02739146/extensions/bundled/agent-status/src/lib.rs
- https://github.com/contour-terminal/contour/blob/5413eb93439c86fcdfbd5cfff5dbbafd02739146/extensions/bundled/clock/src/lib.rs
- https://github.com/contour-terminal/contour/blob/5413eb93439c86fcdfbd5cfff5dbbafd02739146/extensions/testing/echo/src/lib.rs
- https://github.com/contour-terminal/contour/blob/5413eb93439c86fcdfbd5cfff5dbbafd02739146/src/vtextension/CompiledExtension.cpp
- https://github.com/contour-terminal/contour/blob/5413eb93439c86fcdfbd5cfff5dbbafd02739146/wit/statusline/README.md
- https://github.com/bytecodealliance/componentize-dotnet

The copied WIT contract is Apache-2.0; source attribution is in `wit/world.wit`.
