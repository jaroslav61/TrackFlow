using System;
using System.IO;
using System.Linq;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class SampleProjectsTests
{
    [Theory]
    [InlineData("SharedBlockWaitSample.trackflow.json")]
    [InlineData("SharedTurnoutWaitSample.trackflow.json")]
    [InlineData("ParallelIndependentRoutesSample.trackflow.json")]
    [InlineData("DeadlockPotentialSample.trackflow.json")]
    [InlineData("TailClearReleaseSample.trackflow.json")]
    public void MultiRouteSamples_LoadThroughProjectStore_AndContainRequiredRuntimeData(string fileName)
    {
        var root = FindRepositoryRoot();
        var samplePath = Path.Combine(root.FullName, "Samples", fileName);

        Assert.True(File.Exists(samplePath), $"Sample project '{fileName}' neexistuje.");

        var store = new ProjectStore();
        var project = store.Load(samplePath);

        Assert.NotNull(project);
        Assert.NotNull(project.Layout);
        Assert.True(project.Locomotives.Count >= 2, $"Sample '{fileName}' musí obsahovať aspoň 2 lokomotívy.");
        Assert.True(project.Layout.Routes.Count >= 2, $"Sample '{fileName}' musí obsahovať aspoň 2 routes.");

        var namedBlocks = project.Layout.Elements.OfType<BlockElement>()
            .Count(b => !string.IsNullOrWhiteSpace(b.Label));
        var namedSignals = project.Layout.Elements.OfType<SignalElement>()
            .Count(s => !string.IsNullOrWhiteSpace(s.Label));

        Assert.True(namedBlocks >= 2, $"Sample '{fileName}' musí obsahovať pomenované bloky.");
        Assert.True(namedSignals >= 1, $"Sample '{fileName}' musí obsahovať pomenované návestidlá.");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TrackFlow.sln"))
                && Directory.Exists(Path.Combine(current.FullName, "Samples")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Nepodarilo sa nájsť koreň repozitára TrackFlow.");
    }
}

