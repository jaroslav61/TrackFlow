using System.Linq;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class LayoutOverlapIntegrityServiceTests
{
    [Fact]
    public void FindIllegalOverlaps_DveRovneKolajeNaRovnakejBunke_NajdeProblemATipujeDuplikat()
    {
        var layout = new TrackLayout();
        layout.Elements.Add(new TrackSegmentElement { Id = "seg_1", MarkerKey = "TrackSegment", X = 24, Y = 48, Rotation = 0 });
        layout.Elements.Add(new TrackSegmentElement { Id = "seg_2", MarkerKey = "TrackSegment", X = 24, Y = 48, Rotation = 0 });

        var issues = LayoutOverlapIntegrityService.FindIllegalOverlaps(layout, cellSize: 24.0);
        var issue = Assert.Single(issues);

        var msg = LayoutOverlapIntegrityService.BuildIssueMessage(issue);
        Assert.Contains("duplicit", msg);
    }

    [Fact]
    public void FindIllegalOverlaps_RovnaKolajPodVyhybkou_TipujeZmazanieTrackSegmentu()
    {
        var layout = new TrackLayout();
        layout.Elements.Add(new TrackSegmentElement { Id = "seg_1", MarkerKey = "TrackSegment", X = 0, Y = 0, Rotation = 0 });
        layout.Elements.Add(new TurnoutElement { Id = "sw_1", MarkerKey = "Turnout_L", X = 0, Y = 0, Rotation = 0 });

        var issues = LayoutOverlapIntegrityService.FindIllegalOverlaps(layout, cellSize: 24.0);
        var issue = Assert.Single(issues);

        var msg = LayoutOverlapIntegrityService.BuildIssueMessage(issue);
        Assert.Contains("zvyšná rovná koľaj", msg);
        Assert.Contains("TrackSegment", msg);
    }
}

