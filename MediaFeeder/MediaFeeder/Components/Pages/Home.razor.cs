﻿using AntDesign;
using MediaFeeder.Data;
using MediaFeeder.Data.db;
using MediaFeeder.Filters;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace MediaFeeder.Components.Pages;

public partial class Home
{
    [Parameter] public int? FolderId { get; set; }

    [Parameter] public int? SubscriptionId { get; set; }

    private List<YtManagerAppVideo>? Videos { get; set; }

    [Inject] public MediaFeederDataContext? DataContext { get; set; }

    private string? SearchValue { get; set; } = string.Empty;
    private SortOrders SortOrder { get; set; } = SortOrders.Oldest;
    private bool? ShowWatched { get; set; } = false;
    private bool? ShowDownloaded { get; set; }
    private int ResultsPerPage { get; set; } = 50;
    private int PageNumber { get; set; } = 1;
    private int ItemsAvailable { get; set; }
    private TimeSpan Duration { get; set; }
    private string Title { get; set; } = "Home";
    private int FilterHash { get; set; }

    private SemaphoreSlim Updating { get; } = new(1);

    private bool UpdateHash()
    {
        var newHash = $"{FolderId}{SubscriptionId}{SortOrder}{ShowWatched}{ShowDownloaded}{SearchValue}".GetHashCode();

        if (newHash != FilterHash)
        {
            FilterHash = newHash;
            return true;
        }

        return false;
    }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        if (DataContext != null)
            await Update();
    }

    private async Task Update()
    {
        ArgumentNullException.ThrowIfNull(DataContext);

        if (!UpdateHash())
            return;

        try
        {
            await Updating.WaitAsync();

            Videos = null;
            StateHasChanged();

            var source = DataContext.YtManagerAppVideos.AsQueryable();

            if (FolderId != null)
            {
                source = source.Where(v => v.Subscription.ParentFolderId == FolderId);
                Title = (await DataContext.YtManagerAppSubscriptionFolders.FindAsync(FolderId))?.Name ?? "";
            }

            if (SubscriptionId != null)
            {
                source = source.Where(v => v.SubscriptionId == SubscriptionId);
                Title = (await DataContext.YtManagerAppSubscriptions.FindAsync(SubscriptionId))?.Name ?? "";
            }

            source = SortOrder switch
            {
                SortOrders.Oldest => source.OrderBy(v => v.PublishDate),
                SortOrders.Newest => source.OrderByDescending(v => v.PublishDate),
                SortOrders.PlaylistOrder => source.OrderBy(v => v.PlaylistIndex),
                SortOrders.ReversePlaylistOrder => source.OrderByDescending(v => v.PlaylistIndex),
                SortOrders.Popularity => source.OrderByDescending(v => v.Views),
                SortOrders.TopRated => source.OrderByDescending(v => v.Rating),
                _ => source
            };

            if (ShowWatched != null)
                source = source.Where(v => v.Watched == ShowWatched);

            if (ShowDownloaded != null)
                source = source.Where(v => string.IsNullOrWhiteSpace(v.DownloadedPath) != ShowDownloaded);

            if (!string.IsNullOrWhiteSpace(SearchValue))
                source = source.Where(v => v.Name.Contains(SearchValue) || v.Description.Contains(SearchValue));

            ItemsAvailable = await source.CountAsync();
            Duration = TimeSpan.FromSeconds(await source.SumAsync(v => v.Duration));
            Videos = await source.Skip((PageNumber - 1) * ResultsPerPage).Take(ResultsPerPage).ToListAsync();
        }
        finally
        {
            Updating.Release();
        }
    }

    private Task PageChange(PaginationEventArgs paginationEventArgs)
    {
        PageNumber = paginationEventArgs.Page;
        ResultsPerPage = paginationEventArgs.PageSize;

        return Update();
    }
}