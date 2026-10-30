using StatusLine.Blocks.Execution;
using StatusLine.Platform;
using Xunit;

namespace StatusLine.Tests;

public sealed class OperatingSystemIdentityTests {
    [Theory]
    [InlineData("PRETTY_NAME=\"Debian GNU/Linux 13 (trixie)\"\nVERSION_ID=13", "Debian GNU/Linux 13 (trixie)")]
    [InlineData("NAME='Example Linux'\nVERSION_ID=42", "Example Linux 42")]
    [InlineData("NAME=Example\nVERSION=\"13 (stable)\"\nVERSION_ID=13", "Example 13 (stable)")]
    [InlineData("PRETTY_NAME=Old\nPRETTY_NAME=New", "New")]
    [InlineData("# ignored\nINVALID\n", "Linux")]
    [InlineData("PRETTY_NAME=\"A \\\"quoted\\\" distro\"", "A \"quoted\" distro")]
    [InlineData("PRETTY_NAME='Literal $value'", "Literal $value")]
    public void LinuxUsesProductMetadataWithoutExecutingShell(string source, string expected) => Assert.Equal(expected, OperatingSystemIdentity.ParseLinux(source));

    [Theory]
    [InlineData("Windows 10 Pro", "Client", "26H2", "26300", "9711", "Windows 11 26H2", "26300.9711")]
    [InlineData("Windows Server 2025 Standard", "Server", "24H2", "26100", "1", "Windows Server 2025 Standard 24H2", "26100.1")]
    [InlineData("Windows 10 Pro", "Client", "22H2", "19045", "1", "Windows 10 22H2", "19045.1")]
    public void WindowsSeparatesProductReleaseAndFullBuild(string product, string type, string release,
        string build, string revision, string expected, string fullBuild) {
        var identity = OperatingSystemIdentity.Windows(product, type, release, build, revision);
        Assert.Equal(expected, identity.DisplayName);
        Assert.Equal(fullBuild, identity.Build);
    }

    [Fact]
    public void CheapIdentityBlocksDoNotCreateRefreshCollectors() {
        Assert.IsAssignableFrom<IImmediateBlock>(BlockCatalog.Get(StatusLineBlockKind.Host));
        Assert.IsAssignableFrom<IImmediateBlock>(BlockCatalog.Get(StatusLineBlockKind.User));
        Assert.Same(OperatingSystemIdentity.Current, OperatingSystemIdentity.Current);
        Assert.False(string.IsNullOrWhiteSpace(OperatingSystemIdentity.Current.DisplayName));
    }
}
