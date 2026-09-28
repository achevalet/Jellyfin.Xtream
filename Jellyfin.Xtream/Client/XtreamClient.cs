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
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Xtream.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

#pragma warning disable CS1591
namespace Jellyfin.Xtream.Client;

/// <summary>
/// The Xtream API client implementation.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="XtreamClient"/> class.
/// </remarks>
/// <param name="client">The HTTP client used.</param>
/// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
public class XtreamClient(HttpClient client, ILogger<XtreamClient> logger) : IDisposable, IXtreamClient
{
    /// <summary>
    /// The longest interval the rate limiter will wait between two requests, in seconds.
    /// </summary>
    private const double MaxThrottleIntervalSeconds = 300;

    private readonly JsonSerializerSettings _serializerSettings = new()
    {
        Error = NullableEventHandler(logger),
    };

    private readonly SemaphoreSlim _requestSemaphore = new(1, 1);
    private DateTimeOffset _nextRequestTime = DateTimeOffset.MinValue;

    public void UpdateUserAgent()
    {
        client.DefaultRequestHeaders.UserAgent.Clear();
        if (string.IsNullOrWhiteSpace(Plugin.Instance.Configuration.UserAgent))
        {
            ProductHeaderValue header = new ProductHeaderValue("Jellyfin.Xtream", Assembly.GetExecutingAssembly().GetName().Version?.ToString());
            ProductInfoHeaderValue userAgent = new ProductInfoHeaderValue(header);
            client.DefaultRequestHeaders.UserAgent.Add(userAgent);
        }
        else
        {
            // Trust the correctness of the configuration.
            client.DefaultRequestHeaders.Add("User-Agent", Plugin.Instance.Configuration.UserAgent);
        }
    }

    /// <summary>
    /// Ignores error events if the target property is nullable.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    /// <returns>An event handler using the given logger.</returns>
    public static EventHandler<ErrorEventArgs> NullableEventHandler(ILogger<XtreamClient> logger)
    {
        return (object? sender, ErrorEventArgs args) =>
        {
            if (args.ErrorContext.OriginalObject?.GetType() is Type type && args.ErrorContext.Member is string jsonName)
            {
                PropertyInfo? property = type.GetProperties().FirstOrDefault((p) =>
                {
                    CustomAttributeData? attribute = p.CustomAttributes.FirstOrDefault(a => a.AttributeType == typeof(JsonPropertyAttribute));
                    if (attribute == null)
                    {
                        return false;
                    }

                    if (attribute.ConstructorArguments.Count > 0)
                    {
                        // Attribute contains a `propertyName`.
                        string? value = attribute.ConstructorArguments.First().Value as string;
                        return jsonName.Equals(value, StringComparison.Ordinal);
                    }
                    else
                    {
                        // Attribute does not contain a `propertyName`, compare with the name of the property itself.
                        return jsonName.Equals(p.Name, StringComparison.Ordinal);
                    }
                });

                if (property != null && Nullable.GetUnderlyingType(property.PropertyType) != null)
                {
                    logger.LogDebug("Property `{0}` (`{1}` in JSON) is nullable, ignoring parsing error!", property.Name, jsonName);
                    logger.LogDebug("Stack trace: {0}", new System.Diagnostics.StackTrace());
                    args.ErrorContext.Handled = true;
                }
            }
        };
    }

    /// <summary>
    /// Waits until the configured rate limit allows another request to be sent.
    /// </summary>
    /// <param name="maxRequestsPerSecond">The limit, or 0 or less to disable throttling.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task which completes once the caller may send its request.</returns>
    internal async Task ThrottleAsync(double maxRequestsPerSecond, CancellationToken cancellationToken)
    {
        // Negated so that a NaN rate, which no comparison accepts, disables throttling too.
        if (!(maxRequestsPerSecond > 0))
        {
            return;
        }

        // An absurdly low rate would otherwise ask for a delay longer than Task.Delay accepts.
        TimeSpan minInterval = TimeSpan.FromSeconds(Math.Min(1.0 / maxRequestsPerSecond, MaxThrottleIntervalSeconds));
        DateTimeOffset slot;
        await _requestSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            slot = _nextRequestTime > now ? _nextRequestTime : now;

            // Space from the claimed slot, not from now, so the interval does not drift.
            _nextRequestTime = slot + minInterval;
        }
        finally
        {
            _requestSemaphore.Release();
        }

