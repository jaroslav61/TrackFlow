using System.Linq;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class RouteIntegrityServiceTests
{
    [Fact]
    public void ValidateAndRepairOnLoad_NahlasiChybajucePathElementIds()
    {
        var layout = new TrackLayout();
        layout.Elements.Add(new BlockElement { Id = "blk_a", MarkerKey = "Block", X = 0, Y = 0, BlockLengthCells = 1, Rotation = 0 });
        layout.Elements.Add(new BlockElement { Id = "blk_b", MarkerKey = "Block", X = 72, Y = 0, BlockLengthCells = 1, Rotation = 0 });

        var route = new RouteDefinition
        {
            Id = "r1",
            Name = "A->B",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            Kind = RouteDefinitionKind.UserDefinedRoute
        };
        route.PathElementIds.Add("missing_element");
        layout.Routes.Add(route);

        var report = RouteIntegrityService.ValidateAndRepairOnLoad(layout, autoRepairManualRoutes: true);

        var issue = Assert.Single(report.Issues);
        Assert.Equal("r1", issue.RouteId);
        Assert.Contains("missing_element", issue.MissingPathElementIds);
        Assert.Empty(issue.Repairs);
    }

    [Fact]
    public void ValidateAndRepairOnLoad_AutoOpraviJedenChybajuciPrvokMedziDvojicouPoSebeIducichPrvkov()
    {
        // Layout: blkA -- seg1 -- seg2 -- blkB (horizontálne, veľkosť bunky = 24)
        var layout = new TrackLayout();
        layout.Elements.Add(new BlockElement { Id = "blk_a", MarkerKey = "Block", X = 0, Y = 0, BlockLengthCells = 1, Rotation = 0 });
        layout.Elements.Add(new TrackSegmentElement { Id = "seg_1", MarkerKey = "TrackSegment", X = 24, Y = 0, Rotation = 0 });
        layout.Elements.Add(new TrackSegmentElement { Id = "seg_2", MarkerKey = "TrackSegment", X = 48, Y = 0, Rotation = 0 });
        layout.Elements.Add(new BlockElement { Id = "blk_b", MarkerKey = "Block", X = 72, Y = 0, BlockLengthCells = 1, Rotation = 0 });

        // V ceste chýba seg_1, ale seg_1 existuje a je to jediný 1-krokový most medzi blk_a a seg_2.
        var route = new RouteDefinition
        {
            Id = "r1",
            Name = "A->B",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            Kind = RouteDefinitionKind.UserDefinedRoute
        };
        route.PathElementIds.Add("seg_2");
        layout.Routes.Add(route);

        var report = RouteIntegrityService.ValidateAndRepairOnLoad(layout, autoRepairManualRoutes: true);

        var issue = Assert.Single(report.Issues);
        Assert.Empty(issue.MissingPathElementIds);
        var repair = Assert.Single(issue.Repairs);
        Assert.Equal(0, repair.InsertIndex);
        Assert.Equal("seg_1", repair.InsertedElementId);

        Assert.Equal(new[] { "seg_1", "seg_2" }, layout.Routes.Single().PathElementIds.ToArray());
    }

    [Fact]
    public void ValidateAndRepairOnLoad_NahradiChybajuciOdkazVPathElementIdsJednoznacnymMedziPrvkom()
    {
        // Layout: blkA -- seg1 -- seg2 -- blkB
        var layout = new TrackLayout();
        layout.Elements.Add(new BlockElement { Id = "blk_a", MarkerKey = "Block", X = 0, Y = 0, BlockLengthCells = 1, Rotation = 0 });
        layout.Elements.Add(new TrackSegmentElement { Id = "seg_1", MarkerKey = "TrackSegment", X = 24, Y = 0, Rotation = 0 });
        layout.Elements.Add(new TrackSegmentElement { Id = "seg_2", MarkerKey = "TrackSegment", X = 48, Y = 0, Rotation = 0 });
        layout.Elements.Add(new BlockElement { Id = "blk_b", MarkerKey = "Block", X = 72, Y = 0, BlockLengthCells = 1, Rotation = 0 });

        // Cesta obsahuje chýbajúce ID (napr. zmazaný duplicitný segment), ale seg_1 stále existuje.
        var route = new RouteDefinition
        {
            Id = "r1",
            Name = "A->B",
            FromBlockId = "blk_a",
            ToBlockId = "blk_b",
            Kind = RouteDefinitionKind.UserDefinedRoute
        };

        route.PathElementIds.Add("missing_element");
        route.PathElementIds.Add("seg_2");
        layout.Routes.Add(route);

        var report = RouteIntegrityService.ValidateAndRepairOnLoad(layout, autoRepairManualRoutes: true);

        var issue = Assert.Single(report.Issues);
        Assert.Empty(issue.MissingPathElementIds);
        Assert.NotEmpty(issue.Repairs);

        Assert.Equal(new[] { "seg_1", "seg_2" }, layout.Routes.Single().PathElementIds.ToArray());
    }
}



