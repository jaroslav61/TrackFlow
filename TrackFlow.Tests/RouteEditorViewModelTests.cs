using System;
using System.Linq;
using System.Threading.Tasks;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.ViewModels.Editor;
using Xunit;

namespace TrackFlow.Tests;

public class RouteEditorViewModelTests
{
    [Fact]
    public void Constructor_LoadsAvailableRoutes_FromProjectLayout()
    {
        var settings = CreateSettingsWithProject();
        settings.CurrentProject!.Layout.Routes.Add(new RouteDefinition { Id = "r1", Name = "R1" });
        settings.CurrentProject.Layout.Routes.Add(new RouteDefinition { Id = "r2", Name = "R2" });

        var vm = new RouteEditorViewModel(CreateLayoutVm(settings), settings);

        Assert.Equal(2, vm.AvailableRoutes.Count);
        Assert.Contains(vm.AvailableRoutes, r => r.Id == "r1");
        Assert.Contains(vm.AvailableRoutes, r => r.Id == "r2");
    }

    [Fact]
    public void LoadFromElement_PopulatesFields_AndPreselectsRouteAndSensors()
    {
        var settings = CreateSettingsWithProject();
        settings.CurrentProject!.Layout.Routes.Add(new RouteDefinition { Id = "route-A", Name = "A" });

        var layoutVm = CreateLayoutVm(settings);
        var (sensor1Id, sensor2Id) = AddBlockWithTwoIndicators(layoutVm);

        var routeElement = new RouteElement
        {
            RouteName = "Moja cesta",
            RequestYellow = true,
            MaxSpeed = 70,
            LimitedSpeed = 35,
            SelectedRouteDefinitionId = "route-A",
            IndicatorIds = { sensor2Id }
        };

        var vm = new RouteEditorViewModel(layoutVm, settings);
        vm.LoadFromElement(routeElement);

        Assert.Equal("Moja cesta", vm.RouteName);
        Assert.True(vm.RequestYellow);
        Assert.Equal(70, vm.MaxSpeed);
        Assert.Equal(35, vm.LimitedSpeed);
        Assert.Equal("route-A", vm.GetSelectedRouteDefinitionId());

        var selectedIds = vm.AvailableSensors.Where(s => s.IsSelected).Select(s => s.Id).ToList();
        Assert.Single(selectedIds);
        Assert.Equal(sensor2Id, selectedIds[0]);
        Assert.DoesNotContain(sensor1Id, selectedIds);
    }

    [Fact]
    public void SaveToElement_WritesEditedFields_SelectedRoute_AndSelectedIndicators()
    {
        var settings = CreateSettingsWithProject();
        settings.CurrentProject!.Layout.Routes.Add(new RouteDefinition { Id = "route-A", Name = "A" });
        settings.CurrentProject.Layout.Routes.Add(new RouteDefinition { Id = "route-B", Name = "B" });

        var layoutVm = CreateLayoutVm(settings);
        var (sensor1Id, sensor2Id) = AddBlockWithTwoIndicators(layoutVm);

        var routeElement = new RouteElement();
        var vm = new RouteEditorViewModel(layoutVm, settings);

        vm.RouteName = "Upravená cesta";
        vm.RequestYellow = true;
        vm.MaxSpeed = 90;
        vm.LimitedSpeed = 45;
        vm.SelectRouteDefinitionById("route-B");
        vm.SelectSensorsByIds(new[] { sensor1Id, sensor2Id });

        vm.SaveToElement(routeElement);

        Assert.Equal("Upravená cesta", routeElement.RouteName);
        Assert.True(routeElement.RequestYellow);
        Assert.Equal(90, routeElement.MaxSpeed);
        Assert.Equal(45, routeElement.LimitedSpeed);
        Assert.Equal("route-B", routeElement.SelectedRouteDefinitionId);
        Assert.Contains(sensor1Id, routeElement.IndicatorIds);
        Assert.Contains(sensor2Id, routeElement.IndicatorIds);
    }

    [Fact]
    public void SaveCommand_WhenRouteLoaded_PersistsToBoundRouteElement()
    {
        var settings = CreateSettingsWithProject();
        settings.CurrentProject!.Layout.Routes.Add(new RouteDefinition { Id = "route-A", Name = "A" });

        var layoutVm = CreateLayoutVm(settings);
        var (sensor1Id, _) = AddBlockWithTwoIndicators(layoutVm);

        var routeElement = new RouteElement
        {
            RouteName = "Pôvodná",
            SelectedRouteDefinitionId = "route-A"
        };

        var vm = new RouteEditorViewModel(layoutVm, settings);
        vm.LoadFromElement(routeElement);

        vm.RouteName = "Nová";
        vm.RequestYellow = true;
        vm.MaxSpeed = 88;
        vm.LimitedSpeed = 44;
        vm.SelectSensorsByIds(new[] { sensor1Id });

        vm.SaveCommand.Execute(null);

        Assert.Equal("Nová", routeElement.RouteName);
        Assert.True(routeElement.RequestYellow);
        Assert.Equal(88, routeElement.MaxSpeed);
        Assert.Equal(44, routeElement.LimitedSpeed);
        Assert.Single(routeElement.IndicatorIds);
        Assert.Equal(sensor1Id, routeElement.IndicatorIds[0]);
    }

    [Fact]
    public void SaveToElement_WithoutSelection_ClearsRouteAndIndicators()
    {
        var settings = CreateSettingsWithProject();
        settings.CurrentProject!.Layout.Routes.Add(new RouteDefinition { Id = "route-A", Name = "A" });

        var layoutVm = CreateLayoutVm(settings);
        var (sensor1Id, _) = AddBlockWithTwoIndicators(layoutVm);

        var routeElement = new RouteElement
        {
            SelectedRouteDefinitionId = "route-A",
            IndicatorIds = { sensor1Id }
        };

        var vm = new RouteEditorViewModel(layoutVm, settings);
        vm.SelectRouteDefinitionById(null);
        vm.SelectSensorsByIds(Array.Empty<string>());

        vm.SaveToElement(routeElement);

        Assert.Null(routeElement.SelectedRouteDefinitionId);
        Assert.Empty(routeElement.IndicatorIds);
    }

    private static SettingsManager CreateSettingsWithProject()
    {
        var settings = new SettingsManager();
        settings.NewProject();
        return settings;
    }

    private static LayoutEditorViewModel CreateLayoutVm(SettingsManager settings)
    {
        return new LayoutEditorViewModel(settings);
    }

    private static (string Sensor1Id, string Sensor2Id) AddBlockWithTwoIndicators(LayoutEditorViewModel layoutVm)
    {
        var indicator1 = new BlockIndicator
        {
            Id = Guid.NewGuid(),
            Name = "Indikátor 1",
            Type = BlockIndicatorType.Contact
        };

        var indicator2 = new BlockIndicator
        {
            Id = Guid.NewGuid(),
            Name = "Indikátor 2",
            Type = BlockIndicatorType.Flagman
        };

        var block = new BlockElement { MarkerKey = "Block" };
        block.Indicators.Add(indicator1);
        block.Indicators.Add(indicator2);

        layoutVm.Elements.Add(block);

        return (indicator1.Id.ToString(), indicator2.Id.ToString());
    }
}

