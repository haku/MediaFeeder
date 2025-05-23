using MassTransit;
using MediaFeeder.Data;
using MediaFeeder.Tasks;
using Microsoft.EntityFrameworkCore;
using System.ServiceModel.Syndication;
using System.Xml;
using MediaFeeder.Data.db;

namespace MediaFeeder.Providers.RSS;

public class RSSSubscriptionSynchroniseConsumer(
    ILogger<RSSSubscriptionSynchroniseConsumer> logger,
    IDbContextFactory<MediaFeederDataContext> contextFactory,
    IHttpClientFactory httpClientFactory
) : IConsumer<SynchroniseSubscriptionContract<RSSProvider>>
{
    public async Task Consume(ConsumeContext<SynchroniseSubscriptionContract<RSSProvider>> context)
    {
        await using var db = await contextFactory.CreateDbContextAsync(context.CancellationToken);

        var subscription =
            await db.Subscriptions.SingleAsync(s => s.Id == context.Message.SubscriptionId, context.CancellationToken);
        logger.LogInformation("Starting synchronize {}", subscription.Name);

        foreach (var video in await db.Videos
                     .Where(v => v.SubscriptionId == subscription.Id && v.New && DateTimeOffset.UtcNow - v.PublishDate <= TimeSpan.FromDays(1))
                     .ToListAsync(context.CancellationToken)
                )
            video.New = false;

        await db.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("Starting check new videos {}", subscription.Name);

        using var client = httpClientFactory.CreateClient("retry");
        using var feedResponse = await client.GetAsync(subscription.ChannelId, context.CancellationToken);
        feedResponse.EnsureSuccessStatusCode();

        using var feedReader = XmlReader.Create(await feedResponse.Content.ReadAsStreamAsync(context.CancellationToken));
        var feed = SyndicationFeed.Load(feedReader);

        if (subscription.Name == subscription.ChannelName)
            subscription.Name = feed.Title.Text;
        subscription.ChannelName = feed.Title.Text;

        // Need to make absolute
        subscription.Thumb = new Uri(new Uri(subscription.ChannelId), feed.ImageUrl).AbsoluteUri;
        subscription.Thumbnail = new Uri(new Uri(subscription.ChannelId), feed.ImageUrl).AbsoluteUri;

        await db.SaveChangesAsync(context.CancellationToken);

        foreach (var item in feed.Items)
            await SyncVideo(item, subscription, context.CancellationToken);
    }

    private async Task SyncVideo(SyndicationItem item, Subscription subscription, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        item.Id = item.ElementExtensions.ReadElementExtensions<string>("identifier", "http://purl.org/dc/elements/1.1/")
            .FirstOrDefault() ?? item.Id;

        var video = await db.Videos.SingleOrDefaultAsync(v => v.VideoId == item.Id && v.SubscriptionId == subscription.Id, cancellationToken);

        if (video == null)
        {
            video = new Video
            {
                VideoId = item.Id,
                Name = item.Title.Text,
                Description = item.Summary.Text,
                SubscriptionId = subscription.Id,
                UploaderName = subscription.Name,
            };

            db.Videos.Add(video);
        }

        video.VideoId = item.Id;
        video.Name = item.Title.Text;
        video.New = DateTimeOffset.UtcNow - item.PublishDate <= TimeSpan.FromDays(7);
        video.PublishDate = item.PublishDate.UtcDateTime;
        //video.Thumb = "";
        video.Description = item.Summary.Text;
        var rawDuration = item.ElementExtensions
            .ReadElementExtensions<string>("duration", "http://www.itunes.com/dtds/podcast-1.0.dtd")
            .FirstOrDefault();
        if (rawDuration != null)
            video.DurationSpan = TimeSpan.Parse(rawDuration);
        video.UploaderName = string.Join(", ", item.ElementExtensions.ReadElementExtensions<string>("author", "http://www.itunes.com/dtds/podcast-1.0.dtd")
                                               ?? item.Authors.Select(static a => a.Name));
        video.DownloadedPath = item.Links.SingleOrDefault(static l => l.RelationshipType == "enclosure")?.Uri.ToString();

        await db.SaveChangesAsync(cancellationToken);
    }
}
