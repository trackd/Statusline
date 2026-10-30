using System;
using System.IO;
using System.Text;
using System.Threading;

public sealed class BlockingReviewWriter : TextWriter {
    public readonly ManualResetEventSlim Entered = new();
    public readonly ManualResetEventSlim Release = new();
    public override Encoding Encoding => Encoding.UTF8;
    public override void Write(string value) => Block();
    public override void Write(ReadOnlySpan<char> buffer) => Block();
    private void Block() { Entered.Set(); Release.Wait(); }
    protected override void Dispose(bool disposing) {
        if (disposing) { Release.Set(); Entered.Dispose(); Release.Dispose(); }
        base.Dispose(disposing);
    }
}
