using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace NetSearch.Core.Native;

[SupportedOSPlatform("windows")]
public sealed class NtfsVolume : IDisposable
{
    private readonly SafeFileHandle _handle;
    public int BytesPerFileRecordSegment { get; }
    public int BytesPerCluster { get; }
    public long MftStartLcn { get; }

    private NtfsVolume(SafeFileHandle h, int recSeg, int cluster, long mftLcn)
    { _handle = h; BytesPerFileRecordSegment = recSeg; BytesPerCluster = cluster; MftStartLcn = mftLcn; }

    public static NtfsVolume Open(char driveLetter)
    {
        var h = NativeMethods.CreateFile($@"\\.\{char.ToUpperInvariant(driveLetter)}:",
            NativeMethods.GENERIC_READ, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) throw new IOException($"open volume {driveLetter}: failed", Marshal.GetLastWin32Error());

        var buf = new byte[512];
        if (!NativeMethods.DeviceIoControl(h, NativeMethods.FSCTL_GET_NTFS_VOLUME_DATA, IntPtr.Zero, 0,
                buf, (uint)buf.Length, out _, IntPtr.Zero))
        { var e = Marshal.GetLastWin32Error(); h.Dispose(); throw new IOException("NTFS volume data failed", e); }

        // NTFS_VOLUME_DATA_BUFFER offsets:
        long bytesPerCluster = BitConverter.ToInt32(buf, 0x18);   // BytesPerCluster
        long mftStartLcn = BitConverter.ToInt64(buf, 0x20);        // MftStartLcn
        int bytesPerRecord = BitConverter.ToInt32(buf, 0x40);      // BytesPerFileRecordSegment
        return new NtfsVolume(h, bytesPerRecord, (int)bytesPerCluster, mftStartLcn);
    }

    public byte[] ReadClusters(long lcn, int clusterCount)
    {
        long offset = lcn * BytesPerCluster;
        if (!NativeMethods.SetFilePointerEx(_handle, offset, out _, 0))
            throw new IOException("seek failed", Marshal.GetLastWin32Error());
        int total = clusterCount * BytesPerCluster;
        var buffer = new byte[total];
        var chunk = new byte[BytesPerCluster];  // volume reads must stay cluster-aligned
        int done = 0;
        while (done < total)
        {
            if (!NativeMethods.ReadFile(_handle, chunk, (uint)BytesPerCluster, out var read, IntPtr.Zero) || read == 0)
                throw new IOException("read failed", Marshal.GetLastWin32Error());
            Buffer.BlockCopy(chunk, 0, buffer, done, (int)read);
            done += (int)read;
        }
        return buffer;
    }

    public void Dispose() => _handle.Dispose();
}
