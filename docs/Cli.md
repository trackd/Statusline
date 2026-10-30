# StatusLine in Bash, Zsh and Fish

StatusLine provides a configurable prompt and an asynchronously refreshed status
bar. The CLI is a single native executable and runs without PowerShell or a .NET
runtime installation. It shares its renderer and providers with the PowerShell module.

## Install the executable

Download the package for your platform from
[GitHub Releases](https://github.com/trackd/Statusline/releases).

| Platform | Packages |
| --- | --- |
| Linux x64 | `.deb`, `.rpm`, `.tar.gz` |
| macOS ARM64 | `.pkg`, `.tar.gz` |
| Windows x64 | `.zip` |

GitHub displays a SHA256 digest beside each release asset and exposes it through
the release API. To verify a download, compare that digest with the output of
`sha256sum FILE` on Linux, `shasum -a 256 FILE` on macOS, or
`Get-FileHash FILE -Algorithm SHA256` in PowerShell.

For example, once you have downloaded the Linux `0.2.0` archive:

```bash
mkdir -p "$HOME/.local/bin"
tar -xzf statusline-0.2.0-linux-x64.tar.gz -C "$HOME/.local/bin"
export PATH="$HOME/.local/bin:$PATH"
statusline --version
```

Replace `0.2.0` with the version you downloaded. For Windows, extract the ZIP
and add its directory to `PATH`. The CLI's host-writable status output requires
Unix; the PowerShell module provides the Windows prompt and status-line integration.

For the macOS ARM64 installer:

```zsh
sudo installer -pkg ./statusline-0.2.0-osx-arm64.pkg -target /
```

The installer places the executable in `/usr/local/bin/statusline`, with the
license and examples under `/usr/local/share/doc/statusline`. It records the
receipt `io.github.trackd.statusline` and does not edit shell profiles. The installer
is unsigned and not notarized. The `.tar.gz` archive supports manual installation.

Linux x64 builds also include `.deb` and `.rpm` packages. Download the package
matching your distribution, then install it with dependency resolution:

```bash
# Debian / Ubuntu
sudo apt install ./statusline-0.2.0-linux-x64.deb

# Fedora
sudo dnf install ./statusline-0.2.0-linux-x64.rpm
```

The executable is installed as `/usr/bin/statusline`; example configurations
are under `/usr/share/doc/statusline/examples`. Installation leaves shell
profiles alone: initialize your chosen shell explicitly as described below.
Install these files directly; no APT or DNF repository is configured. The packages
are not GPG-signed.

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
See [project.json](examples/cli/project.json) for a two-row prompt with runtime providers.
Right and center prompt groups require `Rows: 2`.
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

## Configuration examples

Copy one of these JSON files to `~/statusline.json`, then initialize with
`--config ~/statusline.json`. Initialization reads the file; it does not install
a profile hook unless you also pass `--install`.

| Example | Theme and layout |
| --- | --- |
| [minimal.json](examples/cli/minimal.json) | Plain text: single-row Git prompt without Nerd Font glyphs |
| [compact.json](examples/cli/compact.json) | Slate: muted blue, rounded segments, short directory name, one row |
| [project.json](examples/cli/project.json) | Violet to teal: separate Powerline blocks, two rows, project runtimes on the right |
| [islands.json](examples/cli/islands.json) | Orchid to amber: separate pills, OS and project context, command results on the right |
| [contour.json](examples/cli/contour.json) | Electric blue to orange: two-row prompt and a full-width gradient status bar with OS, shell version, weather, uptime, CPU, and memory |

From a repository checkout, for example:

```bash
cp docs/examples/cli/project.json ~/statusline.json
eval "$(statusline init bash --config ~/statusline.json)"
```

Use `init zsh` with the same `eval` syntax, or
`statusline init fish --config ~/statusline.json | source` in Fish.
Runtime and project blocks appear when applicable to the current directory.
Transient prompts require Zsh or Fish 4.1 or newer; Bash retains its full prompt.

The Contour example explicitly enables `HostWritable`. Change `WeatherLocation`
to your location; the weather block requests data from wttr.in. Its status bar
uses the blue-to-orange gradient across the entire row, while the prompt keeps
separate blocks. Use a Nerd Font, or select `IconSet: "None"` with `Style: "Plain"`
for a layout without Powerline glyphs. Unknown JSON properties are rejected.

These are complete configurations, rather than named themes built into the CLI.
Mix their styles, colors, and block selections to build your own layout. Only the
Contour example enables a live status bar; the other examples work as prompts.

## Supported blocks

Use these names in `Left`, `Center`, or `Right`, either at the top level for the
prompt or inside `StatusLine` for the bar. Names are case-insensitive.

| Category | Blocks |
| --- | --- |
| Command and location | `Directory`, `Git`, `Status`, `Duration`, `Elevation` |
| Shell and machine | `Shell`, `Os`, `User`, `Host` |
| Live metrics | `Cpu`, `Gpu`, `SystemMemory`, `Network`, `Battery` |
| Time and weather | `Clock`, `Date`, `Uptime`, `Weather` |
| Project and runtimes | `Package`, `VirtualEnv`, `Node`, `Python`, `Dotnet`, `Go`, `Rust`, `Java`, `Ruby` |
| Cloud and containers | `Aws`, `Azure`, `Kubernetes`, `Docker` |

`Shell` shows the running shell and its version, supplied by the Bash, Zsh, or
Fish adapter at initialization. For example, `"Left": ["Os", "Shell", "Host"]`
works well in `StatusLine`. Standalone renders outside an initialized session
omit this block when shell information is unavailable.

Blocks appear when their data is available; runtime, project, cloud, battery,
and GPU blocks depend on the current directory, environment, tools, or hardware.
Set `WeatherLocation` to select the weather location. Slow providers refresh
asynchronously in an initialized session.

The PowerShell-only blocks `PowerShell`, `ProcessMemory`, `Errors`, `StackCount`,
`NestedPrompt`, `Runspaces`, and `HistoryId` are unavailable in the CLI.

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

HostWritable requires a supported Unix terminal. Ghostty does not
support this display; its prompt integration remains available. Windows users
can use file output.

### Contour over SSH

The remote shell may have a generic `TERM` and no `TERM_PROGRAM`. If you know the
connection ends in Contour, export its identity on the remote side before init:

```bash
export TERM_PROGRAM=contour
eval "$(statusline init bash --config ~/statusline.json)"
```

This supplies terminal identity to detection; it cannot add missing terminal
capabilities or repair escape sequences filtered along the connection.

### File output

Add `--status-file` to initialization to write an atomically replaced status file:

```fish
statusline init fish --config /absolute/path/prompt.json --status-file /tmp/statusline.txt | source
```

The same option works with Bash and Zsh. Use a separate file for each session and
an existing parent directory. Contour's command-based indicator can read the file;
see [file-output configuration](Configuration.md#file-output-and-contour-program).
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
settings are also available.

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
