using System.IO;
using NetSearch.Core.Cleaning.Native;

namespace NetSearch.Core.Cleaning;

public sealed record CleanOutcome(long FreedBytes, int DeletedCount, IReadOnlyList<string> Skipped);

public sealed class CleanExecutor
{
    public CleanOutcome Delete(IEnumerable<CleanItem> items, bool useRecycleBin, CancellationToken ct)
    {
        long freed = 0;
        int deleted = 0;
        var skipped = new List<string>();

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            if (item.Path == CleanItem.RecycleBinSentinel)
            {
                if (OperatingSystem.IsWindows() && ShellFileOps.TryEmptyRecycleBin())
                { freed += item.SizeBytes; deleted++; }
                else skipped.Add("Корзина");
                continue;
            }

            try
            {
                if (useRecycleBin && OperatingSystem.IsWindows())
                    ShellFileOps.DeleteToRecycleBin(item.Path);
                else if (item.IsDir)
                    Directory.Delete(item.Path, recursive: true);
                else
                    File.Delete(item.Path);

                freed += item.SizeBytes;
                deleted++;
            }
            catch (OperationCanceledException) { throw; }
            catch { skipped.Add(item.Path); }
        }

        return new CleanOutcome(freed, deleted, skipped);
    }
}
