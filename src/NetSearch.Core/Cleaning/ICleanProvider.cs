namespace NetSearch.Core.Cleaning;

public interface ICleanProvider
{
    CleanCategory Category { get; }
    IEnumerable<CleanItem> Scan(CancellationToken ct);
}
