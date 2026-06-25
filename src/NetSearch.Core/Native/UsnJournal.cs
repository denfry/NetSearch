using System.Runtime.Versioning;

namespace NetSearch.Core.Native;

[SupportedOSPlatform("windows")]
public static class UsnJournal
{
    public static bool TryQuery(NtfsVolume vol, out long journalId, out long nextUsn)
    {
        journalId = 0; nextUsn = 0;
        var outBuf = new byte[80]; // USN_JOURNAL_DATA_V0/V1
        if (!vol.DeviceControl(NativeMethods.FSCTL_QUERY_USN_JOURNAL, Array.Empty<byte>(), outBuf, out _))
            return false;
        var state = UsnJournalData.Parse(outBuf);
        journalId = state.JournalId;
        nextUsn = state.NextUsn;
        return journalId != 0;
    }

    public static bool JournalMatches(NtfsVolume vol, long expectedJournalId)
        => TryQuery(vol, out var id, out _) && id == expectedJournalId;

    public static (long NextUsn, IReadOnlyList<UsnChange> Changes) Read(NtfsVolume vol, long journalId, long startUsn)
    {
        // READ_USN_JOURNAL_DATA_V0: StartUsn, ReasonMask, ReturnOnlyOnClose, Timeout, BytesToWaitFor, UsnJournalID
        var input = new byte[40];
        BitConverter.GetBytes(startUsn).CopyTo(input, 0x00);
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(input, 0x08); // ReasonMask = all
        BitConverter.GetBytes(journalId).CopyTo(input, 0x20);
        var outBuf = new byte[64 * 1024];
        if (!vol.DeviceControl(NativeMethods.FSCTL_READ_USN_JOURNAL, input, outBuf, out var returned))
            return (startUsn, Array.Empty<UsnChange>());
        return UsnRecordParser.Parse(outBuf.AsSpan(0, (int)returned));
    }
}
