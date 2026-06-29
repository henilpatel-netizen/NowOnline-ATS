namespace Ats.Web.Models;

public class PagerModel
{
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public string Action { get; set; } = "Index";
    // Extra query values to preserve across pages (e.g. q, status). Non-null only.
    public Dictionary<string, string> Query { get; set; } = new();
}
