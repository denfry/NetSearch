using Xunit;
using NetSearch.Core.Native;

namespace NetSearch.Core.Tests.Native;

public class MftEntryAssemblerFrnTests
{
    [Fact]
    public void Each_entry_keeps_its_record_number_as_frn()
    {
        var records = new Dictionary<long, ParsedMftRecord>
        {
            [5]  = new(true, 5, "", 0, 0),
            [10] = new(true, 5, "Data", 0, 0),
            [12] = new(false, 10, "a.txt", 1, 1),
        };
        var byName = MftEntryAssembler.Assemble(1, "C:", @"C:\Data", records)
            .ToDictionary(e => e.Name);
        Assert.Equal(12, byName["a.txt"].Frn);
        Assert.Equal(10, byName["Data"].Frn);
    }
}
