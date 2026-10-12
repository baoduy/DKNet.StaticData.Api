using DKNet.AspCore.Extensions.Endpoints;
using DKNet.AspCore.Extensions.Responses;
using DKNet.EfCore.Specifications.Definitions;
using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.Files;
using DKNet.StaticData.Domains.Features.FileGroups.Entities;
using Microsoft.Extensions.Options;

namespace DKNet.StaticData.AppServices.Features.FileGroups;

/// <summary>
///     The query of the group list (spec DRK-2235 §3a): the page, the optional date range and the exact purpose and
///     external reference filters.
/// </summary>
public sealed record FileGroupListQuery
{
    #region Properties

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }

    /// <summary>Inclusive lower bound on when a group was created or last updated.</summary>
    public DateTimeOffset? FromDate { get; init; }

    /// <summary>Inclusive upper bound on when a group was created or last updated.</summary>
    public DateTimeOffset? ToDate { get; init; }

    public string? Purpose { get; init; }

    public string? ExternalRef { get; init; }

    #endregion
}

/// <summary>
///     Lists the owner's groups newest first, one page at a time, in the page shape of DKNet's <c>MapGetList</c>, with
///     its paging defaults and its date range: a group is in the range when its creation time or its update time is.
///     DKNet's list route cannot take the typed <c>purpose</c> and <c>externalRef</c> filters (brief DRK-2239 §9 Q1).
/// </summary>
public sealed class ListFileGroupsHandler(IRepositorySpec repository, IOptions<ListQueryOptions> options)
{
    #region Methods

    public async Task<IResult<PagedResponse<FileGroupDto>>> HandleAsync(
        FileGroupListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.FromDate > query.ToDate)
        {
            return Result.Fail<PagedResponse<FileGroupDto>>(
                new FileError(FileErrorKind.Refused, "fromDate is later than toDate."));
        }

        var limits = options.Value;
        var pageSize = Math.Min(query.PageSize is null or < 1 ? limits.DefaultPageSize : query.PageSize.Value, limits.MaxPageSize);
        var page = await repository.ToPagedListAsync(
            new FileGroupList(query), query.PageNumber is null or < 1 ? 1 : query.PageNumber.Value, pageSize, cancellationToken);
        return Result.Ok(new PagedResponse<FileGroupDto>(page));
    }

    #endregion

    private sealed class FileGroupList : ModelSpecification<FileGroup, FileGroupDto>
    {
        public FileGroupList(FileGroupListQuery query)
        {
            var (from, to, purpose, externalRef) = (query.FromDate, query.ToDate, query.Purpose, query.ExternalRef);
            WithFilter(g =>
                (purpose == null || g.Purpose == purpose)
                && (externalRef == null || g.ExternalRef == externalRef)
                && (((from == null || g.CreatedOn >= from) && (to == null || g.CreatedOn <= to))
                    || (g.UpdatedOn != null && (from == null || g.UpdatedOn >= from) && (to == null || g.UpdatedOn <= to))));
            AddOrderByDescending(g => g.CreatedOn);
            AddOrderByDescending(g => g.Id);
        }
    }
}
