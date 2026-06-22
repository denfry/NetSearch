using System.Runtime.Versioning;
using NetSearch.Core.Indexing;
using NetSearch.Core.Models;

namespace NetSearch.Core.Native;

[SupportedOSPlatform("windows")]
public sealed class MftEnumerator
{
    private readonly int _batchSize;
    public MftEnumerator(int batchSize = 4096) => _batchSize = Math.Max(1, batchSize);

    public void Enumerate(int rootId, string rootPath, Action<IReadOnlyList<FileEntry>> onBatch,
        CancellationToken ct, IProgress<CrawlProgress>? progress = null)
    {
        var driveLetter = char.ToUpperInvariant(rootPath[0]);
        var volumeRoot = driveLetter + ":";
        using var vol = NtfsVolume.Open(driveLetter);

        // MFT record 0 describes $MFT; follow its $DATA runs to read the whole table.
        var rec0 = vol.ReadClusters(vol.MftStartLcn,
            Math.Max(1, vol.BytesPerFileRecordSegment / vol.BytesPerCluster));
        var extents = ReadMftExtents(rec0, vol.BytesPerFileRecordSegment);

        var records = new Dictionary<long, ParsedMftRecord>();
        long recordNumber = 0;
        int recSize = vol.BytesPerFileRecordSegment;
        foreach (var run in extents)
        {
            ct.ThrowIfCancellationRequested();
            if (run.Lcn < 0) { recordNumber += run.ClusterCount * vol.BytesPerCluster / recSize; continue; }
            var data = vol.ReadClusters(run.Lcn, (int)run.ClusterCount);
            for (var off = 0; off + recSize <= data.Length; off += recSize, recordNumber++)
            {
                if (MftRecordParser.TryParse(data.AsSpan(off, recSize), 512, out var r))
                    records[recordNumber] = r;
            }
            progress?.Report(new CrawlProgress(records.Count, volumeRoot));
        }

        var batch = new List<FileEntry>(_batchSize);
        foreach (var e in MftEntryAssembler.Assemble(rootId, volumeRoot, NormalizeRoot(rootPath), records))
        {
            ct.ThrowIfCancellationRequested();
            batch.Add(e);
            if (batch.Count >= _batchSize) { onBatch(batch); batch = new List<FileEntry>(_batchSize); }
        }
        if (batch.Count > 0) onBatch(batch);
        progress?.Report(new CrawlProgress(records.Count, volumeRoot));
    }

    private static string NormalizeRoot(string rootPath) => rootPath.TrimEnd('\\', '/');

    private static IReadOnlyList<DataRun> ReadMftExtents(byte[] rec0, int recSize)
    {
        // Parse record 0's unnamed non-resident $DATA and decode its run list.
        var span = rec0.AsSpan(0, recSize);
        MftRecordParser.TryParse(span, 512, out _); // applies fixups in place
        int pos = BitConverter.ToUInt16(rec0, 0x14);
        while (pos + 8 <= recSize)
        {
            uint type = BitConverter.ToUInt32(rec0, pos);
            if (type == 0xFFFFFFFF) break;
            int len = (int)BitConverter.ToUInt32(rec0, pos + 4);
            byte nonResident = rec0[pos + 0x08];
            byte nameLen = rec0[pos + 0x09];
            if (type == 0x80 && nameLen == 0 && nonResident == 1)
            {
                int runOff = BitConverter.ToUInt16(rec0, pos + 0x20);
                return DataRunParser.Parse(rec0.AsSpan(pos + runOff, len - runOff));
            }
            if (len <= 0) break;
            pos += len;
        }
        return Array.Empty<DataRun>();
    }
}
