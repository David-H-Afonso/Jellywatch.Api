namespace Jellywatch.Api.Helpers;

public class QueryParameters
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; }

    public int Skip => (Page - 1) * PageSize;
    public int Take => PageSize;
}

public class MediaQueryParameters : QueryParameters
{
    public string? State { get; set; }
    public int? ProfileId { get; set; }
}

public class PagedResult<T>
{
    public IEnumerable<T> Data { get; set; } = new List<T>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
