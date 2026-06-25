using Xunit;
using NetSearch.Core.Native;

namespace NetSearch.Core.Tests.Native;

public class UsnJournalDataTests
{
    [Fact]
    public void Parses_journal_id_and_next_usn_not_first_usn()
    {
        var buf = new byte[0x40];
        BitConverter.GetBytes(777L).CopyTo(buf, 0x00);   // UsnJournalID
        BitConverter.GetBytes(111L).CopyTo(buf, 0x08);   // FirstUsn (must NOT be returned)
        BitConverter.GetBytes(999L).CopyTo(buf, 0x10);   // NextUsn (the cursor)

        var s = UsnJournalData.Parse(buf);

        Assert.Equal(777L, s.JournalId);
        Assert.Equal(999L, s.NextUsn);
    }
}
