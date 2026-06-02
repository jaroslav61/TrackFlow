using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class RuntimeStateRegistryFrontierTests
{
    [Fact]
    public void PublishTraversalFrontier_ValidSnapshot_PublishesStateWithVersionOne()
    {
        TrackFlowDoctorService.Instance.Events.Clear();
        var registry = CreateRegistry();
        var snapshot = CreateSnapshot(
            routeId: "r1",
            traversalBlockIds: new[] { "blk_a", "blk_b", "blk_c" },
            currentTraversalIndex: 1,
            currentBlockId: "blk_b",
            leadTraversalIndex: 1,
            frontierBlockIds: new[] { "blk_b", "blk_c" },
            frontierPathElementIds: new[] { "seg_bc" },
            publisherKindName: "TraversalAdvance");

        var published = (bool)registry.GetType().GetMethod("PublishTraversalFrontier")!.Invoke(registry, new[] { snapshot })!;
        Assert.True(published);

        var state = registry.GetType().GetMethod("GetRouteFrontierState")!.Invoke(registry, new object?[] { "r1" });
        Assert.NotNull(state);
        Assert.Equal("r1", GetProperty<string>(state, "RouteId"));
        Assert.Equal("blk_b", GetProperty<string>(state, "CurrentBlockId"));
        Assert.Equal(1, GetProperty<long>(state, "Version"));
        Assert.True(GetProperty<DateTime>(state, "UpdatedAtUtc") > DateTime.MinValue);
        Assert.Equal(new[] { "blk_b", "blk_c" }, GetStringList(state, "FrontierBlockIds"));
    }

    [Fact]
    public void PublishTraversalFrontier_SecondPublish_IncrementsVersionAndReplacesSnapshotAtomically()
    {
        var registry = CreateRegistry();

        var first = CreateSnapshot(
            routeId: "r2",
            traversalBlockIds: new[] { "blk_a", "blk_b", "blk_c" },
            currentTraversalIndex: 0,
            currentBlockId: "blk_a",
            leadTraversalIndex: 0,
            frontierBlockIds: new[] { "blk_a", "blk_b" },
            frontierPathElementIds: new[] { "seg_ab" },
            publisherKindName: "Initialize");
        var second = CreateSnapshot(
            routeId: "r2",
            traversalBlockIds: new[] { "blk_a", "blk_b", "blk_c" },
            currentTraversalIndex: 1,
            currentBlockId: "blk_b",
            leadTraversalIndex: 1,
            frontierBlockIds: new[] { "blk_b", "blk_c" },
            frontierPathElementIds: new[] { "seg_bc" },
            publisherKindName: "TraversalAdvance");

        Assert.True((bool)registry.GetType().GetMethod("PublishTraversalFrontier")!.Invoke(registry, new[] { first })!);
        Assert.True((bool)registry.GetType().GetMethod("PublishTraversalFrontier")!.Invoke(registry, new[] { second })!);

        var state = registry.GetType().GetMethod("GetRouteFrontierState")!.Invoke(registry, new object?[] { "r2" });
        Assert.NotNull(state);
        Assert.Equal(2, GetProperty<long>(state, "Version"));
        Assert.Equal("blk_b", GetProperty<string>(state, "CurrentBlockId"));
        Assert.Equal(new[] { "blk_b", "blk_c" }, GetStringList(state, "FrontierBlockIds"));
        Assert.DoesNotContain("blk_a", GetStringList(state, "FrontierBlockIds"));
    }

    [Fact]
    public void ValidateFrontierSnapshot_ValidSnapshot_ReturnsNoErrors()
    {
        var registry = CreateRegistry();
        var valid = CreateSnapshot(
            routeId: "r3",
            traversalBlockIds: new[] { "blk_a", "blk_b" },
            currentTraversalIndex: 0,
            currentBlockId: "blk_a",
            leadTraversalIndex: 0,
            frontierBlockIds: new[] { "blk_a", "blk_b" },
            frontierPathElementIds: new[] { "seg_ab" },
            publisherKindName: "WaitEnter",
            isWaiting: true,
            waitingBlockId: "blk_b",
            waitingReason: "blocked");

        var state = CreateFrontierStateFromSnapshot(valid);
        var errors = (System.Collections.IEnumerable)registry.GetType().GetMethod("ValidateFrontierSnapshot")!.Invoke(registry, new[] { state })!;
        Assert.Empty(errors.Cast<object>());
    }

    [Fact]
    public void ClearRouteFrontier_RemovesPublishedState()
    {
        var registry = CreateRegistry();
        var snapshot = CreateSnapshot(
            routeId: "r4",
            traversalBlockIds: new[] { "blk_a", "blk_b" },
            currentTraversalIndex: 1,
            currentBlockId: "blk_b",
            leadTraversalIndex: 1,
            frontierBlockIds: new[] { "blk_b" },
            frontierPathElementIds: Array.Empty<string>(),
            publisherKindName: "TraversalWindowRefresh");

        Assert.True((bool)registry.GetType().GetMethod("PublishTraversalFrontier")!.Invoke(registry, new[] { snapshot })!);
        Assert.True((bool)registry.GetType().GetMethod("ClearRouteFrontier")!.Invoke(registry, new object?[] { "r4" })!);
        Assert.Null(registry.GetType().GetMethod("GetRouteFrontierState")!.Invoke(registry, new object?[] { "r4" }));
    }

    private static object CreateRegistry()
    {
        var registryType = Type.GetType("TrackFlow.Runtime.RuntimeStateRegistry, TrackFlow", throwOnError: true)!;
        return Activator.CreateInstance(registryType, nonPublic: true)!;
    }

    private static object CreateSnapshot(
        string routeId,
        IReadOnlyList<string> traversalBlockIds,
        int currentTraversalIndex,
        string currentBlockId,
        int leadTraversalIndex,
        IReadOnlyList<string> frontierBlockIds,
        IReadOnlyList<string> frontierPathElementIds,
        string publisherKindName,
        bool isWaiting = false,
        string? waitingBlockId = null,
        string? waitingReason = null)
    {
        var snapshotType = Type.GetType("TrackFlow.Runtime.TraversalFrontierSnapshot, TrackFlow", throwOnError: true)!;
        var publisherKindType = Type.GetType("TrackFlow.Runtime.RouteFrontierPublisherKind, TrackFlow", throwOnError: true)!;
        var snapshot = Activator.CreateInstance(snapshotType, nonPublic: true)!;

        snapshotType.GetProperty("RouteId")!.SetValue(snapshot, routeId);
        snapshotType.GetProperty("TraversalBlockIds")!.SetValue(snapshot, traversalBlockIds);
        snapshotType.GetProperty("CurrentTraversalIndex")!.SetValue(snapshot, currentTraversalIndex);
        snapshotType.GetProperty("CurrentBlockId")!.SetValue(snapshot, currentBlockId);
        snapshotType.GetProperty("LeadTraversalIndex")!.SetValue(snapshot, leadTraversalIndex);
        snapshotType.GetProperty("FrontierBlockIds")!.SetValue(snapshot, frontierBlockIds);
        snapshotType.GetProperty("FrontierPathElementIds")!.SetValue(snapshot, frontierPathElementIds);
        snapshotType.GetProperty("IsWaiting")!.SetValue(snapshot, isWaiting);
        snapshotType.GetProperty("WaitingBlockId")!.SetValue(snapshot, waitingBlockId);
        snapshotType.GetProperty("WaitingReason")!.SetValue(snapshot, waitingReason);
        snapshotType.GetProperty("ProceedCorridorBlockIds")!.SetValue(snapshot, Array.Empty<string>());
        snapshotType.GetProperty("PublisherKind")!.SetValue(snapshot, Enum.Parse(publisherKindType, publisherKindName));
        return snapshot;
    }

    private static object CreateFrontierStateFromSnapshot(object snapshot)
    {
        var snapshotType = snapshot.GetType();
        var stateType = Type.GetType("TrackFlow.Runtime.RouteFrontierState, TrackFlow", throwOnError: true)!;
        var state = Activator.CreateInstance(stateType, nonPublic: true)!;

        foreach (var propertyName in new[]
                 {
                     "RouteId",
                     "TraversalBlockIds",
                     "CurrentTraversalIndex",
                     "CurrentBlockId",
                     "LeadTraversalIndex",
                     "FrontierBlockIds",
                     "FrontierPathElementIds",
                     "IsWaiting",
                     "WaitingBlockId",
                     "WaitingReason",
                     "TailClearSourceBlockId",
                     "TailClearTargetBlockId",
                     "TailClearTriggered",
                     "BoundaryEntryTriggered",
                     "ProceedCorridorBlockIds",
                     "PublisherKind"
                 })
        {
            var value = snapshotType.GetProperty(propertyName)!.GetValue(snapshot);
            stateType.GetProperty(propertyName)!.SetValue(state, value);
        }

        return state;
    }

    private static T GetProperty<T>(object instance, string propertyName)
        => (T)instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!.GetValue(instance)!;

    private static IReadOnlyList<string> GetStringList(object instance, string propertyName)
        => ((System.Collections.IEnumerable)instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!.GetValue(instance)!)
            .Cast<object>()
            .Select(value => value.ToString() ?? string.Empty)
            .ToList();
}



