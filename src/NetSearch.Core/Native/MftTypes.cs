namespace NetSearch.Core.Native;

public readonly record struct ParsedMftRecord(bool IsDir, long ParentRecordNumber, string Name, long Size, long ModifiedUnix);

public readonly record struct MftNode(string Name, long ParentRecordNumber, bool IsDir);

public readonly record struct DataRun(long Lcn, long ClusterCount);

public enum IndexBackend { Mft, Crawler }

public readonly record struct VolumeInfo(string FileSystem, bool IsFixed, bool IsUnc, char DriveLetter);

public static class MftTime
{
    private const long EpochOffsetTicks = 116444736000000000L; // 1601-01-01 → 1970-01-01

    public static long FileTimeToUnixSeconds(long fileTime)
        => fileTime <= EpochOffsetTicks ? 0 : (fileTime - EpochOffsetTicks) / 10_000_000L;
}
