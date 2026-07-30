namespace Ats.Application.Search;

public interface IGlobalSearchService
{
    // Caps results per category. Tenant scoping comes from the global query filter.
    Task<SearchResults> SearchAsync(string? term, CancellationToken ct = default);
}
