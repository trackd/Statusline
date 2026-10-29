using System.Runtime.InteropServices;

namespace StatusLine.Interop;

internal static partial class NativeMethods {
    // netioapi.h MIB_IF_ROW2. Keep complete native layout so the table stride follows the ABI.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal unsafe struct InterfaceRow2 {
        public ulong Luid;
        public uint Index;
        public Guid Id;
        public fixed char Alias[257];
        public fixed char Description[257];
        public uint PhysicalAddressLength;
        public fixed byte PhysicalAddress[32];
        public fixed byte PermanentPhysicalAddress[32];
        public uint Mtu, Type, TunnelType, MediaType, PhysicalMediumType, AccessType, DirectionType;
        public byte StatusFlags;
        public uint OperationalStatus, AdminStatus, MediaConnectState;
        public Guid NetworkId;
        public uint ConnectionType;
        public ulong TransmitSpeed, ReceiveSpeed, InOctets, InUnicastPackets, InNonUnicastPackets;
        public ulong InDiscards, InErrors, InUnknownProtocols, InUnicastOctets, InMulticastOctets, InBroadcastOctets;
        public ulong OutOctets, OutUnicastPackets, OutNonUnicastPackets, OutDiscards, OutErrors;
        public ulong OutUnicastOctets, OutMulticastOctets, OutBroadcastOctets, OutQueueLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InterfaceTable2 {
        public uint Count;
        public InterfaceRow2 First;
    }

    [LibraryImport("iphlpapi.dll")]
    internal static partial uint GetIfTable2(out nint table);
    [LibraryImport("iphlpapi.dll")]
    internal static partial void FreeMibTable(nint table);
}
