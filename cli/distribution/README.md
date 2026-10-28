# Native distribution plan

Publish versioned archives containing one executable per runtime identifier, with
SHA256 checksum files. Unix tar archives preserve executable permissions; Windows
uses ZIP. The module manifest remains the version source. Debug symbols can be
separate optional assets.

Start with a Homebrew tap for Linux/macOS and a Scoop bucket for Windows. WinGet
portable packages can follow. Consider deb/rpm once the supported Linux/glibc
baseline and architectures are settled. Build Linux assets on the oldest supported
distribution: Native AOT does not make newer system-library dependencies portable.

Package install and upgrade must not edit shell profiles or start workers. Users
explicitly run `statusline init bash --install`, `statusline init zsh --install`, or
`statusline init fish --install`. `--uninstall` removes the managed profile block.
Installed hooks resolve `statusline` through PATH at shell startup, avoiding a
versioned package-manager path. Already running shells need reinitialization after
an upgrade if the old executable path is removed.

No package submissions or package repositories have been published. PowerShell
Gallery distribution of the single-DLL module remains independent.

References: [Homebrew formula cookbook](https://docs.brew.sh/Formula-Cookbook),
[Scoop manifests](https://github.com/ScoopInstaller/Scoop/wiki/App-Manifests),
[WinGet manifests](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest),
[Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).
