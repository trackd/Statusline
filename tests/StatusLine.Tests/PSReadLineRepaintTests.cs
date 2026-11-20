using Xunit;

namespace StatusLine.Tests;

public sealed class PSReadLineRepaintTests {
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void IdleRefreshRequiresActiveUnacceptedInput(bool ready, bool accepted, bool expected) {
        Editor.SetState(ready, accepted);
        Assert.Equal(expected, PromptSession.CanRefreshPrompt(typeof(Editor)));
    }

    [Fact]
    public void UnknownEditorLayoutDisablesIdleRefresh() {
        Assert.False(PromptSession.CanRefreshPrompt(typeof(object)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void IdleRefreshDefersWhileEditorOwnsVisualSelection(int count) {
        Editor.SetState(true, false, count);
        Assert.False(PromptSession.CanRefreshPrompt(typeof(Editor)));
        Editor.SetState(true, false);
        Assert.True(PromptSession.CanRefreshPrompt(typeof(Editor)));
    }

    [Fact]
    public void PostAcceptanceTransientRepaintRemainsAvailable() {
        Editor.SetState(true, true);
        Assert.False(PromptSession.CanRefreshPrompt(typeof(Editor)));
        Assert.True(PromptSession.GetDeferredRepaint(typeof(Editor))!());
        Assert.Equal(1, Editor.RenderCount);
    }

    // Mirrors the private fields consumed by the adapter; accessed through compiled reflection.
#pragma warning disable CS0414
    private sealed class Editor {
        private static Editor _singleton = new();
        private bool _readLineReady;
        private bool _inputAccepted;
        private bool _waitingToRender = true;
        private int _visualSelectionCommandCount;
        internal static int RenderCount { get; private set; }
        internal static void SetState(bool ready, bool accepted, int selection = 0) {
            _singleton = new() { _readLineReady = ready, _inputAccepted = accepted, _visualSelectionCommandCount = selection };
            RenderCount = 0;
        }
        private void Render(bool force) {
            if (force && _waitingToRender) { RenderCount++; _waitingToRender = false; }
        }
    }
#pragma warning restore CS0414
}
