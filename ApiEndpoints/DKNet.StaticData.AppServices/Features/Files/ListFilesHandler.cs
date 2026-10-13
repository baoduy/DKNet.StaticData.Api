using DKNet.AspCore.Extensions.Endpoints;
using DKNet.AspCore.Extensions.Responses;
using DKNet.EfCore.Specifications.Definitions;
using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.Domains.Features.Files.Entities;
using Microsoft.Extensions.Options;

namespace DKNet.StaticData.AppServices.Features.Files;

/// <summary>The query of the file list: the page, the optional date range and the optional group.</summary>
public sealed record FileListQuery
{
    #region Properties

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }

    /// <summary>Inclusive lower bound on when a file was created or last updated.</summary>
    public DateTimeOffset? FromDate { get; init; }

    /// <summary>Inclusive upper bound on when a file was created or last updated.</summary>
    public DateTimeOffset? ToDate { get; init; }

    /// <summary>Only the files linked to this group; a group that does not exist for this owner holds none.</summary>
    public Guid? GroupId { get; init; }

    #endregion
}

/// <summary>
///     Lists the owner's files newest first, one page at a time, in the page shape, paging defaults and date range of
///     DKNet's <c>MapGetList</c>, as <see cref="FileGroups.ListFileGroupsHandler" /> does for groups: DKNet's list route
///     cannot take the typed <c>groupId</c> filter.
/// </summary>
public sealed class ListFilesHandler(IRepositorySpec repository, IOptions<ListQueryOptions> options)
{
    #region Methods

    public async Task<IResult<PagedResponse<StoredFileDto>>> HandleAsync(
        FileListQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.FromDate > query.ToDate)
        {
            return Result.Fail<PagedResponse<StoredFileDto>>(
                new FileError(FileErrorKind.Refused, "fromDate is later than toDate."));
        }

        var limits = options.Value;
        var pageSize = Math.Min(query.PageSize is null or < 1 ? limits.DefaultPageSize : query.PageSize.Value, limits.MaxPageSize);
        var page = await repository.ToPagedListAsync(
            new FileList(query), query.PageNumber is null or < 1 ? 1 : query.PageNumber.Value, pageSize, cancellationToken);
        return Result.Ok(new PagedResponse<StoredFileDto>(page));
    }

    #endregion

    private sealed class FileList : ModelSpecification<StoredFile, StoredFileDto>
    {
        public FileList(FileListQuery query)
        {
            var (from, to, groupId) = (query.FromDate, query.ToDate, query.GroupId);
            WithFilter(f =>
                (groupId == null || f.GroupId == groupId)
                && (((from == null || f.CreatedOn >= from) && (to == null || f.CreatedOn <= to))
                    || (f.UpdatedOn != null && (from == null || f.UpdatedOn >= from) && (to == null || f.UpdatedOn <= to))));
            AddOrderByDescending(f => f.CreatedOn);
            AddOrderByDescending(f => f.Id);
        }
    }
}
