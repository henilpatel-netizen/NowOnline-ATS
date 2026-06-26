namespace Ats.Application.Tenancy;

public static class ReservedSlugs
{
    public static readonly HashSet<string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "api", "careers", "manage", "health", "www", "app", "static", "assets"
    };

    public static bool IsReserved(string slug) => Values.Contains(slug);
}
