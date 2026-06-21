using System;
using System.IO;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public sealed class ProjectStoreTrainSetTests
{
    [Fact]
    public void Load_PreservesRootLevelTrainSets()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TrackFlowTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var projectPath = Path.Combine(tempDir, "project.trackflow.json");

        try
        {
            File.WriteAllText(projectPath, """
            {
              "SchemaVersion": 3,
              "IsDirty": false,
              "Settings": {
                "SchemaVersion": 3
              },
              "Locomotives": [
                {
                  "Id": "0d16d38559ae44ccae20a79d55bce5ac",
                  "Name": "Loco",
                  "Address": 3,
                  "Description": "",
                  "IconName": "",
                  "Functions": []
                }
              ],
              "Wagons": [
                {
                  "Code": "bea6d0a573df45969de1e56cdc4b37b2",
                  "Name": "Wagon"
                }
              ],
              "TrainSets": [
                {
                  "LocomotiveCode": "0d16d38559ae44ccae20a79d55bce5ac",
                  "WagonCodes": [
                    "bea6d0a573df45969de1e56cdc4b37b2"
                  ]
                }
              ],
              "Layout": {
                "SchemaVersion": 3,
                "Elements": [],
                "Routes": []
              },
              "CabAssignments": []
            }
            """);

            var store = new ProjectStore();
            var project = store.Load(projectPath);

            var trainSet = Assert.Single(project.TrainSets);
            Assert.Equal("0d16d38559ae44ccae20a79d55bce5ac", trainSet.LocomotiveCode);
            Assert.Collection(trainSet.WagonCodes,
                code => Assert.Equal("bea6d0a573df45969de1e56cdc4b37b2", code));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // ignore cleanup failures in test temp directory
            }
        }
    }
}

