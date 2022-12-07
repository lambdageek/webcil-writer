using System.Runtime.InteropServices;

namespace WebCil;

/// <summary>
/// The header of a WebCIL file.
/// </summary>
///
/// <remarks>
/// The header is a subset of the PE, COFF and CLI headers that are needed by the mono runtime to load managed assemblies.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct WCHeader
{
    public fixed byte id[2]; // 'W' 'C'
    public byte version;
    public byte reserved0; // 0
    // 4 bytes
    public int reserved1; // 0
    // 8 bytes

    public uint metadata_rva;
    public uint metadata_size;
    // 16 bytes

    public uint cli_flags;
    public int cli_entry_point;
    // 24 bytes

    public uint pe_cli_header_rva;
    public uint pe_cli_header_size;
    // 32 bytes
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CoffSectionHeaderBuilder
{
    public readonly int VirtualSize;
    public readonly int VirtualAddress;
    public readonly int SizeOfRawData;
    public readonly int PointerToRawData;

    public CoffSectionHeaderBuilder(int virtualSize, int virtualAddress, int sizeOfRawData, int pointerToRawData)
    {
        VirtualSize = virtualSize;
        VirtualAddress = virtualAddress;
        SizeOfRawData = sizeOfRawData;
        PointerToRawData = pointerToRawData;
    }
}