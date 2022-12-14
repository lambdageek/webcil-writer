// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

#if MSBUILD_TASK
using Microsoft.Build.Utilities;
using Microsoft.WebAssembly.Build.Tasks.WebCil;

namespace Microsoft.WebAssembly.Build.Tasks;

#else
using Microsoft.Extensions.Logging;
using Microsoft.WebAssembly.Webcil.Metadata;

namespace Microsoft.WebAssembly.Metadata;
#endif


/// <summary>
/// Reads a .NET assembly in a normal PE COFF file and writes it out as a Webcil file
/// </summary>
public class WebcilWriter
{

    // Interesting stuff we've learned about the input PE file
    public record PEFileInfo(
        // The sections in the PE file
        ImmutableArray<SectionHeader> SectionHeaders,
        // The location of the debug directory entries
        DirectoryEntry DebugTableDirectory,
        // The file offset of the sections, following the section directory
        FilePosition SectionStart,
        // The debug directory entries
        ImmutableArray<DebugDirectoryEntry> DebugDirectoryEntries
        );

    // Intersting stuff we know about the webcil file we're writing
    public record WCFileInfo(
        // The header of the webcil file
        WCHeader Header,
        // The section directory of the webcil file
        ImmutableArray<WebcilSectionHeader> SectionHeaders,
        // The file offset of the sections, following the section directory
        FilePosition SectionStart
    );

    private readonly string _inputPath;
    private readonly string _outputPath;

#if MSBUILD_TASK
    private TaskLoggingHelper Log { get; }
    public WebcilWriter(string inputPath, string outputPath, TaskLoggingHelper logger)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;
        Log = logger;
    }
#else

    private ILogger Log { get; }
    public WebcilWriter(string inputPath, string outputPath, ILogger logger)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;
        Log = logger;
    }
