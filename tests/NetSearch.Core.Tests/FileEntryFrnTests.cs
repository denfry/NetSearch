using Xunit;
using NetSearch.Core.Models;

namespace NetSearch.Core.Tests;

public class FileEntryFrnTests
{
    [Fact]
    public void Frn_defaults_to_null_and_can_be_set()
    {
        Assert.Null(FileEntry.FromComponents(1, "a", @"C:\", false, 1, 2).Frn);
        Assert.Equal(42, FileEntry.FromComponents(1, "a", @"C:\", false, 1, 2, frn: 42).Frn);
    }
}
