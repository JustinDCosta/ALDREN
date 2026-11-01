using System.Runtime.InteropServices;

namespace S7.Network.Nlm;

// Network List Manager COM interfaces (netlistmgr.idl), declared up to the last method used, in vtable order.

internal enum NlmEnumNetwork
{
    Connected = 1,
}

internal enum NlmNetworkCategory
{
    Public = 0,
    Private = 1,
    Domain = 2,
}

[ComImport, Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface INetworkListManager
{
    IEnumNetworks GetNetworks(NlmEnumNetwork flags);
}

[ComImport, Guid("DCB00003-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IEnumNetworks
{
    object NewEnum { [return: MarshalAs(UnmanagedType.IUnknown)] get; }

    [PreserveSig]
    int Next(uint count, out INetwork? network, out uint fetched);
}

[ComImport, Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface INetwork
{
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetName();

    void SetName([MarshalAs(UnmanagedType.BStr)] string name);

    [return: MarshalAs(UnmanagedType.BStr)]
    string GetDescription();

    void SetDescription([MarshalAs(UnmanagedType.BStr)] string description);

    Guid GetNetworkId();

    int GetDomainType();

    IEnumNetworkConnections GetNetworkConnections();

    void GetTimeCreatedAndConnected(out uint lowCreated, out uint highCreated, out uint lowConnected, out uint highConnected);

    bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }

    bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }

    int GetConnectivity();

    NlmNetworkCategory GetCategory();
}

[ComImport, Guid("DCB00006-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IEnumNetworkConnections
{
    object NewEnum { [return: MarshalAs(UnmanagedType.IUnknown)] get; }

    [PreserveSig]
    int Next(uint count, out INetworkConnection? connection, out uint fetched);
}

[ComImport, Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface INetworkConnection
{
    INetwork GetNetwork();

    bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }

    bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }

    int GetConnectivity();

    Guid GetConnectionId();

    Guid GetAdapterId();
}
