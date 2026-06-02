using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TrackFlow.Models.Layout;
using TrackFlow.Views.Operation;
using Xunit;

namespace TrackFlow.Tests;

public class OperationViewInteractionMarkupTests
{
    [Fact]
    public void RouteActivationOverlay_MaVypnutyHitTest()
    {
        var xaml = File.ReadAllText(GetWorkspaceFilePath("Views", "Operation", "OperationView.axaml"));

        Assert.Contains("Name=\"RouteActivationMessageOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"False\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationView_NeobsahujeDocasneMultiTagUiDiagnostiky()
    {
        var codeBehind = File.ReadAllText(GetWorkspaceFilePath("Views", "Operation", "OperationView.axaml.cs"));

        Assert.DoesNotContain("[MULTI][UI]", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("[MULTI][UI-REFRESH]", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("[MULTI][KURZOR]", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockRenderer_NekresliRezervaciuLenZoStarehoReservedLocoId()
    {
        var block = new BlockElement
        {
            Id = "blk_a",
            MarkerKey = "Block",
            ReservedLocoId = "loco_demo_1",
            IsShadowSet = false,
            IsOccupied = false
        };

        var method = typeof(OperationView).GetMethod("ResolveRenderableBlockLocoId", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var renderLocoId = method.Invoke(null, new object[] { block });

        Assert.Null(renderLocoId);
    }

    [Fact]
    public void BlockRenderer_NekresliVlakLenZoStarehoAssignedLocoIdAkBlokNieJeObsadeny()
    {
        var block = new BlockElement
        {
            Id = "blk_x",
            MarkerKey = "Block",
            AssignedLocoId = "loco_demo_1",
            IsOccupied = false,
            IsShadowSet = false
        };

        var method = typeof(OperationView).GetMethod("ResolveRenderableBlockLocoId", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var renderLocoId = method.Invoke(null, new object[] { block });

        Assert.Null(renderLocoId);
    }

    [Fact]
    public void OperationView_DragDropHandlerMaExceptionReportingPreAsyncVoid()
    {
        var codeBehind = File.ReadAllText(GetWorkspaceFilePath("Views", "Operation", "OperationView.axaml.cs"));

        Assert.Contains("Program.ReportUnhandledException(\"OperationView.OnCanvasLocoDrop\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TrackFlowDoctorService.Instance.Diagnose(", codeBehind, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutEditor_VlastnostiDialgovMajuExceptionReportingPreAsyncVoid()
    {
        var codeBehind = File.ReadAllText(GetWorkspaceFilePath("Views", "Editor", "LayoutEditorView.axaml.cs"));

        Assert.Contains("Program.ReportUnhandledException(\"LayoutEditorView.OnBlockPropertiesRequested\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"LayoutEditorView.OnTurnoutPropertiesRequested\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"LayoutEditorView.OnSignalPropertiesRequested\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"LayoutEditorView.OnRoutePropertiesRequested\"", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"LayoutEditorView.OnTextPropertiesRequested\"", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationView_PriZmeneDataContextuOdpojiStaryViewModelASvojeSubskripcie()
    {
        var codeBehind = File.ReadAllText(GetWorkspaceFilePath("Views", "Operation", "OperationView.axaml.cs"));

        Assert.Contains("private OperationViewModel? _vmCurrent;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DetachFromVm();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_vmCurrent.LayoutRefreshRequested -= RefreshLayout;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_vmCurrent.Locomotives.CollectionChanged -= OnLocomotivesCollectionChanged;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("loco.AttachedWagons.CollectionChanged -= OnLocoWagonsChanged;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("loco.PropertyChanged -= OnLocoPropertyChanged;", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationView_PriDetachnutiZVisualTreeSpraviCleanupViewModelu()
    {
        var codeBehind = File.ReadAllText(GetWorkspaceFilePath("Views", "Operation", "OperationView.axaml.cs"));

        Assert.Contains("DetachedFromVisualTree += OnDetachedFromVisualTree;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("vm.Locomotives.CollectionChanged += OnLocomotivesCollectionChanged;", codeBehind, StringComparison.Ordinal);
    }

    private static string GetWorkspaceFilePath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "TrackFlow.sln")))
            directory = directory.Parent;

        if (directory == null)
            throw new InvalidOperationException("Nepodarilo sa nájsť koreň workspace TrackFlow.");

        return Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
    }
}


