using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;


public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            System.Console.WriteLine("Usage: webcil-writer <input> <output>");
            return;
        }
        Console.WriteLine("size of WCHeader = {0}", SizeOfHeader());
        string inputPath = args[0];
        string outputPath = args[1];

        using (var peReader = new PEReader(System.IO.File.Open(inputPath, FileMode.Open)))
        {
            DumpPE(peReader);
            WebCil.WCHeader header = new WebCil.WCHeader();
            FillHeader(peReader, ref header);
        }
    }

    public unsafe static int SizeOfHeader()
    {
        return sizeof(WebCil.WCHeader);
    }

    public unsafe static void FillHeader(PEReader peReader, ref WebCil.WCHeader header, out ImmutableArray<WebCil.SectionHeaderBuilder> sectionsHeaders)
    {
        var headers = peReader.PEHeaders;
        var peHeader = headers.PEHeader!;
        var coffHeader = headers.CoffHeader!;
        var corHeader = headers.CorHeader!;
        var sections = headers.SectionHeaders;
        header.id[0] = (byte)'W';
        header.id[1] = (byte)'C';
        header.version = WebCil.Constants.WC_VERSION;
        header.reserved0 = 0;
        header.metadata_rva = (uint)corHeader.MetadataDirectory.RelativeVirtualAddress;
        header.metadata_size = (uint)corHeader.MetadataDirectory.Size;
        header.cli_flags = (uint)corHeader.Flags;
        header.cli_entry_point = corHeader.EntryPointTokenOrRelativeVirtualAddress;
        header.pe_cli_header_rva = (uint)peHeader.CorHeaderTableDirectory.RelativeVirtualAddress;
        header.pe_cli_header_size = (uint)peHeader.CorHeaderTableDirectory.Size;

        int offset = SizeOfHeader();
        int startOfSection = offset;

        // TODO: write the sections, but adjust the raw data ptr to the offset after the WCHeader.
        ImmutableArray<WebCil.SectionHeaderBuilder>.Builder headerBuilder = ImmutableArray.CreateBuilder<WebCil.SectionHeaderBuilder>(coffHeader.NumberOfSections);
        foreach (var sectionHeader in sections)
        {
            var newHeader = new WebCil.SectionHeaderBuilder
            {
                VirtualSize = sectionHeader.VirtualSize,
                VirtualAddress = sectionHeader.VirtualAddress,
                SizeOfRawData = sectionHeader.SizeOfRawData,
                PointerToRawData = sectionHeader.PointerToRawData,
            };

            offset += 16; // sizeof(SectionHeader)
            headerBuilder.Add(newHeader);
        }
        // now adjust the raw data ptrs for the sections and also copy the section data
        // TODO


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
}