#endif


    public void Write()
    {
        Log.LogInformation("Writing Webcil (input {_inputPath}) output to {_outputPath}", _inputPath, _outputPath);

        using var inputStream = File.Open(_inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        PEFileInfo peInfo;
        WCFileInfo wcInfo;
        using (var peReader = new PEReader(inputStream, PEStreamOptions.LeaveOpen))
        {
            // DumpPE(peReader);
            GatherInfo(peReader, out wcInfo, out peInfo);
        }

        using var outputStream = File.Open(_outputPath, FileMode.Create, FileAccess.Write);
        WriteHeader(outputStream, wcInfo.Header);
        WriteSectionHeaders(outputStream, wcInfo.SectionHeaders);
        CopySections(outputStream, inputStream, peInfo.SectionHeaders);
        var wcDebugDirectoryEntries = FixupDebugDirectoryEntries(peInfo, wcInfo);
        OverwriteDebugDirectoryEntries(outputStream, wcInfo, wcDebugDirectoryEntries);
        outputStream.Flush();
    }

    public record struct FilePosition(int Position)
    {
        public static implicit operator FilePosition(int position) => new(position);

        public static FilePosition operator +(FilePosition left, int right) => new(left.Position + right);
    }

    private static unsafe int SizeOfHeader()
    {
        return sizeof(WCHeader);
    }

    public unsafe void GatherInfo(PEReader peReader, out WCFileInfo wcInfo, out PEFileInfo peInfo)
    {
        var headers = peReader.PEHeaders;
        var peHeader = headers.PEHeader!;
        var coffHeader = headers.CoffHeader!;
        var sections = headers.SectionHeaders;
        WCHeader header;
        header.id[0] = (byte)'W';
        header.id[1] = (byte)'C';
        header.version = Constants.WC_VERSION;
        header.reserved0 = 0;
        header.coff_sections = (ushort)coffHeader.NumberOfSections;
        header.reserved1 = 0;
        header.pe_cli_header_rva = (uint)peHeader.CorHeaderTableDirectory.RelativeVirtualAddress;
        header.pe_cli_header_size = (uint)peHeader.CorHeaderTableDirectory.Size;
        header.pe_debug_rva = (uint)peHeader.DebugTableDirectory.RelativeVirtualAddress;
        header.pe_debug_size = (uint)peHeader.DebugTableDirectory.Size;
        Log.LogDebug("pe_debug {PEDebug}", peHeader.DebugTableDirectory);

        // current logical position in the output file
        FilePosition pos = SizeOfHeader();
        // position of the current section in the output file
        // initially it's after all the section headers
        FilePosition curSectionPos = pos + sizeof(WebcilSectionHeader) * coffHeader.NumberOfSections;

        FilePosition firstWCSection = curSectionPos;
        FilePosition firstPESection = 0;

        ImmutableArray<WebcilSectionHeader>.Builder headerBuilder = ImmutableArray.CreateBuilder<WebcilSectionHeader>(coffHeader.NumberOfSections);
        foreach (var sectionHeader in sections)
        {
            // The first section is the one with the lowest file offset
            if (firstPESection.Position == 0)
            {
                firstPESection = sectionHeader.PointerToRawData;
            }
            else
            {
                firstPESection = Math.Min(firstPESection.Position, sectionHeader.PointerToRawData);
            }

            var newHeader = new WebcilSectionHeader
            (
                virtualSize: sectionHeader.VirtualSize,
                virtualAddress: sectionHeader.VirtualAddress,
                sizeOfRawData: sectionHeader.SizeOfRawData,
                pointerToRawData: curSectionPos.Position
            );

            pos += sizeof(WebcilSectionHeader);
            curSectionPos += sectionHeader.SizeOfRawData;
            headerBuilder.Add(newHeader);
        }

        peInfo = new PEFileInfo(SectionHeaders: sections,
                                DebugTableDirectory: peHeader.DebugTableDirectory,
                                SectionStart: firstPESection,
                                DebugDirectoryEntries: peReader.ReadDebugDirectory());

        wcInfo = new WCFileInfo(Header: header,
                                SectionHeaders: headerBuilder.MoveToImmutable(),
                                SectionStart: firstWCSection);
    }

    private static void WriteHeader(Stream s, WCHeader header)
    {
        WriteStructure(s, header);
    }

    private static void WriteSectionHeaders(Stream s, ImmutableArray<WebcilSectionHeader> sectionsHeaders)
    {
        // FIXME: fixup endianness
        if (!BitConverter.IsLittleEndian)
            throw new NotImplementedException();
        foreach (var sectionHeader in sectionsHeaders)
        {
            WriteSectionHeader(s, sectionHeader);
        }
    }

    private static void WriteSectionHeader(Stream s, WebcilSectionHeader sectionHeader)
    {
        WriteStructure(s, sectionHeader);
    }

#if NETCOREAPP2_1_OR_GREATER
    private static void WriteStructure<T>(Stream s, T structure)
        where T : unmanaged
    {
        // FIXME: fixup endianness
        if (!BitConverter.IsLittleEndian)
            throw new NotImplementedException();
        unsafe
        {
            byte* p = (byte*)&structure;
            s.Write(new ReadOnlySpan<byte>(p, sizeof(T)));
        }
    }
#else
    private static void WriteStructure<T>(Stream s, T structure)
        where T : unmanaged
    {
        // FIXME: fixup endianness
        if (!BitConverter.IsLittleEndian)
            throw new NotImplementedException();
        int size = Marshal.SizeOf<T>();
        byte[] buffer = new byte[size];
        IntPtr ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(structure, ptr, false);
            Marshal.Copy(ptr, buffer, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        s.Write(buffer, 0, size);
    }
#endif

    private static void CopySections(Stream outStream, Stream inputStream, ImmutableArray<SectionHeader> peSections)
    {
        // endianness: ok, we're just copying from one stream to another
        foreach (var peHeader in peSections)
        {
            var buffer = new byte[peHeader.SizeOfRawData];
            inputStream.Seek(peHeader.PointerToRawData, SeekOrigin.Begin);
            inputStream.ReadExactly(buffer);
            outStream.Write(buffer);
        }
    }

    private static FilePosition GetPositionOfRelativeVirtualAddress(ImmutableArray<WebcilSectionHeader> wcSections, uint relativeVirtualAddress)
    {
        foreach (var section in wcSections)
        {
            if (relativeVirtualAddress >= section.VirtualAddress && relativeVirtualAddress < section.VirtualAddress + section.VirtualSize)
            {
                return section.PointerToRawData + (int)(relativeVirtualAddress - section.VirtualAddress);
            }
        }

        throw new InvalidOperationException("relative virtual address not in any section");
    }

    // Given a physical file offset, return the section and the offset within the section.
    private static (WebcilSectionHeader section, int offset) GetSectionFromFileOffset(ImmutableArray<WebcilSectionHeader> peSections, FilePosition fileOffset)
    {
        foreach (var section in peSections)
        {
            if (fileOffset.Position >= section.PointerToRawData && fileOffset.Position < section.PointerToRawData + section.SizeOfRawData)
            {
                return (section, fileOffset.Position - section.PointerToRawData);
            }
        }

        throw new InvalidOperationException("file offset not in any section (Webcil)");
    }

    private static void GetSectionFromFileOffset(ImmutableArray<SectionHeader> sections, FilePosition fileOffset)
    {
        foreach (var section in sections)
        {
            if (fileOffset.Position >= section.PointerToRawData && fileOffset.Position < section.PointerToRawData + section.SizeOfRawData)
            {
                return;
            }
        }

        throw new InvalidOperationException($"file offset {fileOffset.Position} not in any section (PE)");
    }

    // Make a new set of debug directory entries that
    // have their data pointers adjusted to be relative to the start of the webcil file.
    // This is necessary because the debug directory entires in the PE file are relative to the start of the PE file,
    // and a PE header is bigger than a webcil header.
    private static ImmutableArray<DebugDirectoryEntry> FixupDebugDirectoryEntries(PEFileInfo peInfo, WCFileInfo wcInfo)
    {
        int dataPointerAdjustment = peInfo.SectionStart.Position - wcInfo.SectionStart.Position;
        ImmutableArray<DebugDirectoryEntry> entries = peInfo.DebugDirectoryEntries;
        ImmutableArray<DebugDirectoryEntry>.Builder newEntries = ImmutableArray.CreateBuilder<DebugDirectoryEntry>(entries.Length);
        foreach (var entry in entries)
        {
            DebugDirectoryEntry newEntry;
            if (entry.Type == DebugDirectoryEntryType.Reproducible)
            {
                // this entry doesn't have an associated data pointer, so just copy it
                newEntry = entry;
            }
            else
            {
                // the "pointer" field is a file offset, find the corresponding entry in the Webcil file and overwrite with the correct file position
                var newDataPointer = entry.DataPointer - dataPointerAdjustment;
                newEntry = new DebugDirectoryEntry(entry.Stamp, entry.MajorVersion, entry.MinorVersion, entry.Type, entry.DataSize, entry.DataRelativeVirtualAddress, newDataPointer);
                GetSectionFromFileOffset(peInfo.SectionHeaders, entry.DataPointer);
                // validate that the new entry is in some section
                GetSectionFromFileOffset(wcInfo.SectionHeaders, newDataPointer);
            }
            newEntries.Add(newEntry);
        }
        return newEntries.MoveToImmutable();
    }

    private static void OverwriteDebugDirectoryEntries(Stream s, WCFileInfo wcInfo, ImmutableArray<DebugDirectoryEntry> entries)
    {
        s.Seek(GetPositionOfRelativeVirtualAddress(wcInfo.SectionHeaders, wcInfo.Header.pe_debug_rva).Position, SeekOrigin.Begin);
        using var writer = new BinaryWriter(s, System.Text.Encoding.UTF8, leaveOpen: true);
        // endianness: ok, we're just copying from one stream to another
        foreach (var entry in entries)
        {
            WriteDebugDirectoryEntry(writer, entry);
        }
        writer.Flush();
        // TODO check that we overwrite with the same size as the original

        // restore the stream position
        s.Seek(0, SeekOrigin.End);
    }

    private static void WriteDebugDirectoryEntry(BinaryWriter writer, DebugDirectoryEntry entry)
    {
        writer.Write((uint)0); // Characteristics
        writer.Write(entry.Stamp);
        writer.Write(entry.MajorVersion);
        writer.Write(entry.MinorVersion);
        writer.Write((uint)entry.Type);
        writer.Write(entry.DataSize);
        writer.Write(entry.DataRelativeVirtualAddress);
        writer.Write(entry.DataPointer);
    }
}
