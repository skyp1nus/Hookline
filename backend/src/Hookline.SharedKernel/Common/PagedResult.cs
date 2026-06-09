namespace Hookline.SharedKernel.Common;

/// <summary>The one paging envelope every module's list endpoint returns.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
    public bool HasNext => (long)Page * PageSize < Total;
    public bool HasPrevious => Page > 1;

    public static PagedResult<T> Empty(int page = 1, int pageSize = 20) =>
        new([], page, pageSize, 0);
}

/// <summary>Common paging request, clamped to safe bounds.</summary>
public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int MaxPageSize = 200;

    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize is < 1 or > MaxPageSize ? 20 : PageSize;
    public int Skip => (SafePage - 1) * SafePageSize;
}
