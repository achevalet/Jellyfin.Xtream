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
using Jellyfin.Xtream.Service;
using MediaBrowser.Model.Drawing;
using Xunit;

namespace Jellyfin.Xtream.Tests;

/// <summary>
/// Tests that the channel images are embedded in the plugin.
/// </summary>
public class ChannelImageTests
{
    private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Theory]
    [InlineData("catchup.png")]
    [InlineData("movies.png")]
    [InlineData("series.png")]
    public void GetEmbeddedImage_ChannelImage_IsPng(string name)
    {
        var image = StreamService.GetEmbeddedImage(name);

        Assert.True(image.HasImage);
        Assert.Equal(ImageFormat.Png, image.Format);
        using var stream = image.Stream;
        var header = new byte[_pngSignature.Length];
        stream.ReadExactly(header);
        Assert.Equal(_pngSignature, header);
    }

    [Fact]
    public void GetEmbeddedImage_Missing_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => StreamService.GetEmbeddedImage("missing.png"));
    }
}
