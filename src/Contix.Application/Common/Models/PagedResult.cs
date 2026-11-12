namespace Contix.Application.Common.Models;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

/// <summary>Parâmetros padrão de paginação.</summary>
public record PageQuery
{
    public const int MaxPageSize = 100;
    private int _page = 1;
    private int _pageSize = 20;

    public int Page { get => _page; init => _page = value < 1 ? 1 : value; }
    public int PageSize { get => _pageSize; init => _pageSize = value is < 1 ? 20 : Math.Min(value, MaxPageSize); }
    public string? OrderBy { get; init; }
    public bool Desc { get; init; }

    public int Skip => (Page - 1) * PageSize;
}
