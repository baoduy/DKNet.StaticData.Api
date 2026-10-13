using DKNet.AspCore.Extensions.Endpoints;
using DKNet.AspCore.Extensions.Responses;
using DKNet.EfCore.Specifications.Definitions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.Domains.Features.SettingGroups.Entities;
using Microsoft.Extensions.Options;

namespace DKNet.StaticData.AppServices.Features.SettingGroups;

/// <summary>The query of the setting group list (spec DRK-2245 §3a): the page and the optional date range, no other filter.</summary>
public sealed record SettingGroupListQuery : IListPageQuery
{
    #region Properties

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }

    public DateTimeOffset? FromDate { get; init; }

    public DateTimeOffset? ToDate { get; init; }

    #endregion
}

/// <summary>
///     Lists the owner's setting groups newest first, one page at a time; a group is in the date range when its creation
///     time or its update time is.
/// </summary>
public sealed class ListSettingGroupsHandler(IRepositorySpec repository, IOptions<ListQueryOptions> options)
{
    #region Methods

    public Task<IResult<PagedResponse<SettingGroupDto>>> HandleAsync(
        SettingGroupListQuery query,
        CancellationToken cancellationToken = default) =>
        repository.ReadAsync(new SettingGroupList(query), query, options.Value, cancellationToken);

    #endregion

    private sealed class SettingGroupList : ModelSpecification<SettingGroup, SettingGroupDto>
    {
        public SettingGroupList(SettingGroupListQuery query)
        {
            var (from, to) = (query.FromDate, query.ToDate);
            WithFilter(g =>
                ((from == null || g.CreatedOn >= from) && (to == null || g.CreatedOn <= to))
                || (g.UpdatedOn != null && (from == null || g.UpdatedOn >= from) && (to == null || g.UpdatedOn <= to)));
            AddOrderByDescending(g => g.CreatedOn);
            AddOrderByDescending(g => g.Id);
        }
    }
}
