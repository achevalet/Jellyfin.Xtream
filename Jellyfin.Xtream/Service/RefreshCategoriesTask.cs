// Copyright (C) 2022  Kevin Jilissen

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Xtream.Configuration;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Xtream.Service;

/// <summary>
/// Lists every category of the VOD and series channels, so their items exist without being browsed first.
/// </summary>
/// <param name="channelManager">Instance of the <see cref="IChannelManager"/> interface.</param>
/// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
/// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
/// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
/// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
public class RefreshCategoriesTask(
    IChannelManager channelManager,
    ILibraryManager libraryManager,
    IProviderManager providerManager,
    IFileSystem fileSystem,
    ILogger<RefreshCategoriesTask> logger) : IScheduledTask
{
    /// <inheritdoc />
    public string Name => "Refresh Xtream categories";

    /// <inheritdoc />
    public string Key => "XtreamRefreshCategories";

    /// <inheritdoc />
    public string Description => "Lists the categories of the Xtream Video On-Demand and Series channels, so their movies and series are up to date without browsing them.";

    /// <inheritdoc />
    public string Category => "Channels";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(24).Ticks,
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        PluginConfiguration config = Plugin.Instance.Configuration;
        List<string> channelNames = [];
        if (config.IsVodVisible)
        {
            channelNames.Add(VodChannel.ChannelName);
        }

        if (config.IsSeriesVisible)
        {
            channelNames.Add(SeriesChannel.ChannelName);
        }

        List<(Guid ChannelId, BaseItem Category)> categories = [];
        foreach (string channelName in channelNames)
        {
            // The id Jellyfin gives the channel it registers for the plugin channel of this name.
            Guid channelId = libraryManager.GetNewItemId("Channel " + channelName, typeof(Channel));
            try
            {
                QueryResult<BaseItem> result = await ListAsync(channelId, null, cancellationToken).ConfigureAwait(false);
                foreach (BaseItem category in result.Items)
                {
                    categories.Add((channelId, category));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to list the categories of channel {Channel}", channelName);
            }
        }

        for (int i = 0; i < categories.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (Guid channelId, BaseItem category) = categories[i];
            try
            {
                await ListAsync(channelId, category.Id, cancellationToken).ConfigureAwait(false);

                // Now that the category has children, a refresh gives it a collage of their images.
                providerManager.QueueRefresh(
                    category.Id,
                    new MetadataRefreshOptions(new DirectoryService(fileSystem)),
                    RefreshPriority.Low);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to refresh category {Category}", category.Name);
            }

            progress.Report(100.0 * (i + 1) / categories.Count);
        }

        progress.Report(100);
    }

    private Task<QueryResult<BaseItem>> ListAsync(Guid channelId, Guid? parentId, CancellationToken cancellationToken)
    {
        InternalItemsQuery query = new()
        {
            ChannelIds = [channelId],
        };
        if (parentId.HasValue)
        {
            query.ParentId = parentId.Value;
        }

        return channelManager.GetChannelItemsInternal(query, new Progress<double>(), cancellationToken);
    }
}
