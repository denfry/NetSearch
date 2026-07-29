namespace NetSearch.Core.Cleaning;

public sealed record CleanCategory(
    string Id, string DisplayName, string Description,
    bool NeedsAdmin, bool UsesRecycleBin, bool DefaultSelected);
