using StatusLine.Blocks.Execution;
using Xunit;

namespace StatusLine.Tests;

public sealed class PreparedSymbolTests {
    [Fact]
    public void PromptLayoutCachesSanitizedSymbolsAndRecompilesChanges() {
        var configuration = new PromptConfiguration {
            Left = [],
            Right = [],
            Input = [],
            Block = [],
            Rows = 2,
            ConnectRows = true,
            Symbol = "\x1b[31m>\x1b[0m",
            TransientSymbol = "\x1b[31mT\x1b[0m",
            ContextSymbol = "\x1b[31mC\x1b[0m",
            InputLinePrefix = "\x1b[31mI\x1b[0m",
        };
        var layout = new PromptLayout(configuration, false);
        Assert.Equal(" > ", layout.Symbol);
        Assert.Equal(" T ", layout.TransientSymbol);
        Assert.Equal(" C ", layout.ContextSymbol);
        Assert.Equal(" I ", layout.InputLinePrefix);
        string full = PromptRenderer.Render(layout, new BlockContext(), 40, transient: false);
        Assert.DoesNotContain("\x1b[31m", full);
        Assert.Contains(" I  > ", full);
        string transient = PromptRenderer.Render(layout, new BlockContext(), 40, transient: true);
        Assert.Contains("T", transient);
        Assert.DoesNotContain("\x1b[31m", transient);
        configuration = configuration with { Symbol = "#" };
        Assert.Equal(" > ", layout.Symbol);
        var updated = new PromptLayout(configuration, false);
        Assert.Contains(" I #", PromptRenderer.Render(updated, new BlockContext(), 40, transient: false));
    }
}
