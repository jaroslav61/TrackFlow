using System;
using Avalonia.Platform;
using Xunit;

namespace TrackFlow.Tests;

public sealed class AvaloniaAssetPresenceTests
{
    [Fact(Skip = "Requires Avalonia asset services initialized by UI app host.")]
    public void LocoAndWagonIcons_AreEmbeddedAsAvaloniaResources()
    {
        Assert.True(AssetLoader.Exists(new Uri("avares://TrackFlow/Assets/LocoIcons/ice1.png")));
        Assert.True(AssetLoader.Exists(new Uri("avares://TrackFlow/Assets/VagonIcons/car1_40.png")));
    }
}


