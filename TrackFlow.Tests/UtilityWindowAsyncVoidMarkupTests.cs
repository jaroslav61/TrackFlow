using System;
using System.IO;
using System.Linq;
using Xunit;

namespace TrackFlow.Tests;

public class UtilityWindowAsyncVoidMarkupTests
{
    [Fact]
    public void DoctorWindow_AsyncVoidHandleryMajuExceptionReporting()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "DoctorWindow.axaml.cs"));

        Assert.Contains("Program.ReportUnhandledException(\"DoctorWindow.OnSaveLogClick\"", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"DoctorWindow.OnSaveFilteredLogClick\"", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"DoctorWindow.OnCopyFilteredLogClick\"", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"DoctorWindow.OnCopyFullLogClick\"", code, StringComparison.Ordinal);
        Assert.Contains("_doctorService.Diagnose(\"Doktor\", $\"Uloženie celého logu zlyhalo:", code, StringComparison.Ordinal);
        Assert.Contains("_doctorService.Diagnose(\"Doktor\", $\"Uloženie filtrovaného výpisu zlyhalo:", code, StringComparison.Ordinal);
        Assert.Contains("_doctorService.Diagnose(\"Doktor\", $\"Kopírovanie filtrovaného výpisu zlyhalo:", code, StringComparison.Ordinal);
        Assert.Contains("_doctorService.Diagnose(\"Doktor\", $\"Kopírovanie celého logu zlyhalo:", code, StringComparison.Ordinal);
    }

    [Fact]
    public void FileBackstageView_LoadedHandlerMaExceptionReportingPreAsyncVoid()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Backstage", "FileBackstageView.axaml.cs"));

        Assert.Contains("Program.ReportUnhandledException(\"FileBackstageView.OnViewLoaded\"", code, StringComparison.Ordinal);
        Assert.Contains("TrackFlowDoctorService.Instance.Diagnose(\"Súbor\", $\"Inicializácia Recent Files backstage view zlyhala:", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadDecoderValuesWindow_CompatOpenedHandlerMaExceptionReportingPreAsyncVoid()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Dialogs", "ReadDecoderValuesWindow.axaml.cs"));

        Assert.Contains("Program.ReportUnhandledException(\"ReadDecoderValuesWindow.OnOpenedStartCompatReading\"", code, StringComparison.Ordinal);
        Assert.Contains("TrackFlowDoctorService.Instance.Diagnose(\"DCC\", $\"Compat štart čítania CV zlyhal:", code, StringComparison.Ordinal);
        Assert.Contains("Error = ex;", code, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post(Close);", code, StringComparison.Ordinal);
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


