using System;
using System.IO;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public sealed class AppSettingsStorePathTests
{
    [Fact]
    public void Constructor_WithRelativePath_AnchorsToExecutableBaseDirectory()
    {
        var relativePath = Path.Combine("test-output", $"settings-{Guid.NewGuid():N}.json");
        var expected = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);

        var store = new AppSettingsStore(relativePath);

        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(store.FilePath));
    }

    [Fact]
    public void SaveApp_CreatesSettingsFile_WhenMissing()
    {
        var relativePath = Path.Combine("test-output", $"settings-{Guid.NewGuid():N}.json");
        var store = new AppSettingsStore(relativePath);

        try
        {
            if (File.Exists(store.FilePath))
                File.Delete(store.FilePath);

            var manager = new SettingsManager(appStore: store);
            manager.LoadApp();

            Assert.True(manager.SaveApp());
            Assert.True(File.Exists(store.FilePath));
        }
        finally
        {
            try
            {
                if (File.Exists(store.FilePath))
                    File.Delete(store.FilePath);

                var dir = Path.GetDirectoryName(store.FilePath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                    Directory.Delete(dir);
            }
            catch
            {
                // best-effort cleanup for test artifacts
            }
        }
    }
}

