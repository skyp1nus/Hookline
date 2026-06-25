using Hookline.SharedKernel.Common;

namespace Hookline.SharedKernel.Tests;

public sealed class PagedResultTests
{
    [Fact]
    public void TotalPages_rounds_up()
    {
        var paged = new PagedResult<int>([1, 2, 3], Page: 1, PageSize: 2, Total: 5);

        Assert.Equal(3, paged.TotalPages);
    }

    [Fact]
    public void TotalPages_zero_when_pageSize_is_zero()
    {
        var paged = new PagedResult<int>([], Page: 1, PageSize: 0, Total: 10);

        Assert.Equal(0, paged.TotalPages);
    }

    [Fact]
    public void TotalPages_zero_when_pageSize_is_negative()
    {
        var paged = new PagedResult<int>([], Page: 1, PageSize: -1, Total: 10);

        Assert.Equal(0, paged.TotalPages);
    }

    [Fact]
    public void HasNext_true_when_more_items_remain()
    {
        var paged = new PagedResult<int>([1], Page: 1, PageSize: 10, Total: 20);

        Assert.True(paged.HasNext);
    }

    [Fact]
    public void HasNext_false_on_last_page()
    {
        var paged = new PagedResult<int>([1], Page: 2, PageSize: 10, Total: 20);

        Assert.False(paged.HasNext);
    }

    [Fact]
    public void HasPrevious_false_on_first_page()
    {
        var paged = new PagedResult<int>([1], Page: 1, PageSize: 10, Total: 20);

        Assert.False(paged.HasPrevious);
    }

    [Fact]
    public void HasPrevious_true_after_first_page()
    {
        var paged = new PagedResult<int>([1], Page: 2, PageSize: 10, Total: 20);

        Assert.True(paged.HasPrevious);
    }

    [Fact]
    public void Empty_creates_zero_total()
    {
        var paged = PagedResult<string>.Empty();

        Assert.Empty(paged.Items);
        Assert.Equal(1, paged.Page);
        Assert.Equal(20, paged.PageSize);
        Assert.Equal(0, paged.Total);
        Assert.Equal(0, paged.TotalPages);
        Assert.False(paged.HasNext);
        Assert.False(paged.HasPrevious);
    }

    [Fact]
    public void PageRequest_clamps_page_below_one()
    {
        var req = new PageRequest(0, 10);
        Assert.Equal(1, req.SafePage);

        var neg = new PageRequest(-5, 10);
        Assert.Equal(1, neg.SafePage);
    }

    [Fact]
    public void PageRequest_clamps_pageSize_to_default()
    {
        var zero = new PageRequest(1, 0);
        Assert.Equal(20, zero.SafePageSize);

        var neg = new PageRequest(1, -1);
        Assert.Equal(20, neg.SafePageSize);

        var huge = new PageRequest(1, 999);
        Assert.Equal(20, huge.SafePageSize);
    }

    [Fact]
    public void PageRequest_accepts_valid_pageSize()
    {
        var req = new PageRequest(1, PageRequest.MaxPageSize);
        Assert.Equal(PageRequest.MaxPageSize, req.SafePageSize);
    }

    [Fact]
    public void PageRequest_Skip_computes_offset()
    {
        var req = new PageRequest(3, 10);
        Assert.Equal(20, req.Skip);
    }

    [Fact]
    public void PageRequest_Skip_uses_safe_values()
    {
        var req = new PageRequest(0, 0);
        Assert.Equal(0, req.Skip); // (SafePage=1 - 1) * SafePageSize=20 = 0
    }
}
