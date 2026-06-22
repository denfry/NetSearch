using System.Buffers.Binary;

namespace NetSearch.Core.Native;

public readonly record struct NtfsVolumeGeometry(int BytesPerCluster, int BytesPerFileRecordSegment, long MftStartLcn);

public static class NtfsVolumeData
{
    /// <summary>Parses the NTFS_VOLUME_DATA_BUFFER returned by FSCTL_GET_NTFS_VOLUME_DATA.</summary>
    public static NtfsVolumeGeometry Parse(ReadOnlySpan<byte> buf)
    {
        var bytesPerCluster = BinaryPrimitives.ReadInt32LittleEndian(buf[0x2C..]);
        var bytesPerRecord  = BinaryPrimitives.ReadInt32LittleEndian(buf[0x30..]);
        var mftStartLcn     = BinaryPrimitives.ReadInt64LittleEndian(buf[0x40..]);
        return new NtfsVolumeGeometry(bytesPerCluster, bytesPerRecord, mftStartLcn);
    }
}
