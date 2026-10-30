using System;
using System.IO;
using System.Text;

public sealed class FailingTerminalWriter : TextWriter {
    public override Encoding Encoding => Encoding.UTF8;
    public override void Write(string value) => throw new IOException("Terminal write failed for regression test.");
    public override void Write(ReadOnlySpan<char> buffer) => throw new IOException("Terminal write failed for regression test.");
}
