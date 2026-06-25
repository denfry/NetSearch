using System.Text;

namespace NetSearch.Core.Tests.Native;

// Builds a minimal but structurally valid NTFS FILE record for parser tests.
internal sealed class MftRecordBuilder
{
    private readonly int _recordSize, _sectorSize;
    private bool _isDir, _inUse = true;
    private long _baseRecord;
    private readonly List<byte[]> _attrs = new();

    public MftRecordBuilder(int recordSize = 1024, int bytesPerSector = 512)
    { _recordSize = recordSize; _sectorSize = bytesPerSector; }

    public MftRecordBuilder AsDirectory() { _isDir = true; return this; }
    public MftRecordBuilder InUse(bool v) { _inUse = v; return this; }
    public MftRecordBuilder BaseRecord(long r) { _baseRecord = r; return this; }

    public MftRecordBuilder StandardInformation(long modifiedFileTime)
    {
        var content = new byte[0x30];
        BitConverter.GetBytes(modifiedFileTime).CopyTo(content, 0x08); // altered/last-write
        _attrs.Add(Attr(0x10, content));
        return this;
    }

    public MftRecordBuilder FileName(long parentRecordNumber, string name, byte ns = 1)
    {
        var nmeBytes = Encoding.Unicode.GetBytes(name);
        var content = new byte[0x42 + nmeBytes.Length];
        BitConverter.GetBytes(parentRecordNumber & 0xFFFFFFFFFFFFL).CopyTo(content, 0x00);
        content[0x40] = (byte)name.Length;
        content[0x41] = ns;
        nmeCopy(nmeBytes, content);
        _attrs.Add(Attr(0x30, content));
        return this;

        static void nmeCopy(byte[] src, byte[] dst) => src.CopyTo(dst, 0x42);
    }

    public MftRecordBuilder DataResident(long size)
    {
        _attrs.Add(Attr(0x80, new byte[(int)size], residentValueLength: (uint)size));
        return this;
    }

    public MftRecordBuilder DataNonResident(long realSize)
    {
        // Non-resident header (no data runs needed for size): RealSize at content-header 0x30.
        var header = new byte[0x40];
        BitConverter.GetBytes(realSize).CopyTo(header, 0x30);
        _attrs.Add(Attr(0x80, header, nonResident: true));
        return this;
    }

    public byte[] Build()
    {
        var rec = new byte[_recordSize];
        Encoding.ASCII.GetBytes("FILE").CopyTo(rec, 0);
        int sectors = _recordSize / _sectorSize;
        ushort usaOffset = 0x30, usaCount = (ushort)(sectors + 1);
        BitConverter.GetBytes(usaOffset).CopyTo(rec, 0x04);
        BitConverter.GetBytes(usaCount).CopyTo(rec, 0x06);
        BitConverter.GetBytes((ushort)1).CopyTo(rec, 0x10);  // sequence number
        BitConverter.GetBytes((ushort)(_baseRecord == 0 ? 1 : 0)).CopyTo(rec, 0x12);
        ushort flags = (ushort)((_inUse ? 0x01 : 0) | (_isDir ? 0x02 : 0));
        BitConverter.GetBytes(flags).CopyTo(rec, 0x16);
        BitConverter.GetBytes(_baseRecord).CopyTo(rec, 0x20);

        int firstAttr = usaOffset + usaCount * 2;
        firstAttr = (firstAttr + 7) & ~7; // 8-byte align
        BitConverter.GetBytes((ushort)firstAttr).CopyTo(rec, 0x14);

        int pos = firstAttr;
        foreach (var a in _attrs) { a.CopyTo(rec, pos); pos += a.Length; }
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(rec, pos); // terminator
        pos += 4;
        BitConverter.GetBytes((uint)pos).CopyTo(rec, 0x18);  // BytesInUse

        // Update sequence array: USN + saved per-sector tail bytes, then stamp sector tails.
        ushort usn = 0xBEEF;
        BitConverter.GetBytes(usn).CopyTo(rec, usaOffset);
        for (var i = 0; i < sectors; i++)
        {
            int tail = (i + 1) * _sectorSize - 2;
            // save real bytes into USA, write USN into the tail
            rec[usaOffset + 2 + i * 2] = rec[tail];
            rec[usaOffset + 2 + i * 2 + 1] = rec[tail + 1];
            rec[tail] = (byte)(usn & 0xFF);
            rec[tail + 1] = (byte)(usn >> 8);
        }
        return rec;
    }

    private static byte[] Attr(uint type, byte[] content, bool nonResident = false, uint residentValueLength = 0)
    {
        if (nonResident)
        {
            // `content` is the 0x40 non-resident header carrying RealSize at offset 0x30.
            var a = (byte[])content.Clone();
            BitConverter.GetBytes(type).CopyTo(a, 0x00);
            BitConverter.GetBytes((uint)a.Length).CopyTo(a, 0x04);
            a[0x08] = 1; // non-resident
            a[0x09] = 0; // unnamed
            return a;
        }
        var r = new byte[0x18 + content.Length];
        BitConverter.GetBytes(type).CopyTo(r, 0x00);
        BitConverter.GetBytes((uint)r.Length).CopyTo(r, 0x04);
        r[0x08] = 0; // resident
        r[0x09] = 0; // unnamed
        BitConverter.GetBytes(residentValueLength == 0 ? (uint)content.Length : residentValueLength).CopyTo(r, 0x10);
        BitConverter.GetBytes((ushort)0x18).CopyTo(r, 0x14); // value offset
        content.CopyTo(r, 0x18);
        return r;
    }
}
