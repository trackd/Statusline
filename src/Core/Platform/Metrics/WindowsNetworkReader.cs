using System.Runtime.InteropServices;
using StatusLine.Interop;

namespace StatusLine.Metrics;

internal static class WindowsNetworkReader {
    private static readonly int RowsOffset = Marshal.OffsetOf<NativeMethods.InterfaceTable2>(nameof(NativeMethods.InterfaceTable2.First)).ToInt32();

    internal static unsafe (long Received, long Sent)? Read() {
        if (NativeMethods.GetIfTable2(out nint table) != 0) {
            return null;
        }

        try {
            uint count = ((NativeMethods.InterfaceTable2*)table)->Count;
            if (count > 1_000_000) {
                return null;
            }

            ReadOnlySpan<NativeMethods.InterfaceRow2> rows = new((byte*)table + RowsOffset, (int)count);
            return Sum(rows);
        }
        finally {
            NativeMethods.FreeMibTable(table);
        }
    }

    internal static (long Received, long Sent) Sum(ReadOnlySpan<NativeMethods.InterfaceRow2> rows) {
        ulong received = 0, sent = 0;
        foreach (ref readonly NativeMethods.InterfaceRow2 row in rows) {
            if (row.OperationalStatus == 1 && row.Type != 24) {
                received += row.InOctets;
                sent += row.OutOctets;
            }
        }

        return ((long)Math.Min(received, long.MaxValue), (long)Math.Min(sent, long.MaxValue));
    }
}
