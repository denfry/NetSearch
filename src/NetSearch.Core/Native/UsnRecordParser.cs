using System.Buffers.Binary;
using System.Text;

namespace NetSearch.Core.Native;

public readonly record struct UsnChange(long Frn, long ParentFrn, string Name, uint Reason);

public static class UsnReason
{
    public const uint FileCreate = 0x100, FileDelete = 0x200,
        RenameOldName = 0x1000, RenameNewName = 0x2000, Close = 0x80000000;
    public static bool IsDelete(uint reason) => (reason & FileDelete) != 0;
}

public static class UsnRecordParser
{
    public static (long NextUsn, IReadOnlyList<UsnChange> Changes) Parse(ReadOnlySpan<byte> readOutput)
    {
        if (readOutput.Length < 8) return (0, Array.Empty<UsnChange>());
        long next = BinaryPrimitives.ReadInt64LittleEndian(readOutput);
        var changes = new List<UsnChange>();
        int pos = 8;
        while (pos + 0x3C <= readOutput.Length)
        {
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(readOutput[pos..]);
            if (len < 0x3C || pos + len > readOutput.Length) break;
            long frn = BinaryPrimitives.ReadInt64LittleEndian(readOutput[(pos + 0x08)..]) & 0xFFFFFFFFFFFFL;
            long parent = BinaryPrimitives.ReadInt64LittleEndian(readOutput[(pos + 0x10)..]) & 0xFFFFFFFFFFFFL;
            uint reason = BinaryPrimitives.ReadUInt32LittleEndian(readOutput[(pos + 0x28)..]);
            int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(readOutput[(pos + 0x38)..]);
            int nameOff = BinaryPrimitives.ReadUInt16LittleEndian(readOutput[(pos + 0x3A)..]);
            var name = Encoding.Unicode.GetString(readOutput.Slice(pos + nameOff, nameLen));
            changes.Add(new UsnChange(frn, parent, name, reason));
            pos += len;
        }
        return (next, changes);
    }
}
