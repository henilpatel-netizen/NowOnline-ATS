namespace Ats.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => (int)Math.Ceiling(Total / (double)Math.Max(1, PageSize));
    public bool HasPrev => Page > 1;
    public bool HasNext => Page < TotalPages;
}
