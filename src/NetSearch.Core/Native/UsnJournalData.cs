using System.Buffers.Binary;

namespace NetSearch.Core.Native;

public readonly record struct UsnJournalState(long JournalId, long NextUsn);

public static class UsnJournalData
{
    /// <summary>Parses USN_JOURNAL_DATA_V0 (output of FSCTL_QUERY_USN_JOURNAL): UsnJournalID@0x00, NextUsn@0x10.</summary>
    public static UsnJournalState Parse(ReadOnlySpan<byte> buf)
    {
        var journalId = BinaryPrimitives.ReadInt64LittleEndian(buf[0x00..]);
        var nextUsn   = BinaryPrimitives.ReadInt64LittleEndian(buf[0x10..]);
        return new UsnJournalState(journalId, nextUsn);
    }
}
