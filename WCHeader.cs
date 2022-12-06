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

    public uint metadata_rva;
    public uint metadata_size;

    public uint cli_flags;
    public int cli_entry_point;

    // public ushort runtime_major;
    // public ushort runtime_minor;

    public uint pe_cli_header_rva;
    public uint pe_cli_header_size;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SectionHeaderBuilder
{
    public int VirtualSize;
    public int VirtualAddress;
    public int SizeOfRawData;
    public int PointerToRawData;
}