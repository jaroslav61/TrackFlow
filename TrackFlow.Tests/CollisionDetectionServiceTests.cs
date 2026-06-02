using System.Collections.Generic;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class CollisionDetectionServiceTests
{
	private static BlockElement CreateBlock(int cx, int cy, int len = 1)
	{
		return new BlockElement
		{
			MarkerKey = "Block",
			X = cx * 24.0,
			Y = cy * 24.0,
			Rotation = 0,
			BlockLengthCells = len
		};
	}

	[Fact]
	public void EvaluateEntry_TargetObsadenyInouLokomotivou_JeBlocked()
	{
		var service = new CollisionDetectionService();
		var target = CreateBlock(0, 0);
		target.IsOccupied = true;
		target.AssignedLocoId = "999";

		var result = service.EvaluateEntry(new List<LayoutElement> { target }, target.Id, "754");

		Assert.False(result.IsSafe);
		Assert.Equal("target-block-occupied", result.Reason);
		Assert.Equal(target.Id, result.BlockingBlockId);
	}

	[Fact]
	public void EvaluateEntry_TargetObsadenyTouIstouLokomotivou_JeSafe()
	{
		var service = new CollisionDetectionService();
		var target = CreateBlock(0, 0);
		target.IsOccupied = true;
		target.AssignedLocoId = "754";

		var result = service.EvaluateEntry(new List<LayoutElement> { target }, target.Id, "754");

		Assert.True(result.IsSafe);
	}

	[Fact]
	public void EvaluateEntry_TargetRezervovanyInouLokomotivou_JeBlocked()
	{
		var service = new CollisionDetectionService();
		var target = CreateBlock(0, 0);
		target.ReservedLocoId = "999";

		var result = service.EvaluateEntry(new List<LayoutElement> { target }, target.Id, "754");

		Assert.False(result.IsSafe);
		Assert.Equal("target-block-reserved", result.Reason);
		Assert.Equal(target.Id, result.BlockingBlockId);
	}

	[Fact]
	public void EvaluateEntry_SusednyObsadenyBlokVzdialenost1_JeBlocked()
	{
		var service = new CollisionDetectionService();

		var blockA = CreateBlock(0, 0);
		var blockB = CreateBlock(2, 0);
		blockB.IsOccupied = true;
		blockB.AssignedLocoId = "other";

		var connector = new TrackSegmentElement
		{
			MarkerKey = "TrackSegment",
			X = 24.0,
			Y = 0,
			Rotation = 0
		};

		var elements = new List<LayoutElement> { blockA, connector, blockB };

		var result = service.EvaluateEntry(elements, blockA.Id, "754", safetyDistanceBlocks: 1);

		Assert.False(result.IsSafe);
		Assert.Equal("neighbor-block-occupied", result.Reason);
		Assert.Equal(blockB.Id, result.BlockingBlockId);
	}

	[Fact]
	public void EvaluateEntry_SafetyDistanceZero_NeriesiSusedov()
	{
		var service = new CollisionDetectionService();

		var blockA = CreateBlock(0, 0);
		var blockB = CreateBlock(2, 0);
		blockB.IsOccupied = true;
		blockB.AssignedLocoId = "other";

		var connector = new TrackSegmentElement
		{
			MarkerKey = "TrackSegment",
			X = 24.0,
			Y = 0,
			Rotation = 0
		};

		var elements = new List<LayoutElement> { blockA, connector, blockB };

		var result = service.EvaluateEntry(elements, blockA.Id, "754", safetyDistanceBlocks: 0);

		Assert.True(result.IsSafe);
	}

	[Fact]
	public void EvaluateEntry_RouteTopology_ParalelnyNepripojenyBlokNieJeNeighbor()
	{
		var service = new CollisionDetectionService();

		var blockA = CreateBlock(0, 0);
		blockA.Label = "B1";
		blockA.IsOccupied = true;
		blockA.AssignedLocoId = "754";

		var blockB = CreateBlock(5, 0);
		blockB.Label = "B5";

		var parallel = CreateBlock(0, 1);
		parallel.Label = "B1-spodny";
		parallel.IsOccupied = true;
		parallel.AssignedLocoId = "other";

		var route = new RouteDefinition
		{
			Id = "r_b1_b5",
			FromBlockId = blockA.Id,
			ToBlockId = blockB.Id,
			BlockIds = new List<string> { blockA.Id, blockB.Id }
		};

		var layout = new TrackLayout
		{
			Elements = new List<LayoutElement> { blockA, blockB, parallel },
			Routes = new List<RouteDefinition> { route }
		};

		var result = service.EvaluateEntry(layout, blockB.Id, "754", route, safetyDistanceBlocks: 1);

		Assert.True(result.IsSafe);
	}

	[Fact]
	public void EvaluateEntry_RouteTopology_SourceSideVetvaNieJeTargetSafetyNeighbor()
	{
		var service = new CollisionDetectionService();

		var blockA = CreateBlock(0, 0);
		blockA.Label = "B1";
		blockA.IsOccupied = true;
		blockA.AssignedLocoId = "754";

		var blockB = CreateBlock(5, 0);
		blockB.Label = "B5";

		var sourceBranch = CreateBlock(0, 1);
		sourceBranch.Label = "B3";
		sourceBranch.IsOccupied = true;
		sourceBranch.AssignedLocoId = "other";

		var mainRoute = new RouteDefinition
		{
			Id = "r_b1_b5",
			FromBlockId = blockA.Id,
			ToBlockId = blockB.Id,
			BlockIds = new List<string> { blockA.Id, blockB.Id }
		};
		var sourceBranchRoute = new RouteDefinition
		{
			Id = "r_b1_b3",
			FromBlockId = blockA.Id,
			ToBlockId = sourceBranch.Id,
			BlockIds = new List<string> { blockA.Id, sourceBranch.Id }
		};

		var layout = new TrackLayout
		{
			Elements = new List<LayoutElement> { blockA, blockB, sourceBranch },
			Routes = new List<RouteDefinition> { mainRoute, sourceBranchRoute }
		};

		var result = service.EvaluateEntry(layout, blockB.Id, "754", mainRoute, safetyDistanceBlocks: 1);

		Assert.True(result.IsSafe);
	}

	[Fact]
	public void EvaluateEntry_RouteTopology_GlobalnaTargetSideVetvaNieJeCandidateRouteNeighbor()
	{
		var service = new CollisionDetectionService();

		var blockA = CreateBlock(0, 0);
		blockA.Label = "B1";
		blockA.IsOccupied = true;
		blockA.AssignedLocoId = "754";

		var blockB = CreateBlock(5, 0);
		blockB.Label = "B5";

		var branch = CreateBlock(5, 1);
		branch.Label = "B2";
		branch.IsOccupied = true;
		branch.AssignedLocoId = "other";

		var mainRoute = new RouteDefinition
		{
			Id = "r_b1_b5",
			FromBlockId = blockA.Id,
			ToBlockId = blockB.Id,
			BlockIds = new List<string> { blockA.Id, blockB.Id }
		};
		var branchRoute = new RouteDefinition
		{
			Id = "r_b5_b2",
			FromBlockId = blockB.Id,
			ToBlockId = branch.Id,
			BlockIds = new List<string> { blockB.Id, branch.Id }
		};

		var layout = new TrackLayout
		{
			Elements = new List<LayoutElement> { blockA, blockB, branch },
			Routes = new List<RouteDefinition> { mainRoute, branchRoute }
		};

		var result = service.EvaluateEntry(layout, blockB.Id, "754", mainRoute, safetyDistanceBlocks: 1);

		Assert.True(result.IsSafe);
	}

	[Fact]
	public void EvaluateEntry_RouteTopology_CandidateRouteNeighborVBezpecnejVzdialenostiBlokuje()
	{
		var service = new CollisionDetectionService();

		var blockA = CreateBlock(0, 0);
		blockA.Label = "B1";
		blockA.IsOccupied = true;
		blockA.AssignedLocoId = "754";

		var blockB = CreateBlock(5, 0);
		blockB.Label = "B5";

		var blockC = CreateBlock(7, 0);
		blockC.Label = "B6";
		blockC.IsOccupied = true;
		blockC.AssignedLocoId = "other";

		var candidateRoute = new RouteDefinition
		{
			Id = "r_b1_b5_b6",
			FromBlockId = blockA.Id,
			ToBlockId = blockC.Id,
			BlockIds = new List<string> { blockA.Id, blockB.Id, blockC.Id }
		};

		var layout = new TrackLayout
		{
			Elements = new List<LayoutElement> { blockA, blockB, blockC },
			Routes = new List<RouteDefinition> { candidateRoute }
		};

		var result = service.EvaluateEntry(layout, blockB.Id, "754", candidateRoute, safetyDistanceBlocks: 1);

		Assert.False(result.IsSafe);
		Assert.Equal("neighbor-block-occupied", result.Reason);
		Assert.Equal(blockC.Id, result.BlockingBlockId);
	}
}

