using DKNet.AspCore.Extensions.Endpoints;
using DKNet.AspCore.Extensions.Responses;
using DKNet.EfCore.Specifications.Definitions;
using DKNet.EfCore.Specifications.Repositories;
using DKNet.StaticData.Domains.Features.UiSettings.Entities;
using Microsoft.Extensions.Options;

namespace DKNet.StaticData.AppServices.Features.UiSettings;

/// <summary>
///     The query of the setting list (spec DRK-2245 §3a): the page, the optional date range and the exact setting key and
///     group filters.
/// </summary>
public sealed record UiSettingListQuery : IListPageQuery
{
    #region Properties

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }

    public DateTimeOffset? FromDate { get; init; }

    public DateTimeOffset? ToDate { get; init; }

    /// <summary>Only the setting with this key, compared exactly after trimming.</summary>
    public string? SettingKey { get; init; }

    /// <summary>Only the settings linked to this group; a group that does not exist for this owner holds none.</summary>
    public Guid? GroupId { get; init; }

    #endregion
}

/// <summary>
///     Lists the owner's settings newest first, one page at a time, each with its value; a setting is in the date range
///     when its creation time or its update time is. When both filters are sent, a setting matches both.
/// </summary>
public sealed class ListUiSettingsHandler(IRepositorySpec repository, IOptions<ListQueryOptions> options)
{
    #region Methods

    public Task<IResult<PagedResponse<UiSettingDto>>> HandleAsync(
        UiSettingListQuery query,
        CancellationToken cancellationToken = default) =>
        repository.ReadAsync(new UiSettingList(query), query, options.Value, cancellationToken);

    #endregion

    private sealed class UiSettingList : ModelSpecification<UiSetting, UiSettingDto>
    {
        public UiSettingList(UiSettingListQuery query)
        {
            var (from, to, settingKey, groupId) = (query.FromDate, query.ToDate, TextRules.Trim(query.SettingKey), query.GroupId);
            WithFilter(s =>
                (settingKey == null || s.SettingKey == settingKey)
                && (groupId == null || s.GroupId == groupId)
                && (((from == null || s.CreatedOn >= from) && (to == null || s.CreatedOn <= to))
                    || (s.UpdatedOn != null && (from == null || s.UpdatedOn >= from) && (to == null || s.UpdatedOn <= to))));
            AddOrderByDescending(s => s.CreatedOn);
            AddOrderByDescending(s => s.Id);
        }
    }
}
