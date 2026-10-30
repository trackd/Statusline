# Contour WASM painter (unshipped prototype)

This experimental Rust painter and standalone C# frame encoder are retained for development only.
They are not part of StatusLine's public API, staged module, Gallery package or release archive.
`Enable-StatusLine` supports HostWritable and File; it does not support `-Target Extension`.

The proposed transport sends UTF-8 frames as base64 in OSC 1337
`SetUserVar=statusline_frame`. A compatible Contour development build decodes the user variable
and lets the painter return VT bytes on its isolated status surface. Both ends cap payloads at
64 KiB of UTF-8. The painter has no timers, commands, filesystem or networking.
No live producer is wired into the released module.

Source and WIT bindings target Contour `5413eb93439c86fcdfbd5cfff5dbbafd02739146` and its
experimental `feature/wasm-statusline-extensions` work. The copied WIT is Apache-2.0 and records
its source attribution. The older C# toolchain experiment is under `extensions/contour-wasm-dotnet`.

## Developer checks

The encoder is independent of the production assembly:

```powershell
Invoke-Pester -Path ./extensions/contour-wasm-rust/ExtensionFrame.Tests.ps1
```

Rebuilding the painter requires Rust, the `wasm32-wasip1` target, and wasm-tools.
Dependencies use Cargo.lock:

```powershell
& ./extensions/contour-wasm-rust/build.ps1 -WasmToolsPath '/path/to/wasm-tools'
```

The prototype build runs Rust tests and checks core-WASM imports/exports against Contour's
contract. It writes the experimental asset to `artifacts/contour/statusline.wasm`, outside the
module source tree; the normal build never stages it. Do not copy it into a public package.
wasm-tools is not bundled with this repository; pass the path of a separately installed copy.

## Validation boundaries

Encoder tests and compiled WASM contract checks do not prove transport integration or rendering
in a Contour window. Reintroducing this feature requires a deliberately supported producer API,
native integration tests and live-terminal checks for resizing, idle/wake, prompt output,
configuration reload and disabling/re-enabling.
