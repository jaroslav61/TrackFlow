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
        Assert.Equal(0, block.lengthMm);
    }

    [Fact]
    public void BlockProperties_Save_Writes_Length_To_Model()
    {
        var block = new BlockElement { MarkerKey = "Block", Label = "B1" };
        var vm = new BlockPropertiesViewModel(block);

        vm.LengthMm = 1230;
        vm.SaveCommand.Execute(null);

        Assert.Equal(1230, block.lengthMm);
    }

    [Fact]
    public void Save_And_Reload_Project_Preserves_Block_LengthMm()
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
                lengthMm = 3210,
                BlockLengthCells = 6,
            });

            Assert.True(mgr.SaveProjectAs(projectPath));

            var reloaded = new SettingsManager();
            reloaded.LoadApp();
            reloaded.OpenProject(projectPath);

            var loadedBlock = reloaded.CurrentProject!.Layout.Elements.OfType<BlockElement>().Single();
            Assert.Equal(3210, loadedBlock.lengthMm);
        }
        finally
        {
            if (File.Exists(projectPath))
                File.Delete(projectPath);
        }
    }
}
