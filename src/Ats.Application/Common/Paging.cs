namespace Ats.Application.Common;

public static class Paging
{
    // page comes straight from the query string: computed in long and capped, a huge page gives an
    // empty page instead of an overflowed negative Skip.
    public static int Offset(int page, int pageSize) =>
        (int)Math.Clamp((Math.Max(page, 1) - 1L) * pageSize, 0, int.MaxValue);

    // Out-of-range pages land on the nearest real page, so the pager never shows "Page -5" or an empty
    // page past the end. Computed in long so total near int.MaxValue cannot overflow.
    public static int Clamp(int page, int total, int pageSize)
    {
        var size = Math.Max(1, pageSize);
        var totalPages = (int)((Math.Max(0L, total) + size - 1) / size);
        return Math.Clamp(page, 1, Math.Max(1, totalPages));
    }
}
