using System;
using System.IO;
using System.Linq;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.ViewModels.Editor;
using Xunit;

namespace TrackFlow.Tests;

public sealed class BlockLengthPersistenceTests
{
    [Fact]
    public void New_Block_Default_Length_Is_Zero()
    {
        var block = new BlockElement();
        Assert.Equal(0, block.LengthCm);
    }

    [Fact]
    public void BlockProperties_Save_Writes_Length_To_Model()
    {
        var block = new BlockElement { MarkerKey = "Block", Label = "B1" };
        var vm = new BlockPropertiesViewModel(block);

        vm.LengthCm = 123;
        vm.SaveCommand.Execute(null);

        Assert.Equal(123, block.LengthCm);
    }

    [Fact]
    public void Save_And_Reload_Project_Preserves_Block_LengthCm()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), $"trackflow-project-{Guid.NewGuid():N}.json");

        try
        {
            var mgr = new SettingsManager();
            mgr.LoadApp();
            mgr.NewProject();

            mgr.CurrentProject!.Layout.Elements.Add(new BlockElement
            {
                MarkerKey = "Block",
                Label = "Blok 1",
                X = 100,
                Y = 200,
                LengthCm = 321,
                BlockLengthCells = 6,
            });

            Assert.True(mgr.SaveProjectAs(projectPath));

            var reloaded = new SettingsManager();
            reloaded.LoadApp();
            reloaded.OpenProject(projectPath);

            var loadedBlock = reloaded.CurrentProject!.Layout.Elements.OfType<BlockElement>().Single();
            Assert.Equal(321, loadedBlock.LengthCm);
        }
        finally
        {
            if (File.Exists(projectPath))
                File.Delete(projectPath);
        }
    }
}

