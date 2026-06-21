using System;
using System.Linq;
using System.Reflection;
using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class TrackFlowDoctorServiceTests
{
    [Fact]
    public void ExportCurrentLogText_ZachovaChronologickePoradieATabSeparatedFormat()
    {
        var doctor = TrackFlowDoctorService.Instance;
        doctor.Events.Clear();

        doctor.Diagnose("Prevádzka", "[MULTI][CAKANIE] prvý", DiagnosticLevel.Warning);
        doctor.Diagnose("Prevádzka", "[MULTI][ARBITRAZ] druhý");

        var export = doctor.ExportCurrentLogText();
        var lines = export.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Contains("\tVarovanie\tPrevádzka\t[MULTI][CAKANIE] prvý", lines[0]);
        Assert.Contains("\tInformácia\tPrevádzka\t[MULTI][ARBITRAZ] druhý", lines[1]);
    }

    [Fact]
    public void InsertMarker_PridaMarkerEventAExportEscapujeNovyRiadok()
    {
        var doctor = TrackFlowDoctorService.Instance;
        doctor.Events.Clear();

        doctor.InsertMarker();

        var marker = Assert.Single(doctor.Events);
        Assert.Equal("Marker", marker.Source);
        Assert.Contains("========== MARKER ==========" , marker.Message);
        Assert.Contains("čas=[", marker.Message);

        var exportLine = doctor.ExportCurrentLogText().Trim();
        Assert.Contains("\tInformácia\tMarker\t========== MARKER ==========\\nčas=[", exportLine);
    }

    [Fact]
    public void GetEventsChronologicalSnapshot_VratiNajstarsiZaznamAkoPrvy()
    {
        var doctor = TrackFlowDoctorService.Instance;
        doctor.Events.Clear();

        doctor.Diagnose("Prevádzka", "[MULTI][BLOK] starší");
        doctor.Diagnose("Prevádzka", "[MULTI][PAT] novší", DiagnosticLevel.Warning);

        var snapshot = doctor.GetEventsChronologicalSnapshot();

        Assert.Equal(2, snapshot.Count);
        Assert.Equal("[MULTI][BLOK] starší", snapshot[0].Message);
        Assert.Equal("[MULTI][PAT] novší", snapshot[1].Message);
    }

    [Fact]
    public void ExportEventsText_UmozniVyexportovatLenFiltrovanuPodmnozinuUdalosti()
    {
        var doctor = TrackFlowDoctorService.Instance;
        doctor.Events.Clear();

        doctor.Diagnose("Prevádzka", "[MULTI][CAKANIE] čakanie", DiagnosticLevel.Warning);
        doctor.Diagnose("Prevádzka", "[MULTI][PAT] patová situácia", DiagnosticLevel.Warning);
        doctor.Diagnose("Prevádzka", "[MULTI][NAVESTIDLO] voľno");

        var filtered = doctor.GetEventsChronologicalSnapshot()
            .Where(entry => entry.Message.Contains("[MULTI][CAKANIE]", StringComparison.Ordinal)
                         || entry.Message.Contains("[MULTI][PAT]", StringComparison.Ordinal));

        var export = doctor.ExportEventsText(filtered);
        var lines = export.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Contains("[MULTI][CAKANIE] čakanie", export);
        Assert.Contains("[MULTI][PAT] patová situácia", export);
        Assert.DoesNotContain("[MULTI][NAVESTIDLO] voľno", export);
    }

    [Fact]
    public void DiagnosticEvent_DisplayVlastnosti_PreloziaZdrojUrovenAStrukturovanuSpravu()
    {
        var entry = new DiagnosticEvent
        {
            Source = "RouteActivation",
            Level = DiagnosticLevel.Warning,
            Message = "[MULTI][VYHYBKA] výhybka=[V1], stav=[prestavená], požadovaný=[do odbočky], žiada=[Stanica A → Stanica B], vlastník=[Stanica A → Stanica B]"
        };

        Assert.Equal("Aktivácia cesty", entry.DisplaySource);
        Assert.Equal("Varovanie", entry.DisplayLevelText);
        Assert.Equal("Výhybka V1 prestavená na smer do odbočky pre cestu Stanica A → Stanica B", entry.MessageText);
    }

    [Fact]
    public void DiagnosticEvent_MessageText_PreSignalPoPrejazdeZobraziJasnySlovenskyText()
    {
        var entry = new DiagnosticEvent
        {
            Source = "Prevádzka",
            Message = "[MULTI][NAVESTIDLO] cesta=[Stanica A → Stanica B], návestidlo=[Na6], stav=[stoj-po-prejazde], aspekt=[Stoj], vlak=[R 754]"
        };

        Assert.Equal("Návestidlo Na6 zhodené na STOJ po prejazde úseku (cesta Stanica A → Stanica B)", entry.MessageText);
    }

    [Fact]
    public void DiagnosticEvent_MessageText_PreUvolnenuVyhybkuZobraziAjJejAktualnuPolohu()
    {
        var entry = new DiagnosticEvent
        {
            Source = "Prevádzka",
            Message = "[MULTI][VYHYBKA] výhybka=[V1], stav=[uvoľnená], požadovaný=[Rovno], žiada=[r_internal_001], vlastník=[r_internal_001]"
        };

        Assert.Equal("Výhybka V1 uvoľnená, aktuálna poloha Rovno", entry.MessageText);
    }

    [Fact]
    public void DiagnosticEvent_MessageText_PreTechnickeIdCestySkryjeSuroveRouteId()
    {
        var entry = new DiagnosticEvent
        {
            Source = "Prevádzka",
            Message = "[MULTI][NAVESTIDLO] cesta=[r_internal_001], návestidlo=[Na6], stav=[stoj-po-prejazde], aspekt=[Stoj], vlak=[R 754]"
        };

        Assert.Equal("Návestidlo Na6 zhodené na STOJ po prejazde úseku (cesta neznáma cesta)", entry.MessageText);
    }

    [Fact]
    public void Diagnose_BezprostrednyDuplicitnyZaznamPotlaci()
    {
        var doctor = TrackFlowDoctorService.Instance;
        doctor.Events.Clear();

        doctor.Diagnose("Prevádzka", "Duplicitná skúška", DiagnosticLevel.Info);
        doctor.Diagnose("Prevádzka", "Duplicitná skúška", DiagnosticLevel.Info);

        var entry = Assert.Single(doctor.Events);
        Assert.Equal("Duplicitná skúška", entry.Message);
    }

    [Fact]
    public void DiagnosticEvent_MessageText_PreloziDccAnglicizmyDoSlovenciny()
    {
        var entry = new DiagnosticEvent
        {
            Source = "DCC",
            Message = "⚠️ Auto-reconnect k centrále Z21 zlyhal (keepalive timeout)"
        };

        Assert.Equal("Automatické pripájanie k centrále Z21 zlyhal (vypršal dohľad spojenia)", entry.MessageText);
    }

    [Fact]
    public void TrackFlowDoctorFormatter_SkryjeInternuOrchestraciuVDoctorOkne()
    {
        Assert.False(ShouldDisplayInDoctorWindow("Prevádzka", "[MULTI][ORCHESTRACIA] cesta=[A → B], stav=[refresh-skip-unchanged], detail=[source=[timer]]"));
        Assert.True(ShouldDisplayInDoctorWindow("Cesta", "▶ Začiatok cesty: A → B (vlak Os 1234)"));
    }

    [Fact]
    public void TrackFlowDoctorFormatter_SkryjeUiHighlightDiagnostikuVDoctorOkne()
    {
        Assert.False(ShouldDisplayInDoctorWindow("Prevádzka", "[MULTI][UI-HIGHLIGHT] cesta=[r_internal_001], stav=[highlight-source], detail=[source=[runtime-window]]"));
    }

    [Fact]
    public void TrackFlowDoctorFormatter_SkryjeTechnickuSyntetickuDiagnostikuNavestidla()
    {
        Assert.False(ShouldDisplayInDoctorWindow("Návestidlo", "Syntéza aspektu pre Na6: základná návesť=Výstraha, ďalšia návesť=Stoj, Výsledok=Výstraha"));
        Assert.True(ShouldDisplayInDoctorWindow("Návestidlo", "Nahadzujem návestidlo Na6 na Výstraha"));
    }

    private static bool ShouldDisplayInDoctorWindow(string source, string message)
    {
        var formatterType = typeof(TrackFlowDoctorService).Assembly.GetType("TrackFlow.Services.TrackFlowDoctorFormatter");
        Assert.NotNull(formatterType);

        var method = formatterType!.GetMethod(
            "ShouldDisplayInDoctorWindow",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string), typeof(string) },
            modifiers: null);

        Assert.NotNull(method);
        return Assert.IsType<bool>(method!.Invoke(null, new object[] { source, message }));
    }
}




