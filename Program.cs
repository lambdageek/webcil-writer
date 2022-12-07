using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace WebCil.Writer;

record struct FilePosition(int Position)
{
    public static implicit operator FilePosition(int position) => new(position);

    public static FilePosition operator +(FilePosition left, int right) => new(left.Position + right);
}

sealed class SaveStreamPosition : IDisposable
{
    private readonly Stream _stream;
    private readonly long _position;

    public SaveStreamPosition(Stream stream)
    {
        _stream = stream;
        _position = stream.Position;
    }

    public void Dispose()
    {
        _stream.Position = _position;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            System.Console.WriteLine("Usage: writer <input> <output>");
            return;
        }
        Console.WriteLine("size of WCHeader = {0}", SizeOfHeader());
        string inputPath = args[0];
        string outputPath = args[1];

        using var inputStream = System.IO.File.Open(inputPath, FileMode.Open);
        ImmutableArray<CoffSectionHeaderBuilder> sectionsHeaders;
        ImmutableArray<SectionHeader> peSections;
        WCHeader header = new();
        using (var peReader = new PEReader(inputStream, PEStreamOptions.LeaveOpen))
        {
            DumpPE(peReader);
            FillHeader(peReader, ref header, out peSections, out sectionsHeaders);
        }

        using var outputStream = System.IO.File.Open(outputPath, FileMode.Create);
        WriteHeader(outputStream, header);
        WriteSectionHeaders(outputStream, sectionsHeaders);
        CopySections(outputStream, inputStream, peSections);

    }

    public unsafe static int SizeOfHeader()
    {
        return sizeof(WCHeader);
    }

    public unsafe static void FillHeader(PEReader peReader, ref WCHeader header, out ImmutableArray<SectionHeader> peSections, out ImmutableArray<CoffSectionHeaderBuilder> sectionsHeaders)
    {
        var headers = peReader.PEHeaders;
        var peHeader = headers.PEHeader!;
        var coffHeader = headers.CoffHeader!;
        var corHeader = headers.CorHeader!;
        var sections = headers.SectionHeaders;
        header.id[0] = (byte)'W';
        header.id[1] = (byte)'C';
        header.version = Constants.WC_VERSION;
        header.reserved0 = 0;
        header.coff_sections = (ushort)coffHeader.NumberOfSections;
        header.reserved1 = 0;
        header.metadata_rva = (uint)corHeader.MetadataDirectory.RelativeVirtualAddress;
        header.metadata_size = (uint)corHeader.MetadataDirectory.Size;
        header.cli_flags = (uint)corHeader.Flags;
        header.cli_entry_point = corHeader.EntryPointTokenOrRelativeVirtualAddress;
        header.pe_cli_header_rva = (uint)peHeader.CorHeaderTableDirectory.RelativeVirtualAddress;
        header.pe_cli_header_size = (uint)peHeader.CorHeaderTableDirectory.Size;

        // current logical position in the output file
        FilePosition pos = SizeOfHeader();
        // position of the current section in the output file
        // initially it's after all the section headers
        FilePosition curSectionPos = pos + sizeof(CoffSectionHeaderBuilder) * coffHeader.NumberOfSections;

        // TODO: write the sections, but adjust the raw data ptr to the offset after the WCHeader.
        ImmutableArray<CoffSectionHeaderBuilder>.Builder headerBuilder = ImmutableArray.CreateBuilder<CoffSectionHeaderBuilder>(coffHeader.NumberOfSections);
        foreach (var sectionHeader in sections)
        {
            var newHeader = new CoffSectionHeaderBuilder
            (
                virtualSize: sectionHeader.VirtualSize,
                virtualAddress: sectionHeader.VirtualAddress,
                sizeOfRawData: sectionHeader.SizeOfRawData,
                pointerToRawData: curSectionPos.Position
            );

            pos += sizeof(CoffSectionHeaderBuilder);
            curSectionPos += sectionHeader.SizeOfRawData;
            headerBuilder.Add(newHeader);
        }

        peSections = sections;
        sectionsHeaders = headerBuilder.ToImmutable();
    }

    public static void DumpPE(PEReader peReader)
    {
        var headers = peReader.PEHeaders;
        Console.WriteLine($"metadata start {headers.MetadataStartOffset} size {headers.MetadataSize}");
        var sections = headers.SectionHeaders;
        foreach (var section in sections)
        {
            Console.WriteLine($"section {section.Name} 0x{section.VirtualAddress:x8} {section.VirtualSize} {section.SizeOfRawData}");
        }

        DumpBytes(peReader.GetEntireImage().GetContent(headers.MetadataStartOffset, headers.MetadataSize));


        const int method1RVA = 0x00002050;
        int off = RvaToOffset(sections, method1RVA);
        Console.WriteLine("Method 1 header is in the file at offset 0x{0:x8}", off);
        ImmutableArray<byte> method1Header = peReader.GetEntireImage().GetContent(off, 1);
        Console.WriteLine("Method 1 header is {0:x2}", method1Header[0]);
        Console.WriteLine("Method 1 size is {0} bytes", method1Header[0] >> 2);
        DumpBytes(peReader.GetEntireImage().GetContent(off, 0x10));

    }

    public static int RvaToOffset(ImmutableArray<SectionHeader> sections, int rva)
    {
        foreach (var section in sections)
        {
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.VirtualSize)
            {
                int relativeOffset = rva - section.VirtualAddress;
                return relativeOffset + section.PointerToRawData;
            }
        }
        throw new System.Exception("RVA not found");
    }

    public static void DumpBytes(ImmutableArray<byte> bytes)
    {
        const int width = 16;
        char[] visible = new char[width];
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            Console.Write("{0:X2} ", bytes[i]);

            int off = i % width;
            if (b >= 32 && b < 127)
                visible[off] = (char)b;
            else
                visible[off] = '.';
            if (off == width - 1)
                Console.WriteLine("\t{0}", new string(visible));
        }
        if (bytes.Length % width != 0)
        {
            int pad = width - bytes.Length % width;
            for (int i = 0; i < pad; i++)
                Console.Write("   ");
            Console.WriteLine("\t{0}", new string(visible, 0, bytes.Length % width));
        }
    }

    static void WriteHeader(Stream s, WCHeader header)
    {
        // FIXME: fixup endianness
        if (!BitConverter.IsLittleEndian)
            throw new NotImplementedException();
        unsafe
        {
            byte* p = &header.id[0]; ;
            s.Write(new ReadOnlySpan<byte>(p, sizeof(WCHeader)));
        }
    }

    static void WriteSectionHeaders(Stream s, ImmutableArray<CoffSectionHeaderBuilder> sectionsHeaders)
    {
        // FIXME: fixup endianness
        if (!BitConverter.IsLittleEndian)
            throw new NotImplementedException();
        foreach (var sectionHeader in sectionsHeaders)
        {
            unsafe
            {
                byte* p = (byte*)&sectionHeader;
                s.Write(new ReadOnlySpan<byte>(p, sizeof(CoffSectionHeaderBuilder)));
            }
        }
    }

    static void CopySections(Stream outStream, Stream inputStream, ImmutableArray<SectionHeader> peSections)
    {
        // endianness: ok, we're just copying from one stream to another
        foreach (var peHeader in peSections)
        {
            inputStream.Seek(peHeader.PointerToRawData, SeekOrigin.Begin);
            inputStream.CopyTo(outStream, peHeader.SizeOfRawData);
        }
    }

}


