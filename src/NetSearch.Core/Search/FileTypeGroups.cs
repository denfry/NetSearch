namespace NetSearch.Core.Search;

/// <summary>Maps friendly file-type group names (shown as quick buttons in the UI)
/// to the comma-separated extension list used by the extension filter.</summary>
public static class FileTypeGroups
{
    public static string Extensions(string group) => group switch
    {
        "PDF" => "pdf",
        "Word" => "doc, docx",
        "Excel" => "xls, xlsx",
        "Фото" => "jpg, jpeg, png, gif, bmp",
        _ => "",
    };
}
