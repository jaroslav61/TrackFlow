using System.Collections.ObjectModel;
using System.Linq;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.ViewModels.Editor;
using TrackFlow.Services.Dcc;
using Xunit;

namespace TrackFlow.Tests;

public class LayoutEditorRegressionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(20)]
    public void PlaceElementAt_NeumiestniMarkerDoObsadenejBunkyBloku_Horizontal(int blockLengthCells)
    {
        var vm = new LayoutEditorViewModel
        {
            SelectedTool = LayoutTool.Place,
            PendingElementType = LayoutElementType.TrackSegment,
            SelectedMarkerKey = "TrackSegment"
        };

        vm.Elements.Add(new BlockElement
        {
            MarkerKey = "Block",
            X = 0,
            Y = 0,
            Rotation = 0,
            BlockLengthCells = blockLengthCells
        });

        var before = vm.Elements.Count;
        var xInsideLastCell = (blockLengthCells - 1) * LayoutEditorViewModel.CellSize;

        vm.PlaceElementAt(xInsideLastCell, 0);

        Assert.Equal(before, vm.Elements.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(20)]
    public void PlaceElementAt_NeumiestniMarkerDoObsadenejBunkyBloku_Vertical(int blockLengthCells)
    {
        var vm = new LayoutEditorViewModel
        {
            SelectedTool = LayoutTool.Place,
            PendingElementType = LayoutElementType.TrackSegment,
            SelectedMarkerKey = "TrackSegment"
        };

        vm.Elements.Add(new BlockElement
        {
            MarkerKey = "Block",
            X = 0,
            Y = 0,
            Rotation = 90,
            BlockLengthCells = blockLengthCells
        });

        var before = vm.Elements.Count;
        var yInsideLastCell = (blockLengthCells - 1) * LayoutEditorViewModel.CellSize;

        vm.PlaceElementAt(0, yInsideLastCell);

        Assert.Equal(before, vm.Elements.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(20)]
    public void PlaceElementAt_UmiestniMarkerMimoRozsahBloku_Horizontal(int blockLengthCells)
    {
        var vm = new LayoutEditorViewModel
        {
            SelectedTool = LayoutTool.Place,
            PendingElementType = LayoutElementType.TrackSegment,
            SelectedMarkerKey = "TrackSegment"
        };

        vm.Elements.Add(new BlockElement
        {
            MarkerKey = "Block",
            X = 0,
            Y = 0,
            Rotation = 0,
            BlockLengthCells = blockLengthCells
        });

        var before = vm.Elements.Count;
        var xRightAfterBlock = blockLengthCells * LayoutEditorViewModel.CellSize;

        vm.PlaceElementAt(xRightAfterBlock, 0);

        Assert.Equal(before + 1, vm.Elements.Count);
    }

    [Fact]
    public void SelectElementAt_VyberieAjPoslednuBunkuDlhehoBloku()
    {
        var vm = new LayoutEditorViewModel();
        var block = new BlockElement
        {
            MarkerKey = "Block",
            X = 0,
            Y = 0,
            Rotation = 0,
            BlockLengthCells = 20
        };
        vm.Elements.Add(block);

        var xInsideLastCell = 19 * LayoutEditorViewModel.CellSize + 1;
        var yInside = 1;

        vm.SelectElementAt(xInsideLastCell, yInside);

        Assert.Same(block, vm.SelectedElement);
    }

    [Fact]
    public void SmartStripsLocomotives_PoOdstraneniLokyUzNesposobiRepaint()
    {
        var vm = new LayoutEditorViewModel();
        var locos = new ObservableCollection<Locomotive>();
        var loco = new Locomotive("754", "Test loko");

        var block = new BlockElement
        {
            MarkerKey = "Block",
            AssignedLocoId = loco.Code
        };
        vm.Elements.Add(block);

        var repaintCount = 0;
        vm.RequestBlockRepaint = _ => repaintCount++;
        vm.SmartStripsLocomotives = locos;

        locos.Add(loco);
        loco.Name = "Test loko 2";
        Assert.True(repaintCount > 0);

        var afterSubscribedChange = repaintCount;
        locos.Remove(loco);
        loco.Name = "Test loko 3";

        Assert.Equal(afterSubscribedChange, repaintCount);
    }

    [Fact]
    public void SameDccSignature_HostJeCaseInsensitive_AlePortAJTypMusisedia()
    {
        var a = new EffectiveSettings
        {
            DccCentralType = DccCentralType.Z21,
            DccCentralHost = "192.168.0.111",
            DccCentralPort = 21105
        };

        var b = new EffectiveSettings
        {
            DccCentralType = DccCentralType.Z21,
            DccCentralHost = "192.168.0.111".ToUpperInvariant(),
            DccCentralPort = 21105
        };

        var c = new EffectiveSettings
        {
            DccCentralType = DccCentralType.Z21,
            DccCentralHost = "192.168.0.111",
            DccCentralPort = 5555
        };

        Assert.True(DccConnectionService.SameDccSignature(a, b));
        Assert.False(DccConnectionService.SameDccSignature(a, c));
    }

    [Fact]
    public void PlaceElementCommand_Route_VytvoriRouteElementSoSpravnymMarkerKey()
    {
        var vm = new LayoutEditorViewModel
        {
            SelectedTool = LayoutTool.Place
        };

        vm.PlaceElementCommand.Execute("Route");
        vm.PlaceElementAt(0, 0);

        var placed = Assert.Single(vm.Elements);
        var route = Assert.IsType<RouteElement>(placed);
        Assert.Equal("Route", route.MarkerKey);
    }

    [Fact]
    public void PlaceElementCommand_Signal4_VytvoriPlnyOdchodovySignalProfil()
    {
        var vm = new LayoutEditorViewModel
        {
            SelectedTool = LayoutTool.Place
        };

        vm.PlaceElementCommand.Execute("Signal4");
        vm.PlaceElementAt(0, 0);

        var placed = Assert.Single(vm.Elements);
        var signal = Assert.IsType<SignalElement>(placed);
        Assert.Equal("Signal", signal.MarkerKey);
        Assert.Equal("5-aspect-departure", signal.SignalProfile);
    }

    [Fact]
    public void PlaceElementCommand_Signal_PriradiAutoNazovNaPoradoveCislo()
    {
        var vm = new LayoutEditorViewModel
        {
            SelectedTool = LayoutTool.Place
        };

        vm.PlaceElementCommand.Execute("Signal");
        vm.PlaceElementAt(0, 0);
        vm.PlaceElementAt(LayoutEditorViewModel.CellSize, 0);

        var signals = vm.Elements.OfType<SignalElement>().ToList();
        Assert.Equal(2, signals.Count);
        Assert.Equal("Na1", signals[0].Label);
        Assert.Equal("Na2", signals[1].Label);
    }

    [Fact]
    public void InspectorDirectionalSignals_ProdukujeHighlightIds_AUkladaDoBloku()
    {
        var vm = new LayoutEditorViewModel();

        var block = new BlockElement { MarkerKey = "Block", Id = "b1" };
        var sLeft = new SignalElement { Id = "s-left", MarkerKey = "Signal", Label = "S-L" };
        var sRight = new SignalElement { Id = "s-right", MarkerKey = "Signal", Label = "S-R" };

        vm.Elements.Add(block);
        vm.Elements.Add(sLeft);
        vm.Elements.Add(sRight);
        vm.SelectedElement = block;

        var leftOpt = vm.InspectorDirectionalSignalItems.First(x => x.Id == "s-left");
        var rightOpt = vm.InspectorDirectionalSignalItems.First(x => x.Id == "s-right");

        vm.InspectorSelectedSignalLeft = leftOpt;
        vm.InspectorSelectedSignalRight = rightOpt;

        Assert.Equal("s-left", block.SignalLeftId);
        Assert.Equal("s-right", block.SignalRightId);
        Assert.Contains("s-left", vm.InspectorHighlightedSignalIds);
        Assert.Contains("s-right", vm.InspectorHighlightedSignalIds);
        Assert.True(vm.IsInspectorDirectionalSignalHighlighted(sLeft));
        Assert.True(vm.IsInspectorDirectionalSignalHighlighted(sRight));
    }

    [Fact]
    public void OnBlockPropertiesEdited_NotifyInspector_NeprepiseSmerovePriradeniaNaNull()
    {
        // Regression: NotifyInspector() volá RebuildInspectorDirectionalSignalItems(),
        // ktorý Clear()-uje ItemsSource ComboBoxov. Bez ochrany tým ComboBox spätne
        // nastaví SelectedItem=null cez binding a setter prepíše block.Signal*Id na null.
        var vm = new LayoutEditorViewModel();

        var block = new BlockElement
        {
            MarkerKey = "Block",
            Id = "b1",
            SignalLeftId = "s-left",
            SignalRightId = "s-right",
            SignalUpId = "s-up",
            SignalDownId = "s-down"
        };
        vm.Elements.Add(block);
        vm.Elements.Add(new SignalElement { Id = "s-left", MarkerKey = "Signal", Label = "L" });
        vm.Elements.Add(new SignalElement { Id = "s-right", MarkerKey = "Signal", Label = "R" });
        vm.Elements.Add(new SignalElement { Id = "s-up", MarkerKey = "Signal", Label = "U" });
        vm.Elements.Add(new SignalElement { Id = "s-down", MarkerKey = "Signal", Label = "D" });
        vm.SelectedElement = block;

        // Simuluje refresh inšpektora po zatvorení dialógu Vlastnosti bloku.
        vm.OnBlockPropertiesEdited();

        Assert.Equal("s-left", block.SignalLeftId);
        Assert.Equal("s-right", block.SignalRightId);
        Assert.Equal("s-up", block.SignalUpId);
        Assert.Equal("s-down", block.SignalDownId);
    }
}
