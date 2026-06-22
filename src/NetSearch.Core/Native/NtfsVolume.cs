using System.ComponentModel;
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
        if (h.IsInvalid) { var err = Marshal.GetLastWin32Error(); throw new IOException($"open volume {driveLetter}: failed", new Win32Exception(err)); }

        var buf = new byte[512];
        if (!NativeMethods.DeviceIoControl(h, NativeMethods.FSCTL_GET_NTFS_VOLUME_DATA, IntPtr.Zero, 0,
                buf, (uint)buf.Length, out _, IntPtr.Zero))
        { var e = Marshal.GetLastWin32Error(); h.Dispose(); throw new IOException("NTFS volume data failed", new Win32Exception(e)); }

        var geo = NtfsVolumeData.Parse(buf);
        return new NtfsVolume(h, geo.BytesPerFileRecordSegment, geo.BytesPerCluster, geo.MftStartLcn);
    }

    public byte[] ReadClusters(long lcn, int clusterCount)
    {
        long offset = lcn * BytesPerCluster;
        if (!NativeMethods.SetFilePointerEx(_handle, offset, out _, 0))
            { var err = Marshal.GetLastWin32Error(); throw new IOException("seek failed", new Win32Exception(err)); }
        int total = clusterCount * BytesPerCluster;
        var buffer = new byte[total];
        var chunk = new byte[BytesPerCluster];  // volume reads must stay cluster-aligned
        int done = 0;
        while (done < total)
        {
            if (!NativeMethods.ReadFile(_handle, chunk, (uint)BytesPerCluster, out var read, IntPtr.Zero) || read == 0)
                { var err = Marshal.GetLastWin32Error(); throw new IOException("read failed", new Win32Exception(err)); }
            Buffer.BlockCopy(chunk, 0, buffer, done, (int)read);
            done += (int)read;
        }
        return buffer;
    }

    public byte[] ReadRecord(long recordNumber)
    {
        long byteOffset = MftStartLcn * BytesPerCluster + recordNumber * BytesPerFileRecordSegment;
        long lcn = byteOffset / BytesPerCluster;
        int within = (int)(byteOffset % BytesPerCluster);
        var clusters = ReadClusters(lcn, (BytesPerFileRecordSegment + BytesPerCluster - 1) / BytesPerCluster + 1);
        return clusters.AsSpan(within, BytesPerFileRecordSegment).ToArray();
    }

    public bool DeviceControl(uint code, byte[] input, byte[] output, out uint returned)
    {
        var inHandle = input.Length > 0
            ? System.Runtime.InteropServices.GCHandle.Alloc(input, System.Runtime.InteropServices.GCHandleType.Pinned)
            : default;
        try
        {
            var inPtr = input.Length > 0 ? inHandle.AddrOfPinnedObject() : IntPtr.Zero;
            return NativeMethods.DeviceIoControl(_handle, code, inPtr, (uint)input.Length, output, (uint)output.Length, out returned, IntPtr.Zero);
        }
        finally { if (inHandle.IsAllocated) inHandle.Free(); }
    }

    public void Dispose() => _handle.Dispose();
}
