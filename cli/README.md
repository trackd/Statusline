# StatusLine in Bash, Zsh and Fish

The PowerShell module remains a single `StatusLine.dll`. The executable compiles the
same rendering, Unicode, built-in providers, platform readers and refresh code from
`src/` directly. It has no PowerShell or shared Core DLL dependency. PowerShell session
capture, runspace identity and refresh wake-up callbacks stay in `src/PowerShell/`;
cmdlets and script-block execution remain module-only.

## Build

Native AOT must be published on the target operating system. For Linux x64, use a
.NET 10 SDK and its native prerequisites (on Ubuntu: `clang` and `zlib1g-dev`). The
build script publishes, runs the native executable checks, and creates a package:

```powershell
pwsh -NoProfile -NonInteractive -File cli/Publish-Native.ps1 -RuntimeIdentifier linux-x64
```

The `statusline` executable runs independently of `dotnet` or PowerShell. Unix packages
use tar.gz to retain the executable bit. The separate
`.dbg` file is optional debug information. The existing module build remains unchanged:

```powershell
pwsh -NoProfile -File ./build.ps1 -BuildAndTestOnly -SkipHelp
```

## Shell setup

Put the executable on `PATH`. Use the appropriate command in your interactive shell
configuration. Bash requires version 5 or newer for execution timing.

```bash
eval "$(statusline init bash --config /absolute/path/prompt.json)"
```

```zsh
eval "$(statusline init zsh --config /absolute/path/prompt.json)"
```

```fish
statusline init fish --config /absolute/path/prompt.json | source
```

Omit `--config` to use Directory, Git, Status and Duration. Configuration is JSON;
see [example.json](example.json). Block names, styles, icon sets and gradient colors
use the existing module's values. Right/Center prompt groups require `Rows: 2`.
An explicit width is passed by each shell adapter, along with the last exit code,
current directory and command duration. Bash/Zsh preserve existing prompt hooks;
Fish replaces `fish_prompt`. Reinitializing does not duplicate Bash/Zsh hooks.

Each initialized shell owns a persistent worker. Short CLI calls forward prompt
state to it; providers keep their asynchronous caches between calls and refresh
while the shell is idle. The worker exits when its shell exits. The next prompt
can recover a crashed worker. Configuration or relevant provider-environment changes
restart the worker so values are not reused across incompatible environments.

The worker can return a partial initial prompt while slow providers collect, just
as the module does. Later calls use the collected values. Failed refreshes preserve
cached values and label them stale. `statusline info --session TOKEN` reports the
worker PID, status-file error and provider errors for diagnostics.

## Live status bar

For a Unix terminal supporting DEC HostWritable status lines (such as Contour),
set the root configuration property `"Target": "HostWritable"`, then initialize
the shell normally with `--config`. The worker writes to `/dev/tty`, independently
of captured prompt output. It refreshes while idle, reads the actual terminal width
on each tick, and clears/hides the status area when stopped or when its parent shell
exits. It uses the shared terminal capability detector without consuming shell input;
unknown/unsupported terminals are rejected. CLI HostWritable on Windows is not yet
implemented; file output remains available there.

```json
{
  "Target": "HostWritable",
  "Left": ["Directory", "Git", "Status", "Duration"],
  "StatusLine": {
    "Left": ["Git"],
    "Center": ["Uptime"],
    "Right": ["Cpu", "SystemMemory", "Clock"]
  }
}
```

The optional `StatusLine` object configures the bar separately from the prompt.
The target defaults to no direct terminal output, so enabling a prompt does not
implicitly allocate a status area. Abrupt termination such as SIGKILL cannot perform
terminal cleanup. Other applications which change the terminal status mode may
require restarting the worker afterward.

For file output instead (or alongside HostWritable), add `--status-file`:

Add `--status-file /absolute/path/statusline.txt` to the initialization command:

```fish
statusline init fish --config /absolute/path/prompt.json --status-file /tmp/statusline.txt | source
```

The same option works with Bash and Zsh. The worker replaces this file atomically
and refreshes it while the shell is idle. The optional `StatusLine` object in
[example.json](example.json) configures its blocks independently of the prompt.
Without that object it shows Git, uptime, CPU, system memory and clock.
Use a separate file for each session and an existing parent directory.

