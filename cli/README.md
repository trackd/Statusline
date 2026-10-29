# StatusLine in Bash, Zsh and Fish

StatusLine provides a configurable prompt and an asynchronously refreshed status
bar. The CLI is a single native executable and runs without PowerShell or a .NET
runtime installation. It shares its renderer and providers with the PowerShell module.

## Shell setup

Put `statusline` on `PATH`, then initialize it in your interactive shell:

```bash
eval "$(statusline init bash --config /absolute/path/prompt.json)"
```

```zsh
eval "$(statusline init zsh --config /absolute/path/prompt.json)"
```

```fish
statusline init fish --config /absolute/path/prompt.json | source
```

Omit `--config` for the default Directory, Git, Status and Duration prompt.
See [example.json](example.json) for a two-row prompt with runtime providers and a
separately configured status bar. Right and center prompt groups require `Rows: 2`.
Use a Nerd Font for icons, or set `"IconSet": "None"`.

Bash requires version 5 or newer. Transient prompts are supported in Zsh and Fish
4.1 or newer with `"Transient": true`. Zsh completion registration requires
`compdef` to be available.

### Install a profile hook

Plain `init` prints shell code without changing your profile. Add `--install` to
install a managed hook, or `--uninstall` to remove it:

```sh
statusline init bash --config /absolute/path/prompt.json --install
statusline init bash --uninstall
```

The same options work for Zsh and Fish. Use `--profile FILE` for a custom location.
Default locations are:

- Bash: `~/.bashrc`
- Zsh: `$ZDOTDIR/.zshrc`, or `~/.zshrc`
- Fish: `$XDG_CONFIG_HOME/fish/conf.d/statusline.fish`, or its equivalent under `~/.config`

Existing profile content, encoding, newlines and symlinks are preserved. The first
existing profile receives a `.statusline.bak` backup. Quoted `~/...` paths are
expanded by the CLI. On Linux, omitting the shell name lets `init` infer the calling shell.

## Live status bar

Contour supports a host-writable status display that updates independently of the
prompt. Enable it with `"Target": "HostWritable"`:

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
Without it, the bar shows Git, uptime, CPU, system memory and clock. Enabling a
prompt alone does not allocate a status area.

HostWritable currently requires a supported Unix terminal. Ghostty does not
support this display; its prompt integration remains available. Windows users
can use file output.

### File output

Add `--status-file` to initialization to write an atomically replaced status file:

```fish
statusline init fish --config /absolute/path/prompt.json --status-file /tmp/statusline.txt | source
```

The same option works with Bash and Zsh. Use a separate file for each session and
an existing parent directory. Contour's command-based indicator can read the file;
see [file-output configuration](../docs/Configuration.md#file-output-and-contour-program).
The final frame remains in the file after the worker exits.

## Refresh and shell integration

Each shell owns a persistent worker with asynchronous provider caches. Slow
providers may be absent from the first prompt; later renders use their collected
values. Failed refreshes retain cached values and mark them stale. Workers recover
at the next prompt after a crash and restart when relevant configuration or
provider environment values change.

On Linux, Bash, Zsh and Fish reuse persistent connections without launching a CLI
client for each prompt. macOS Zsh/Fish currently use short-lived clients.

`IdleTimeoutSeconds` defaults to 60; set it to 0 to disable idle pausing.
`UpdateDuringCommand` defaults to false. Set it to true to continue refreshing
during commands, subject to the idle timeout. Activity is measured from prompt
and command-start events; individual keystrokes are not monitored.

`ShellIntegration: "Auto"` reports prompt/command boundaries and the working
directory in supported terminals. It defers events already supplied by terminal
hooks or native Fish marking. Explicit `None`, `Osc133`, `Osc633` and `ITerm2`
settings are also available. See the [Contour/Ghostty compatibility notes](../docs/ShellIntegration-Compatibility.md)
for supported behavior and limitations.

A shell started with `sudo -i`, `sudo -s` or `su` needs its own profile hook,
running as that user. The outer shell's worker pauses during commands by default
and restores its status area when the prompt returns.

## Commands and limitations

Run `statusline --help` for commands and options. Useful standalone commands include:

- `statusline prompt`: render a prompt.
- `statusline statusline`: render a status row without changing the terminal display.
- `statusline info --session TOKEN`: inspect worker state and provider errors.
- `statusline stop --session TOKEN`: stop a worker and restore its terminal state.

Standalone renders without a session token retain caches only for that invocation.
PowerShell session blocks, script-block providers and `ProcessMemory` remain
module-only. Abrupt termination such as SIGKILL cannot perform terminal cleanup.

## Build and test

Building the CLI requires the .NET 11 SDK or newer. Native AOT publishing must run
on the target operating system; Ubuntu also needs `clang` and `zlib1g-dev`.

```powershell
dotnet build cli/StatusLine.Cli.csproj
pwsh -NoProfile -NonInteractive -File cli/Publish-Native.ps1 -RuntimeIdentifier linux-x64
```

The managed build runs trim/AOT compatibility checks without native compilation.
The publish script builds the executable, runs its checks, and creates an archive
with a SHA256 checksum. The default module build excludes the CLI; the PowerShell
module remains a single `StatusLine.dll` targeting .NET 10.

Release builds use full trimming and speed optimization. Select size optimization
with `-OptimizationPreference Size`; use `-OutputPath` for a separate output directory.
Native debug symbols are optional and separate from the executable.

To include the pinned Contour/Ghostty integration checks on Linux:

```powershell
pwsh -NoProfile -NonInteractive -File cli/Publish-Native.ps1 -RuntimeIdentifier linux-x64 -VerifyTerminalIntegration
```

These checks exercise both integration load orders in Bash, Zsh and Fish PTYs.
They also run in Linux CI. Live terminal UI behavior still needs manual validation.
When testing in a container, use `--init` so exited workers are reaped.
