using Xunit;
using NetSearch.Core.Native;
using NetSearch.Core.Tests.Native;

namespace NetSearch.Core.Tests.Native;

public class MftRecordParserTests
{
    private const long Mod = 116444736000000000L + 100L * 10_000_000L; // epoch +100s

    [Fact]
    public void Parses_file_with_resident_data_size_name_and_parent()
    {
        var rec = new MftRecordBuilder()
            .StandardInformation(Mod)
            .FileName(parentRecordNumber: 5, "Report.txt", ns: 1)
            .DataResident(42)
            .Build();

        Assert.True(MftRecordParser.TryParse(rec, 512, out var r));
        Assert.False(r.IsDir);
        Assert.Equal(5, r.ParentRecordNumber);
        Assert.Equal("Report.txt", r.Name);
        Assert.Equal(42, r.Size);
        Assert.Equal(100, r.ModifiedUnix);
    }

    [Fact]
    public void Uses_real_size_for_non_resident_data()
    {
        var rec = new MftRecordBuilder()
            .StandardInformation(Mod).FileName(5, "big.bin", 1).DataNonResident(1_000_000).Build();
        Assert.True(MftRecordParser.TryParse(rec, 512, out var r));
        Assert.Equal(1_000_000, r.Size);
    }

    [Fact]
    public void Directory_has_zero_size_and_isdir_true()
    {
        var rec = new MftRecordBuilder().AsDirectory()
            .StandardInformation(Mod).FileName(5, "Docs", 1).Build();
        Assert.True(MftRecordParser.TryParse(rec, 512, out var r));
        Assert.True(r.IsDir);
        Assert.Equal(0, r.Size);
        Assert.Equal("Docs", r.Name);
    }

    [Fact]
    public void Prefers_win32_name_over_dos_short_name()
    {
        var rec = new MftRecordBuilder()
            .StandardInformation(Mod)
            .FileName(5, "LONGNA~1", ns: 2)            // DOS 8.3 first
            .FileName(5, "LongName.txt", ns: 1)        // Win32 second
            .DataResident(1).Build();
        Assert.True(MftRecordParser.TryParse(rec, 512, out var r));
        Assert.Equal("LongName.txt", r.Name);
    }

    [Fact]
    public void Rejects_not_in_use_records()
    {
        var free = new MftRecordBuilder().InUse(false).StandardInformation(Mod).FileName(5, "x", 1).Build();
        Assert.False(MftRecordParser.TryParse(free, 512, out _));
    }

    [Fact]
    public void Rejects_extension_records()
    {
        var ext = new MftRecordBuilder().BaseRecord(7).StandardInformation(Mod).FileName(5, "x", 1).DataResident(1).Build();
        Assert.False(MftRecordParser.TryParse(ext, 512, out _));
    }

    [Fact]
    public void Truncated_record_returns_false_without_throwing()
    {
        var full = new MftRecordBuilder()
            .StandardInformation(Mod)
            .FileName(5, "file.txt", ns: 1)
            .DataResident(10)
            .Build();
        // Pass only the first 64 bytes — well short of the attribute area.
        var truncated = full.AsSpan(0, 64).ToArray();
        Assert.False(MftRecordParser.TryParse(truncated, 512, out _));
    }

    [Fact]
    public void Prefers_posix_name_over_dos_short_name()
    {
        var rec = new MftRecordBuilder()
            .StandardInformation(Mod)
            .FileName(5, "myfile~1.txt", ns: 2)   // DOS short name first
            .FileName(5, "myfile.txt", ns: 0)       // POSIX second
            .DataResident(1)
            .Build();
        Assert.True(MftRecordParser.TryParse(rec, 512, out var r));
        Assert.Equal("myfile.txt", r.Name);
    }
}
