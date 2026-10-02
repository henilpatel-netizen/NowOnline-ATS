namespace Ats.Application.Common;

public static class LikePattern
{
    // SQL Server LIKE pattern for "contains term": % _ [ are bracket-escaped so user input matches literally.
    public static string Contains(string term) => "%" + term
        .Replace("[", "[[]", StringComparison.Ordinal)
        .Replace("%", "[%]", StringComparison.Ordinal)
        .Replace("_", "[_]", StringComparison.Ordinal) + "%";
}
