using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class SampleProjectsSchemaV3Tests
{
    [Theory]
    [InlineData("demo-layout.trackflow.json")]
    [InlineData("demo-stanica-zhlavie.trackflow.json")]
    [InlineData("demo-faza-2-1-runtime.trackflow.json")]
    public void SampleProject_UsesSchemaV3_AndCanonicalRouteDirections(string sampleFileName)
    {
        var root = FindRepositoryRoot();
        var samplePath = Path.Combine(root, "Samples", sampleFileName);
        Assert.True(File.Exists(samplePath), $"Sample file not found: {samplePath}");

        var json = File.ReadAllText(samplePath);
        using var doc = JsonDocument.Parse(json);

        var project = doc.RootElement;
        Assert.Equal(3, project.GetProperty("SchemaVersion").GetInt32());

        var layout = project.GetProperty("Layout");
        Assert.Equal(3, layout.GetProperty("SchemaVersion").GetInt32());

        var routes = layout.GetProperty("Routes").EnumerateArray().ToList();
        foreach (var route in routes)
        {
            Assert.True(IsCanonicalDirection(route.GetProperty("FromBlockDirection").GetString()));
            Assert.True(IsCanonicalDirection(route.GetProperty("ToBlockDirection").GetString()));
            Assert.True(IsCanonicalDirection(route.GetProperty("StartNavigationDirection").GetString()));
            Assert.Equal("Stop", route.GetProperty("SafetyFallbackAspect").GetString());
        }
    }

    private static bool IsCanonicalDirection(string? value)
        => string.Equals(value, "Left", StringComparison.Ordinal)
           || string.Equals(value, "Right", StringComparison.Ordinal)
           || string.Equals(value, "Up", StringComparison.Ordinal)
           || string.Equals(value, "Down", StringComparison.Ordinal);

    [Fact]
    public void Phase21DemoSample_LoadsThroughSettingsManager_WithExpectedRoutesAndSignal()
    {
        var root = FindRepositoryRoot();
        var samplePath = Path.Combine(root, "Samples", "demo-faza-2-1-runtime.trackflow.json");

        var settings = new SettingsManager();
        settings.OpenProject(samplePath);

        var project = settings.CurrentProject;
        Assert.NotNull(project);
        var layout = project!.Layout;
        Assert.NotNull(layout);

        Assert.Equal(2, layout.Routes.Count);
        Assert.Contains(layout.Routes, r => r.Id == "r_straight");
        Assert.Contains(layout.Routes, r => r.Id == "r_diverge");

        var startBlock = Assert.Single(layout.Elements.OfType<BlockElement>(), b => b.Id == "blk_a");
        var startSignal = Assert.Single(layout.Elements.OfType<SignalElement>(), s => s.Id == "sig_start");

        Assert.Equal("sig_start", startBlock.SignalRightId);
        Assert.Equal("3-aspect", startSignal.SignalProfile);
        Assert.Single(project.Locomotives);
        Assert.Equal("Demo 754", project.Locomotives[0].Name);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TrackFlow.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing TrackFlow.sln.");
    }
}

