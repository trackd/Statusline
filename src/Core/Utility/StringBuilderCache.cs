using System.Text;

namespace StatusLine.Utility;

internal static class StringBuilderCache {
    private const int MaximumRetainedCapacity = 16384;
    // Rendering can nest three group builders inside the final output builder.
    [ThreadStatic] private static StringBuilder? first;
    [ThreadStatic] private static StringBuilder? second;
    [ThreadStatic] private static StringBuilder? third;
    [ThreadStatic] private static StringBuilder? fourth;

    public static StringBuilder Rent(int capacity = 256) {
        StringBuilder? builder = first;
        first = second;
        second = third;
        third = fourth;
        fourth = null;
        if (builder is null) {
            return new StringBuilder(capacity);
        }

        builder.EnsureCapacity(capacity);
        return builder;
    }

    public static void Return(StringBuilder builder) {
        if (builder.Capacity > MaximumRetainedCapacity) {
            return;
        }

        builder.Clear();
        if (first is null) {
            first = builder;
        }
        else if (second is null) {
            second = builder;
        }
        else if (third is null) {
            third = builder;
        }
        else {
            fourth ??= builder;
        }
    }

    public static string Complete(StringBuilder builder) {
        try {
            return builder.ToString();
        }
        finally {
            Return(builder);
        }
    }
}
