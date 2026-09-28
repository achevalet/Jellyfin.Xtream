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
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Xtream.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Xtream.Tests;

/// <summary>
/// Tests for the API rate limiter. Only lower bounds are asserted, as a loaded machine may be slower.
/// </summary>
public class RequestThrottleTests
{
    private static XtreamClient CreateClient() =>
        new XtreamClient(new HttpClient(), NullLogger<XtreamClient>.Instance);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public async Task ThrottleAsync_WhenDisabled_DoesNotWait(double maxRequestsPerSecond)
    {
        using XtreamClient client = CreateClient();
        Stopwatch stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < 20; i++)
        {
            await client.ThrottleAsync(maxRequestsPerSecond, CancellationToken.None);
        }

        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"Took {stopwatch.ElapsedMilliseconds}ms with throttling disabled.");
    }

    [Fact]
    public async Task ThrottleAsync_SpacesSequentialCallers()
    {
        const int Calls = 5;
        const double Rate = 20.0;
        using XtreamClient client = CreateClient();

        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < Calls; i++)
        {
            await client.ThrottleAsync(Rate, CancellationToken.None);
        }

        stopwatch.Stop();

        // The first caller goes immediately, so only the remaining ones are spaced.
        double expected = (Calls - 1) * (1000.0 / Rate);
        Assert.True(
            stopwatch.Elapsed.TotalMilliseconds >= expected * 0.8,
            $"Took {stopwatch.Elapsed.TotalMilliseconds}ms, expected at least {expected * 0.8}ms.");
    }

    [Fact]
    public async Task ThrottleAsync_SpacesConcurrentCallers()
    {
        const int Calls = 8;
        const double Rate = 20.0;
        using XtreamClient client = CreateClient();

        long[] finished = new long[Calls];
        Stopwatch stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, Calls).Select(async i =>
        {
            await client.ThrottleAsync(Rate, CancellationToken.None);
            finished[i] = stopwatch.ElapsedMilliseconds;
        }));

        stopwatch.Stop();

        double expected = (Calls - 1) * (1000.0 / Rate);
        Assert.True(
            stopwatch.Elapsed.TotalMilliseconds >= expected * 0.8,
            $"Took {stopwatch.Elapsed.TotalMilliseconds}ms, expected at least {expected * 0.8}ms.");

        // Each caller holds its own slot, so the nth is not released before n intervals have passed.
        long[] ordered = finished.OrderBy(t => t).ToArray();
        for (int i = 0; i < ordered.Length; i++)
        {
            double earliest = i * (1000.0 / Rate) * 0.8;
            Assert.True(
                ordered[i] >= earliest,
                $"Caller {i} was released after {ordered[i]}ms, before its slot at {earliest}ms.");
        }
    }

    [Theory]
    [InlineData(0.0000001)]
    [InlineData(1e-320)]
    [InlineData(double.Epsilon)]
    public async Task ThrottleAsync_AbsurdlyLowRate_ClampsInsteadOfThrowing(double maxRequestsPerSecond)
    {
        using XtreamClient client = CreateClient();
        using CancellationTokenSource cts = new CancellationTokenSource();

        // The first caller claims the immediate slot, so the second one has to wait the interval.
        await client.ThrottleAsync(maxRequestsPerSecond, CancellationToken.None);

        Task throttled = client.ThrottleAsync(maxRequestsPerSecond, cts.Token);
        Assert.False(throttled.IsFaulted, "The rate limiter rejected the configured interval.");

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => throttled);
    }

    [Fact]
    public async Task ThrottleAsync_NotANumber_DoesNotWait()
    {
        using XtreamClient client = CreateClient();
        Stopwatch stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < 20; i++)
        {
            await client.ThrottleAsync(double.NaN, CancellationToken.None);
        }

        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"Took {stopwatch.ElapsedMilliseconds}ms for a NaN rate.");
    }

    [Fact]
    public async Task ThrottleAsync_ObservesCancellation()
    {
        using XtreamClient client = CreateClient();
        using CancellationTokenSource cts = new CancellationTokenSource();

        // Claim the only immediate slot, so the next caller has to wait.
        await client.ThrottleAsync(1.0, CancellationToken.None);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ThrottleAsync(1.0, cts.Token));
    }
}
