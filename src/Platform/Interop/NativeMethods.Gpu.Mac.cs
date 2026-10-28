using System.Runtime.InteropServices;

namespace StatusLine.Interop;

internal static partial class NativeMethods {
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string IOReport = "/usr/lib/libIOReport.dylib";
    [LibraryImport(CoreFoundation)] internal static partial void CFRelease(nint value);
    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint CFStringCreateWithCString(nint allocator, string value, uint encoding);
    [LibraryImport(CoreFoundation)] internal static unsafe partial byte CFStringGetCString(nint value, byte* buffer, nint size, uint encoding);
    [LibraryImport(CoreFoundation)] internal static partial nint CFDictionaryGetValue(nint dictionary, nint key);
    [LibraryImport(CoreFoundation)] internal static partial nint CFArrayGetCount(nint array);
    [LibraryImport(CoreFoundation)] internal static partial nint CFArrayGetValueAtIndex(nint array, nint index);
    [LibraryImport(IOReport)] internal static partial nint IOReportCopyChannelsInGroup(nint group, nint subgroup, ulong a, ulong b, ulong c);
    [LibraryImport(IOReport)] internal static partial nint IOReportCreateSubscription(nint allocator, nint channels, out nint subscribed, ulong id, nint options);
    [LibraryImport(IOReport)] internal static partial nint IOReportCreateSamples(nint subscription, nint channels, nint options);
    [LibraryImport(IOReport)] internal static partial nint IOReportCreateSamplesDelta(nint previous, nint current, nint options);
    [LibraryImport(IOReport)] internal static partial nint IOReportChannelGetChannelName(nint channel);
    [LibraryImport(IOReport)] internal static partial int IOReportStateGetCount(nint channel);
    [LibraryImport(IOReport)] internal static partial nint IOReportStateGetNameForIndex(nint channel, int index);
    [LibraryImport(IOReport)] internal static partial long IOReportStateGetResidency(nint channel, int index);
}
