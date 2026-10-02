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

using Jellyfin.Xtream.Client.Models;
using Jellyfin.Xtream.Providers;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Xtream.Tests;

/// <summary>
/// Tests that the details the server provides for a movie fill what the movie lacks.
/// </summary>
public class VodDetailsTests
{
    [Fact]
    public void ApplyDetails_MovieWithoutGenres_TakesGenresFromServer()
    {
        Movie movie = new();

        XtreamVodProvider.ApplyDetails(movie, new VodInfo { Genre = "Drama, Comedy" });

        Assert.Equal(new[] { "Drama", "Comedy" }, movie.Genres);
    }

    [Fact]
    public void ApplyDetails_MovieWithGenres_KeepsThem()
    {
        Movie movie = new() { Genres = ["Action"] };

        XtreamVodProvider.ApplyDetails(movie, new VodInfo { Genre = "Drama" });

        Assert.Equal(new[] { "Action" }, movie.Genres);
    }

    [Fact]
    public void ApplyDetails_NoGenresFromServer_LeavesNone()
    {
        Movie movie = new();

        XtreamVodProvider.ApplyDetails(movie, new VodInfo { Genre = null });

        Assert.Empty(movie.Genres);
    }

    [Fact]
    public void ApplyDetails_FillsPlot()
    {
        Movie movie = new();

        XtreamVodProvider.ApplyDetails(movie, new VodInfo { Plot = "A plot." });

        Assert.Equal("A plot.", movie.Overview);
    }
}
