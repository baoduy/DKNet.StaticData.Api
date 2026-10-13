using DKNet.AspCore.Extensions.Endpoints;
using DKNet.AspCore.Extensions.Responses;
using DKNet.EfCore.Specifications.Definitions;
using DKNet.EfCore.Specifications.Extensions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.AppServices.Features.Files;

namespace DKNet.StaticData.AppServices.Share;

/// <summary>The page and the optional date range every list query of a setting or setting group route takes.</summary>
public interface IListPageQuery
{
    #region Properties

    int? PageNumber { get; }

    int? PageSize { get; }

    /// <summary>Inclusive lower bound on when a record was created or last updated.</summary>
    DateTimeOffset? FromDate { get; }

    /// <summary>Inclusive upper bound on when a record was created or last updated.</summary>
    DateTimeOffset? ToDate { get; }

    #endregion
}

/// <summary>
///     Reads one page of a list in the page shape and with the paging defaults of DKNet's <c>MapGetList</c>, as the file
///     and file group lists do: page 1 and the default page size when none is sent, never more than the maximum page size.
/// </summary>
internal static class ListPages
{
    #region Methods

    /// <returns>The page, or a refusal when the date range ends before it starts.</returns>
    public static async Task<IResult<PagedResponse<TDto>>> ReadAsync<TEntity, TDto>(
        this IRepositorySpec repository,
        IModelSpecification<TEntity, TDto> list,
        IListPageQuery query,
        ListQueryOptions limits,
        CancellationToken cancellationToken)
        where TEntity : class
        where TDto : class
    {
        if (query.FromDate > query.ToDate)
        {
            return Result.Fail<PagedResponse<TDto>>(new FileError(FileErrorKind.Refused, "fromDate is later than toDate."));
        }

        var pageSize = Math.Min(query.PageSize is null or < 1 ? limits.DefaultPageSize : query.PageSize.Value, limits.MaxPageSize);
        var page = await repository.ToPagedListAsync(
            list, query.PageNumber is null or < 1 ? 1 : query.PageNumber.Value, pageSize, cancellationToken);
        return Result.Ok(new PagedResponse<TDto>(page));
    }

    #endregion
}
