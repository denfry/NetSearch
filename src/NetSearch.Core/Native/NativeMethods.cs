using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace NetSearch.Core.Native;

[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    public const uint GENERIC_READ = 0x80000000;
    public const uint FILE_SHARE_READ = 0x1, FILE_SHARE_WRITE = 0x2;
    public const uint OPEN_EXISTING = 3;
    public const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x00090064;
    public const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4;
    public const uint FSCTL_READ_USN_JOURNAL  = 0x000900BB;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, uint inSize,
        byte[] outBuf, uint outSize, out uint returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetFilePointerEx(SafeFileHandle h, long distance, out long newPointer, uint method);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadFile(SafeFileHandle h, byte[] buffer, uint toRead, out uint read, IntPtr overlapped);
}
