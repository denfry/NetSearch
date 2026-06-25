using System.Text;
using Xunit;
using NetSearch.Core.Native;

namespace NetSearch.Core.Tests.Native;

public class UsnRecordParserTests
{
    private static byte[] Record(long frn, long parent, uint reason, string name)
    {
        var nm = Encoding.Unicode.GetBytes(name);
        int len = 0x3C + nm.Length;
        len = (len + 7) & ~7;
        var b = new byte[len];
        BitConverter.GetBytes((uint)len).CopyTo(b, 0x00);
        BitConverter.GetBytes((ushort)2).CopyTo(b, 0x04);  // major version
        BitConverter.GetBytes(frn).CopyTo(b, 0x08);
        BitConverter.GetBytes(parent).CopyTo(b, 0x10);
        BitConverter.GetBytes(reason).CopyTo(b, 0x28);
        BitConverter.GetBytes((ushort)nm.Length).CopyTo(b, 0x38);
        BitConverter.GetBytes((ushort)0x3C).CopyTo(b, 0x3A);
        nm.CopyTo(b, 0x3C);
        return b;
    }

    [Fact]
    public void Parses_next_usn_and_multiple_records()
    {
        var r1 = Record(100, 5, 0x100, "a.txt");      // create
        var r2 = Record(200, 5, 0x200, "gone.txt");   // delete
        var buf = new byte[8 + r1.Length + r2.Length];
        BitConverter.GetBytes(99999L).CopyTo(buf, 0);
        r1.CopyTo(buf, 8);
        r2.CopyTo(buf, 8 + r1.Length);

        var (next, changes) = UsnRecordParser.Parse(buf);

        Assert.Equal(99999, next);
        Assert.Equal(2, changes.Count);
        Assert.Equal(100, changes[0].Frn);
        Assert.Equal("a.txt", changes[0].Name);
        Assert.True(UsnReason.IsDelete(changes[1].Reason));
        Assert.Equal(200, changes[1].Frn);
    }
}
