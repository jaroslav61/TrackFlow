using System;
using System.IO;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public sealed class VehicleIconLoaderTests
{
    private const string TinyPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR42mNk+A8AAwUBAO+X3i8AAAAASUVORK5CYII=";

    [Fact(Skip = "Requires Avalonia imaging backend initialization in test host.")]
    public void TryLoadBitmap_ResolvesRegisteredIcon_ByFileNameFromRelativeLikeValue()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"trackflow-icon-{Guid.NewGuid():N}.png");
        var iconFileName = Path.GetFileName(tempFile);

        try
        {
            File.WriteAllBytes(tempFile, Convert.FromBase64String(TinyPngBase64));
            IconRegistry.Register(iconFileName, tempFile);

            using var bitmap = VehicleIconLoader.TryLoadBitmap($"Assets\\LocoIcons\\{iconFileName}");
            Assert.NotNull(bitmap);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact(Skip = "Requires Avalonia imaging backend initialization in test host.")]
    public void TryLoadBitmap_ResolvesRegisteredIcon_WhenExtensionIsMissing()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"trackflow-icon-{Guid.NewGuid():N}.png");
        var iconBaseName = Path.GetFileNameWithoutExtension(tempFile);
        var iconFileName = Path.GetFileName(tempFile);

        try
        {
            File.WriteAllBytes(tempFile, Convert.FromBase64String(TinyPngBase64));
            IconRegistry.Register(iconFileName, tempFile);

            using var bitmap = VehicleIconLoader.TryLoadBitmap(iconBaseName);
            Assert.NotNull(bitmap);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}