This uses Contour's indicator-command target: configure the indicator to read the
file (for example, `cat /tmp/statusline.txt`). See the existing
[Contour file-output configuration](../docs/Configuration.md#file-output-and-contour-program).
File output does not write into the shell's main screen. The final frame remains in
the file after shutdown; a later session can reuse the path.

## Idle activity and refresh policy

The root `IdleTimeoutSeconds` option defaults to 60, matching the PowerShell module;
zero disables idle pausing. The CLI uses the module's monotonic activity tracker.
After that many seconds without a prompt or command-start event, background rendering,
new provider scheduling and status output pause. Already running bounded provider calls
may finish. Prompt readiness and command acceptance reset the timer. Individual
keystrokes and terminal visibility are not monitored, and the worker never reads
terminal input. Diagnostic polling does not count as activity.

`UpdateDuringCommand` defaults to false, also matching the module. Set it to true
to keep background refreshes running during commands, subject to the idle timeout.
These options apply to both file and HostWritable output and the worker's prompt
provider scheduling. `statusline info --session TOKEN` reports `IdleSeconds`,
`IsIdle`, `IsCommandExecuting` and `RefreshPaused`.

## Shell integration and transient prompts

`ShellIntegration: "Auto"` follows the module's terminal detection and marker policy.
It emits command/prompt boundaries and working-directory metadata in supported
terminals. Explicit `None`, `Osc133`, `Osc633` and `ITerm2` values are also accepted.
Command text is supplied by Zsh/Fish; Bash emits boundaries without command text.

`Transient: true` redraws accepted prompts using `TransientSymbol` in Zsh and
Fish 4.1 or newer. Fish 3.7 still supports ordinary prompts. Bash transient redraw
is not implemented; enabling it prints a clear notice and keeps the full prompt.

`statusline prompt` prints a prompt; `statusline statusline` prints one styled row
without moving the main terminal cursor. That row can be consumed by Contour's
command-based indicator. `--shell bash` adds Readline nonprinting markers;
`--shell zsh` wraps control sequences and escapes literal percent characters.
Raw/Fish output contains ordinary VT sequences. Paths and branch names are never
evaluated as shell code.

## Module-specific features

PowerShell-specific blocks and `ProcessMemory` are rejected by the CLI. The latter
currently measures the renderer process; it needs explicit shell-PID semantics before
being offered to other shells. Script-block definitions remain a module feature.
One-shot `prompt`/`statusline` calls without a session token remain available for
scripts. Their caches last only for that invocation. The PowerShell module retains
its existing in-process behavior and script-block features. Argument/configuration
errors exit with 2; unavailable providers can be absent from the initial frame.

## Installation and binary size

Plain `statusline init bash|zsh|fish` prints hook code without modifying profiles.
Add `--install` to explicitly install a managed profile block, or `--uninstall` to
remove it. `--profile FILE` selects a custom profile. Defaults are `.bashrc`,
`$ZDOTDIR/.zshrc` (or home), and `$XDG_CONFIG_HOME/fish/conf.d/statusline.fish`
(or the home configuration directory). Existing content, encoding, newlines and
symlinks are preserved; the first existing profile gets a `.statusline.bak` backup.

Release builds use Native AOT, full trimming, stripped native symbols and size
optimization. Reflection-based JSON is disabled in favor of generated serializers.
Debugger, stack trace, tracing/metrics and unused HTTP protocol features are disabled;
normal error messages, Unicode and globalization remain available. Stack traces
therefore require a diagnostic build. Archives include SHA256 checksum files.

See the [distribution plan](distribution/README.md) for package-manager packaging.

## Executable validation

The C# black-box suite uses only the BCL. Linux/glibc checks include real PTYs:

```powershell
dotnet run --project cli/tests/NativeCli.Tests.csproj -c Release -- /absolute/path/to/statusline
```

It checks isolated execution, real Git, status/duration, configuration validation,
grouped rows, cached provider invocation counts, idle status-file changes, stale
values on provider failure, recovery, parent lifetime, existing hooks, literal
prompt metacharacters, quoted paths, repeated initialization, resize and Ctrl+C.
HostWritable PTY checks cover idle refresh, resize without another prompt,
exit-status propagation, stop/parent-exit cleanup and unsupported terminals.
Run container tests with `--init` so terminated worker processes are reaped.
The target-platform CLI/worker checks also run in the native build workflow;
the PTY fixture is Linux/glibc-specific. macOS and Windows builds await hosted
validation. Full terminal/plugin coexistence and alternate screens remain outside
these automated checks.
