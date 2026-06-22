using System.Buffers.Binary;
using System.Text;

namespace NetSearch.Core.Native;

public static class MftRecordParser
{
    private const uint Signature = 0x454C4946; // "FILE" little-endian
    private const uint AttrEnd = 0xFFFFFFFF;
    private const uint StandardInformation = 0x10, FileName = 0x30, Data = 0x80;

    public static bool TryParse(Span<byte> record, int bytesPerSector, out ParsedMftRecord result)
    {
        result = default;
        if (record.Length < 48) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(record) != Signature) return false;

        ushort usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[0x04..]);
        ushort usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record[0x06..]);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record[0x16..]);
        if ((flags & 0x01) == 0) return false;                 // not in use
        if (BinaryPrimitives.ReadInt64LittleEndian(record[0x20..]) != 0) return false; // extension record

        ApplyFixups(record, usaOffset, usaCount, bytesPerSector);

        bool isDir = (flags & 0x02) != 0;
        long size = 0, modified = 0, parent = 0;
        string? name = null; byte bestNs = 255;

        int pos = BinaryPrimitives.ReadUInt16LittleEndian(record[0x14..]);
        while (pos + 8 <= record.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(record[pos..]);
            if (type == AttrEnd) break;
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(pos + 4)..]);
            if (len <= 0 || pos + len > record.Length) break;
            byte nonResident = record[pos + 0x08];
            byte nameLen = record[pos + 0x09];

            if (type == StandardInformation && nonResident == 0)
            {
                int vo = BinaryPrimitives.ReadUInt16LittleEndian(record[(pos + 0x14)..]);
                modified = MftTime.FileTimeToUnixSeconds(
                    BinaryPrimitives.ReadInt64LittleEndian(record[(pos + vo + 0x08)..]));
            }
            else if (type == FileName && nonResident == 0)
            {
                int vo = BinaryPrimitives.ReadUInt16LittleEndian(record[(pos + 0x14)..]);
                int c = pos + vo;
                long p = BinaryPrimitives.ReadInt64LittleEndian(record[c..]) & 0xFFFFFFFFFFFFL;
                byte fnLen = record[c + 0x40];
                byte ns = record[c + 0x41];
                // Prefer Win32(1)/Win32&DOS(3) over POSIX(0) over DOS(2).
                int rank = ns switch { 1 => 0, 3 => 0, 0 => 1, _ => 2 };
                int bestRank = bestNs switch { 1 => 0, 3 => 0, 0 => 1, 255 => 9, _ => 2 };
                if (rank < bestRank)
                {
                    name = Encoding.Unicode.GetString(record.Slice(c + 0x42, fnLen * 2));
                    parent = p; bestNs = ns;
                }
            }
            else if (type == Data && nameLen == 0)
            {
                if (nonResident == 0)
                    size = BinaryPrimitives.ReadUInt32LittleEndian(record[(pos + 0x10)..]);
                else
                    size = BinaryPrimitives.ReadInt64LittleEndian(record[(pos + 0x30)..]);
            }
            pos += len;
        }

        if (name is null) return false;
        result = new ParsedMftRecord(isDir, parent, name, isDir ? 0 : size, modified);
        return true;
    }

    private static void ApplyFixups(Span<byte> record, int usaOffset, int usaCount, int bytesPerSector)
    {
        if (usaCount < 1) return;
        int sectors = usaCount - 1;
        for (var i = 0; i < sectors; i++)
        {
            int tail = (i + 1) * bytesPerSector - 2;
            int src = usaOffset + 2 + i * 2;
            if (tail + 2 > record.Length || src + 2 > record.Length) break;
            record[tail] = record[src];
            record[tail + 1] = record[src + 1];
        }
    }
}