        TimeSpan wait = slot - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string> QueryApiRaw(ConnectionInfo connectionInfo, string urlPath, CancellationToken cancellationToken)
    {
        await ThrottleAsync(Plugin.Instance.Configuration.MaxApiRequestsPerSecond, cancellationToken).ConfigureAwait(false);
        Uri uri = new Uri(connectionInfo.BaseUrl + urlPath);
        return await client.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> QueryApi<T>(ConnectionInfo connectionInfo, string urlPath, CancellationToken cancellationToken)
    {
        string jsonContent = await QueryApiRaw(connectionInfo, urlPath, cancellationToken).ConfigureAwait(false);
        return JsonConvert.DeserializeObject<T>(jsonContent, _serializerSettings)!;
    }

    public Task<PlayerApi> GetUserAndServerInfoAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken) =>
        QueryApi<PlayerApi>(
          connectionInfo,
          $"/player_api.php?username={connectionInfo.UserName}&password={connectionInfo.Password}",
          cancellationToken);

    public Task<List<Series>> GetSeriesByCategoryAsync(ConnectionInfo connectionInfo, int categoryId, CancellationToken cancellationToken) =>
         QueryApi<List<Series>>(
           connectionInfo,
           $"/player_api.php?username={connectionInfo.UserName}&password={connectionInfo.Password}&action=get_series&category_id={categoryId}",
           cancellationToken);

    public Task<SeriesStreamInfo> GetSeriesStreamsBySeriesAsync(ConnectionInfo connectionInfo, int seriesId, CancellationToken cancellationToken) =>
         QueryApi<SeriesStreamInfo>(
           connectionInfo,
           $"/player_api.php?username={connectionInfo.UserName}&password={connectionInfo.Password}&action=get_series_info&series_id={seriesId}",
           cancellationToken);

    public Task<List<StreamInfo>> GetVodStreamsByCategoryAsync(ConnectionInfo connectionInfo, int categoryId, CancellationToken cancellationToken) =>
         QueryApi<List<StreamInfo>>(
           connectionInfo,
           $"/player_api.php?username={connectionInfo.UserName}&password={connectionInfo.Password}&action=get_vod_streams&category_id={categoryId}",
           cancellationToken);

    public Task<VodStreamInfo> GetVodInfoAsync(ConnectionInfo connectionInfo, int streamId, CancellationToken cancellationToken) =>
         QueryApi<VodStreamInfo>(
           connectionInfo,
           $"/player_api.php?username={connectionInfo.UserName}&password={connectionInfo.Password}&action=get_vod_info&vod_id={streamId}",
           cancellationToken);

    public Task<List<StreamInfo>> GetLiveStreamsAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken) =>
         QueryApi<List<StreamInfo>>(
           connectionInfo,
           $"/player_api.php?username={connectionInfo.UserName}&password={connectionInfo.Password}&action=get_live_streams",
           cancellationToken);

    public Task<List<StreamInfo>> GetLiveStreamsByCategoryAsync(ConnectionInfo connectionInfo, int categoryId, CancellationToken cancellationToken) =>
         QueryApi<List<StreamInfo>>(
           connectionInfo,
           $"/player_api.php?username={connectionInfo.UserName}&password={connectionInfo.Password}&action=get_live_streams&category_id={categoryId}",
           cancellationToken);

    public Task<List<Category>> GetSeriesCategoryAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken) =>
         QueryApi<List<Category>>(
           connectionInfo,
           $"/player_api.php?username={connectionInfo.UserName}&password={connectionInfo.Password}&action=get_series_categories",
           cancellationToken);

    public Task<List<Category>> GetVodCategoryAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken) =>
         QueryApi<List<Category>>(
           connectionInfo,
           $"/player_api.php?username={connectionInfo.UserName}&password={connectionInfo.Password}&action=get_vod_categories",
           cancellationToken);

    public Task<List<Category>> GetLiveCategoryAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken) =>
         QueryApi<List<Category>>(
           connectionInfo,
           $"/player_api.php?username={connectionInfo.UserName}&password={connectionInfo.Password}&action=get_live_categories",
           cancellationToken);

    public Task<EpgListings> GetEpgInfoAsync(ConnectionInfo connectionInfo, int streamId, CancellationToken cancellationToken) =>
         QueryApi<EpgListings>(
           connectionInfo,
           $"/player_api.php?username={connectionInfo.UserName}&password={connectionInfo.Password}&action=get_simple_data_table&stream_id={streamId}",
           cancellationToken);

    /// <summary>
    /// Dispose the HTTP client.
    /// </summary>
    /// <param name="b">Unused.</param>
    protected virtual void Dispose(bool b)
    {
        client?.Dispose();
        _requestSemaphore.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
