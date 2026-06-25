using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using TrackFlow.Models;
using TrackFlow.Models.Calibration;
using TrackFlow.Models.Layout;
using TrackFlow.ViewModels.Library;
using Xunit;

namespace TrackFlow.Tests;

public class LocomotiveSpeedEditorMarkupTests
{
    [Fact]
    public void LocomotivesWindow_RychlostTabJeInlineABezSamostatnehoSpeedEditorView()
    {
        var xaml = File.ReadAllText(GetWorkspaceFilePath("Views", "Library", "LocomotivesWindow.axaml"));

        Assert.Contains("<TabItem Header=\"Rýchlosť\">", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Náhľad uloženého rýchlostného profilu\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Kalibrácia rýchlosti...\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OpenCalibrationWindow_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("speedCalibrationLaunchButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DataContext=\"{Binding SpeedEditor}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Kalibrácia rýchlosti lokomotívy je presunutá do samostatného okna.", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Graf slúži len na čítanie už uložených profilov.", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Kalibračná metóda", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Krivka rýchlostného profilu", xaml, StringComparison.Ordinal);
    }



    [Fact]
    public void LocomotivesWindow_RychlostneZalozkySuNaviazaneNaAktivnySmerVoViewModele()
    {
        var xaml = File.ReadAllText(GetWorkspaceFilePath("Views", "Library", "LocomotiveCalibrationWindow.axaml"));

        Assert.Contains("Content=\"Uložiť profil\" Command=\"{Binding SaveProfileCommand}\" IsEnabled=\"{Binding CanSaveActiveProfile}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Inicializovať profil\" Click=\"OnInitializeProfileClick\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Projekt obsahuje neuložené zmeny profilu\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsVisible=\"{Binding HasPendingProfileProjectChanges}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<TabItem Header=\"Analýza\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Porovnanie kriviek &amp; Štatistika", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<TabControl Classes=\"tc-tabs\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Namerané rýchlosti", xaml, StringComparison.Ordinal);
        Assert.Contains("Smer dopredu", xaml, StringComparison.Ordinal);
        Assert.Contains("Smer dozadu", xaml, StringComparison.Ordinal);
        Assert.Contains("Profil dopredu", xaml, StringComparison.Ordinal);
        Assert.Contains("Profil dozadu", xaml, StringComparison.Ordinal);
        Assert.Contains("Obidva profily", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LocomotiveCalibrationWindow_KalibracneComboBoxyPouzivajuSelectionTemplateSIkonou()
    {
        var xaml = File.ReadAllText(GetWorkspaceFilePath("Views", "Library", "LocomotiveCalibrationWindow.axaml"));

        Assert.Contains("Kalibračné bloky", xaml, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(xaml, "<ComboBox.ItemTemplate>"));
        Assert.Equal(3, CountOccurrences(xaml, "<ComboBox.SelectionBoxItemTemplate>"));
        Assert.True(CountOccurrences(xaml, "Source=\"{Binding IconImage}\"") >= 6);
    }


    [Fact]
    public void SharedDataGridStyle_NerezervujeMiestoPreSortIkonyVHlaveStlpca()
    {
        var xaml = File.ReadAllText(GetWorkspaceFilePath("Styles", "DataGrid.axaml"));

        Assert.Contains("<Style Selector=\"DataGridColumnHeader\">", xaml, StringComparison.Ordinal);
        Assert.Contains("Setter Property=\"Template\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ContentPresenter Name=\"PART_ContentPresenter\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Sort", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Glyph", xaml, StringComparison.Ordinal);
    }


    [Fact]
    public void LocomotivesWindow_RychlostnaTabulkaPouzivaTextoveStlpcePreKrokANameranuRychlostABunkyMajuOddeleneMriezky()
    {
        var xaml = File.ReadAllText(GetWorkspaceFilePath("Views", "Library", "LocomotiveCalibrationWindow.axaml"));

        Assert.Equal(1, CountOccurrences(xaml, "ItemsSource=\"{Binding ForwardSpeedProfileRows}\""));
        Assert.Equal(1, CountOccurrences(xaml, "ItemsSource=\"{Binding BackwardSpeedProfileRows}\""));
        Assert.Equal(2, CountOccurrences(xaml, "SelectedItem=\"{Binding SelectedSpeedProfileRow, Mode=TwoWay}\""));
        Assert.Equal(1, CountOccurrences(xaml, "<DataGridTextColumn Binding=\"{Binding FwdRawSpeedText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\" Width=\"89\" MinWidth=\"89\" CanUserSort=\"False\">"));
        Assert.Equal(1, CountOccurrences(xaml, "<DataGridTextColumn Binding=\"{Binding BwdRawSpeedText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\" Width=\"89\" MinWidth=\"89\" CanUserSort=\"False\">"));
        Assert.DoesNotContain("Binding=\"{Binding CalculatedSpeedText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Vypočítaná&#x0a;km/h\"", xaml, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(xaml, "Text=\"Smer dopredu\""));
        Assert.Equal(1, CountOccurrences(xaml, "Text=\"Smer dozadu\""));
        Assert.Equal(2, CountOccurrences(xaml, "RowHeight=\"26\""));
        Assert.Contains("<Border Grid.Column=\"0\" Classes=\"speedCard\" Padding=\"8\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Border Grid.Column=\"1\" Classes=\"speedCard\" Padding=\"8\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Grid.Row=\"1\" ColumnDefinitions=\"*,*\" ColumnSpacing=\"10\" VerticalAlignment=\"Stretch\">", xaml, StringComparison.Ordinal);
        Assert.Contains("TypeBadgeText", xaml, StringComparison.Ordinal);
        Assert.Contains("StatusSummary", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSeconds", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SpeedEditor_ExponujeSlovenskeMetodyAKalibracneIndikatoryZProjektu()
    {
        var viewModel = new LocomotiveSpeedEditorViewModel();

        Assert.Collection(
            viewModel.CalibrationMethodDisplayNames,
            option => Assert.Equal("Automatické meranie kompletného rýchlostného profilu (detektory obsadenia)", option),
            option => Assert.Equal("Automatické meranie kompletného rýchlostného profilu (momentové kontakty)", option),
            option => Assert.Equal("Automatické meranie jedného rýchlostného stupňa (detektory obsadenia)", option),
            option => Assert.Equal("Automatické meranie jedného rýchlostného stupňa (momentové kontakty)", option),
            option => Assert.Equal("Test kompenzácie brzdenia (detektory obsadenia)", option),
            option => Assert.Equal("Test kompenzácie brzdenia (momentové kontakty)", option),
            option => Assert.Equal("Manuálne meranie pomocou externého zariadenia", option));

        Assert.Collection(
            viewModel.CalibrationMethodOptions,
            option =>
            {
                Assert.Equal(CalibrationMethod.AutomaticFullProfileOccupancy, option.Method);
                Assert.Equal("Automatické meranie kompletného rýchlostného profilu (detektory obsadenia)", option.Description);
            },
            option =>
            {
                Assert.Equal(CalibrationMethod.AutomaticFullProfileMomentary, option.Method);
                Assert.Equal("Automatické meranie kompletného rýchlostného profilu (momentové kontakty)", option.Description);
            },
            option =>
            {
                Assert.Equal(CalibrationMethod.AutomaticSingleStepOccupancy, option.Method);
                Assert.Equal("Automatické meranie jedného rýchlostného stupňa (detektory obsadenia)", option.Description);
            },
            option =>
            {
                Assert.Equal(CalibrationMethod.AutomaticSingleStepMomentary, option.Method);
                Assert.Equal("Automatické meranie jedného rýchlostného stupňa (momentové kontakty)", option.Description);
            },
            option =>
            {
                Assert.Equal(CalibrationMethod.BrakingCompensationTestOccupancy, option.Method);
                Assert.Equal("Test kompenzácie brzdenia (detektory obsadenia)", option.Description);
            },
            option =>
            {
                Assert.Equal(CalibrationMethod.BrakingCompensationTestMomentary, option.Method);
                Assert.Equal("Test kompenzácie brzdenia (momentové kontakty)", option.Description);
            },
            option =>
            {
                Assert.Equal(CalibrationMethod.ManualExternalDevice, option.Method);
                Assert.Equal("Manuálne meranie pomocou externého zariadenia", option.Description);
            });

        Assert.NotNull(viewModel.SelectedMethod);
        Assert.Equal(CalibrationMethod.AutomaticFullProfileOccupancy, viewModel.SelectedMethod!.Method);
        Assert.Equal("Automatické meranie kompletného rýchlostného profilu (detektory obsadenia)", viewModel.SelectedCalibrationMethod);
        Assert.Equal("Automatické meranie kompletného rýchlostného profilu (detektory obsadenia)", viewModel.SelectedCalibrationMethodTooltip);
        Assert.True(viewModel.IsBlockConfigurationEnabled);

        Assert.True(viewModel.IsStartBlockEnabled);
        Assert.True(viewModel.IsMiddleBlockEnabled);
        Assert.True(viewModel.IsEndBlockEnabled);

        viewModel.SelectedMethod = viewModel.CalibrationMethods[1];
        Assert.True(viewModel.IsStartBlockEnabled);
        Assert.False(viewModel.IsMiddleBlockEnabled);
        Assert.True(viewModel.IsEndBlockEnabled);

        viewModel.SelectedMethod = viewModel.CalibrationMethods[2];
        Assert.True(viewModel.IsStartBlockEnabled);
        Assert.True(viewModel.IsMiddleBlockEnabled);
        Assert.True(viewModel.IsEndBlockEnabled);

        viewModel.SelectedMethod = viewModel.CalibrationMethods[3];
        Assert.True(viewModel.IsStartBlockEnabled);
        Assert.False(viewModel.IsMiddleBlockEnabled);
        Assert.True(viewModel.IsEndBlockEnabled);

        viewModel.SelectedMethod = viewModel.CalibrationMethods[4];
        Assert.False(viewModel.IsStartBlockEnabled);
        Assert.True(viewModel.IsMiddleBlockEnabled);
        Assert.False(viewModel.IsEndBlockEnabled);

        viewModel.SelectedMethod = viewModel.CalibrationMethods[5];
        Assert.True(viewModel.IsStartBlockEnabled);
        Assert.False(viewModel.IsMiddleBlockEnabled);
        Assert.False(viewModel.IsEndBlockEnabled);

        viewModel.SelectedMethod = viewModel.CalibrationMethods[6];
        Assert.False(viewModel.IsStartBlockEnabled);
        Assert.False(viewModel.IsMiddleBlockEnabled);
        Assert.False(viewModel.IsEndBlockEnabled);
        Assert.False(viewModel.IsBlockConfigurationEnabled);

        viewModel.SelectedMethod = viewModel.CalibrationMethods[0];

        Assert.Empty(viewModel.StartBlockOptions);
        Assert.Null(viewModel.SelectedStartBlock);
        Assert.Empty(viewModel.SpeedProfileRows);
        Assert.Empty(viewModel.ForwardMeasurementPoints);
        Assert.Empty(viewModel.BackwardMeasurementPoints);
        Assert.Equal(string.Empty, viewModel.ForwardCurvePathData);
        Assert.Empty(viewModel.ForwardCurveMarkers);
        Assert.False(viewModel.HasPendingProfileProjectChanges);

        viewModel.SyncProjectIndicators(new[]
        {
            "Stanica A / Indikátor 2",
            "Depo / Indikátor 1",
            "Stanica A / Indikátor 3"
        });

        Assert.Equal(new[]
        {
            "Depo / Indikátor 1",
            "Stanica A / Indikátor 2",
            "Stanica A / Indikátor 3"
        }, viewModel.StartBlockOptions.Select(option => option.DisplayName));
        Assert.All(viewModel.StartBlockOptions, option => Assert.Equal("avares://TrackFlow/Assets/Appicons/16/cont_ind.png", option.IconPath));
        Assert.Equal(viewModel.StartBlockOptions.Select(option => option.DisplayName), viewModel.MiddleBlockOptions.Select(option => option.DisplayName));
        Assert.Equal(viewModel.StartBlockOptions.Select(option => option.DisplayName), viewModel.EndBlockOptions.Select(option => option.DisplayName));
        Assert.Null(viewModel.SelectedStartBlock);
        Assert.Null(viewModel.SelectedMiddleBlock);
        Assert.Null(viewModel.SelectedEndBlock);

        viewModel.PauseSecondsText = "19,7abc";
        Assert.Equal("15", viewModel.PauseSecondsText);
        Assert.Equal(15.0, viewModel.PauseSeconds);

        viewModel.PauseSecondsText = "2.5";
        Assert.Equal("2.5", viewModel.PauseSecondsText);
        Assert.Equal(2.5, viewModel.PauseSeconds);

        viewModel.RunoutDistanceMmText = "12,345abc";
        Assert.Equal("12.34", viewModel.RunoutDistanceMmText);
        Assert.Equal(12.34, viewModel.RunoutDistanceMm);

        viewModel.RunoutDistanceMmText = "8";
        Assert.Equal("8.00", viewModel.RunoutDistanceMmText);
        Assert.Equal(8.0, viewModel.RunoutDistanceMm);
    }

    [Fact]
    public void SpeedEditor_PrepnutieMetodyVycistiVybrateBlockyKtoreSuDisabled()
    {
        var viewModel = new LocomotiveSpeedEditorViewModel();
        viewModel.SyncProjectIndicators(new[]
        {
            "Stanica / Blok 1",
            "Stanica / Blok 2",
            "Stanica / Blok 3"
        });

        // Metóda 1: všetky Enabled – vyberieme hodnoty do všetkých troch
        viewModel.SelectedMethod = viewModel.CalibrationMethods[0];
        viewModel.SelectedStartBlock = viewModel.StartBlockOptions[0];
        viewModel.SelectedMiddleBlock = viewModel.MiddleBlockOptions[1];
        viewModel.SelectedEndBlock = viewModel.EndBlockOptions[2];

        Assert.NotNull(viewModel.SelectedStartBlock);
        Assert.NotNull(viewModel.SelectedMiddleBlock);
        Assert.NotNull(viewModel.SelectedEndBlock);

        // Metóda 2: Stred Disabled → SelectedMiddleBlock sa musí vyčistiť
        viewModel.SelectedMethod = viewModel.CalibrationMethods[1];
        Assert.NotNull(viewModel.SelectedStartBlock);
        Assert.Null(viewModel.SelectedMiddleBlock);
        Assert.NotNull(viewModel.SelectedEndBlock);

        // Metóda 5: Štart a Koniec Disabled → musia sa vyčistiť
        viewModel.SelectedStartBlock = viewModel.StartBlockOptions[0];
        viewModel.SelectedEndBlock = viewModel.EndBlockOptions[2];
        viewModel.SelectedMethod = viewModel.CalibrationMethods[4];
        Assert.Null(viewModel.SelectedStartBlock);
        Assert.Null(viewModel.SelectedEndBlock);

        // Metóda 6: Stred a Koniec Disabled
        viewModel.SelectedMiddleBlock = viewModel.MiddleBlockOptions[0];
        viewModel.SelectedEndBlock = viewModel.EndBlockOptions[2];
        viewModel.SelectedMethod = viewModel.CalibrationMethods[5];
        Assert.Null(viewModel.SelectedMiddleBlock);
        Assert.Null(viewModel.SelectedEndBlock);

        // Metóda 7: všetky Disabled → všetky sa vyčistia
        viewModel.SelectedStartBlock = viewModel.StartBlockOptions[0];
        viewModel.SelectedMethod = viewModel.CalibrationMethods[6];
        Assert.Null(viewModel.SelectedStartBlock);
        Assert.Null(viewModel.SelectedMiddleBlock);
        Assert.Null(viewModel.SelectedEndBlock);
    }


    [Fact]
    public void SpeedEditor_IndikatorMechanickejStabilityNormalizujeAsymetriuPodlaMaximalnejRychlosti()
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Laminátka", Number = "240", Power = 3080, MaxSpeed = 120 };
        ReplaceDiagnosticsPoints(locomotive,
            (10, 26.0, 24.0),
            (60, 74.0, 70.0),
            (110, 114.0, 108.0));

        var viewModel = new LocomotiveSpeedEditorViewModel();
        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);

        Assert.Equal("3 Spol. bodov", viewModel.AverageDifferenceDetailText);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.FrictionCurvePathData));
        Assert.True(string.IsNullOrWhiteSpace(viewModel.PowerCurvePathData));
        Assert.Equal(20.0, viewModel.MechanicalChartAxisMaximum, 1);
        Assert.Equal("20", viewModel.MechanicalYAxisTopLabel);
        Assert.True(viewModel.MechanicalWarningThresholdVisible);
        Assert.True(viewModel.MechanicalIdealThresholdVisible);
        Assert.Equal("kritický stupeň: 110", viewModel.MechanicalCriticalStepText);
        Assert.Contains("5,0%", viewModel.FrictionPeakSummaryText, StringComparison.Ordinal);
        Assert.Equal(string.Empty, viewModel.PowerUsageSummaryText);
        Assert.True(string.IsNullOrWhiteSpace(viewModel.PerformanceEmptyStateText));
    }

    [Fact]
    public void SpeedEditor_IndikatorMechanickejStabilityPouzijeRovnakyVzorecPreGrafAjTextNaKritickomStupni25()
    {
        var locomotive = new LocoRecord
        {
            Id = "loco-1",
            Name = "Brejlovec",
            Number = "754",
            DecoderType = "DCC 28",
            MaxSpeed = 120
        };

        ReplaceDiagnosticsPoints(locomotive,
            (0, 0.0, 0.0),
            (25, 60.0, 57.6),
            (28, 66.0, 65.0));

        var viewModel = new LocomotiveSpeedEditorViewModel();
        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);

        Assert.Equal("kritický stupeň: 25", viewModel.MechanicalCriticalStepText);
        Assert.Equal("max. asymetria: 2,0%", viewModel.FrictionPeakSummaryText);
        Assert.Contains("235.57 116.2", viewModel.FrictionCurvePathData, StringComparison.Ordinal);
    }

    [Fact]
    public void SpeedEditor_IndikatorMechanickejStabilityHladaMaximumAjNaKoncovomStupni()
    {
        var locomotive = new LocoRecord
        {
            Id = "loco-1",
            Name = "Brejlovec",
            Number = "754",
            DecoderType = "DCC 28",
            MaxSpeed = 120
        };

        ReplaceDiagnosticsPoints(locomotive,
            (0, 0.0, 0.0),
            (25, 60.0, 58.2),
            (28, 66.0, 63.6));

        var viewModel = new LocomotiveSpeedEditorViewModel();
        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);

        Assert.Equal("kritický stupeň: 28", viewModel.MechanicalCriticalStepText);
        Assert.Equal("max. asymetria: 2,0%", viewModel.FrictionPeakSummaryText);
        Assert.EndsWith("260 116.2", viewModel.FrictionCurvePathData, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(25.0, 24.0, "Stav diagnostiky: OK", AiDiagnosticSeverity.Ok, "●", "#E4F8E8", "#B7E4C0", "#166534")]
    [InlineData(25.0, 21.5, "Stav diagnostiky: Upozornenie", AiDiagnosticSeverity.Warning, "⚠", "#FFF4E5", "#F7C97D", "#9A5B00")]
    [InlineData(25.0, 19.0, "Stav diagnostiky: Zlé", AiDiagnosticSeverity.Error, "⛔", "#FDEAEA", "#F1A8A8", "#991B1B")]
    public void SpeedEditor_AiDiagnostikaPrepinaFarbyATvarIkonyPodlaZavaznosti(
        double forwardSpeed,
        double backwardSpeed,
        string expectedStatusText,
        AiDiagnosticSeverity expectedSeverity,
        string expectedIcon,
        string expectedBackground,
        string expectedBorderBrush,
        string expectedForeground)
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        locomotive.ForwardSpeedProfilePoints.Add(new LocoSpeedProfilePoint
        {
            Step = 10,
            Direction = "Dopredu",
            CalculatedSpeedKmh = forwardSpeed,
            RawSpeedKmh = forwardSpeed,
            TimeSeconds = 1,
            Status = "Automatika"
        });
        locomotive.BackwardSpeedProfilePoints.Add(new LocoSpeedProfilePoint
        {
            Step = 10,
            Direction = "Dozadu",
            CalculatedSpeedKmh = backwardSpeed,
            RawSpeedKmh = backwardSpeed,
            TimeSeconds = 1,
            Status = "Automatika"
        });

        var viewModel = new LocomotiveSpeedEditorViewModel();
        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);

        Assert.Equal(expectedStatusText, viewModel.EngineStatusText);
        Assert.Equal(expectedSeverity, viewModel.EngineStatusSeverity);
        Assert.Equal(expectedIcon, viewModel.EngineStatusIconText);
        Assert.Equal(expectedBackground, viewModel.EngineStatusBackground);
        Assert.Equal(expectedBorderBrush, viewModel.EngineStatusBorderBrush);
        Assert.Equal(expectedForeground, viewModel.EngineStatusForeground);
        Assert.StartsWith("Analýza:", viewModel.AnalysisSummaryText, StringComparison.Ordinal);
        Assert.StartsWith("Odporúčanie:", viewModel.AiRecommendationText, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.RecommendedCvTweaksText));
        Assert.True(Enum.IsDefined(viewModel.EngineStatusCauseType));
    }

    [Theory]
    [InlineData(25.0, 24.0)]
    [InlineData(25.0, 21.5)]
    [InlineData(25.0, 19.0)]
    public void SpeedEditor_AiDiagnostikaPouzivaNahodneTextoveVariantyPreRovnakyStav(double forwardSpeed, double backwardSpeed)
    {
        var analysisVariants = Enumerable.Range(0, 18)
            .Select(_ => CreateAndSaveSpeedEditorForDiagnostics(forwardSpeed, backwardSpeed).AnalysisSummaryText)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var recommendationVariants = Enumerable.Range(0, 18)
            .Select(_ => CreateAndSaveSpeedEditorForDiagnostics(forwardSpeed, backwardSpeed).AiRecommendationText)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var tweakVariants = Enumerable.Range(0, 18)
            .Select(_ => CreateAndSaveSpeedEditorForDiagnostics(forwardSpeed, backwardSpeed).RecommendedCvTweaksText)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(analysisVariants.Length >= 2, $"Očakávali sa aspoň 2 varianty textu Analýza, získané: {string.Join(" | ", analysisVariants)}");
        Assert.True(recommendationVariants.Length >= 2, $"Očakávali sa aspoň 2 varianty textu Odporúčanie, získané: {string.Join(" | ", recommendationVariants)}");
        Assert.True(tweakVariants.Length >= 2, $"Očakávali sa aspoň 2 varianty textu odporúčaných úprav, získané: {string.Join(" | ", tweakVariants)}");
    }


    [Fact]
    public void SpeedEditor_AiDiagnostikaRozpoznaProblemVNizkychKrokoch()
    {
        var viewModel = CreateSpeedEditorForDiagnostics(
            (4, 8.0, 4.0),
            (40, 40.0, 40.0),
            (90, 90.0, 90.0));

        Assert.Equal(AiDiagnosticProblemType.LowSteps, viewModel.EngineStatusProblemType);
        Assert.Equal(AiDiagnosticCauseType.StartupCvTuning, viewModel.EngineStatusCauseType);
        AssertDiagnosticTextContainsAny(viewModel, "nízkych krokoch", "rozjazd", "CV2", "CV3");
    }


    [Fact]
    public void SpeedEditor_AiDiagnostikaRozpoznaProblemVoVysokejRychlosti()
    {
        var viewModel = CreateSpeedEditorForDiagnostics(
            (10, 20.0, 20.0),
            (60, 70.0, 70.0),
            (110, 120.0, 116.0));

        Assert.Equal(AiDiagnosticProblemType.HighSpeed, viewModel.EngineStatusProblemType);
        Assert.Equal(AiDiagnosticCauseType.TopCurveInstability, viewModel.EngineStatusCauseType);
        AssertDiagnosticTextContainsAny(viewModel, "vysokej rýchlosti", "hornej časti krivky", "CV5");
    }

    [Fact]
    public void SpeedEditor_AiDiagnostikaRozpoznaMechanickyOdporAkoPravdepodobnuPricinu()
    {
        var viewModel = CreateSpeedEditorForDiagnostics(
            (10, 20.0, 19.0),
            (60, 70.0, 67.0),
            (110, 120.0, 114.0));

        Assert.Equal(AiDiagnosticProblemType.DirectionAsymmetry, viewModel.EngineStatusProblemType);
        Assert.Equal(AiDiagnosticCauseType.MechanicalResistance, viewModel.EngineStatusCauseType);
        AssertDiagnosticTextContainsAny(viewModel, "mechanický odpor", "trenie", "spriahad", "pohon");
    }

    [Fact]
    public void LocomotivesWindowViewModel_ExponujeSpeedEditorProperty()
    {
        var property = typeof(LocomotivesWindowViewModel)
            .GetProperty("SpeedEditor", BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(property);
        Assert.Equal(typeof(LocomotiveSpeedEditorViewModel), property.PropertyType);
    }

    [Fact]
    public void LocomotivesWindowViewModel_VypnutieDccProgramovaniaAutomatickyZrusiRezimMerania()
    {
        var settings = new TrackFlow.Services.SettingsManager();
        settings.NewProject();
        var viewModel = new LocomotivesWindowViewModel(settings);
        settings.ProjectLocomotives.Add(new LocoRecord { Name = "Test", Address = 3, IsDccProgrammingEnabled = true });
        viewModel.RunWithSaveGuard(() => viewModel.Selected = settings.ProjectLocomotives[0]);

        viewModel.IsDisableDynamicsForMeasurement = true;
        viewModel.IsDccProgrammingEnabled = false;

        Assert.False(viewModel.IsDisableDynamicsForMeasurement);

        viewModel.IsDisableDynamicsForMeasurement = true;

        Assert.False(viewModel.IsDisableDynamicsForMeasurement);
    }

    [Fact]
    public void LocoRecord_NovaLokomotivaMaDccProgramovanieDefaultneVypnute()
    {
        var loco = new LocoRecord();

        Assert.False(loco.IsDccProgrammingEnabled);
    }

    [Fact]
    public void LocoRecord_VypnutieDccProgramovaniaVynulujeVsetkyDccHodnoty()
    {
        var loco = new LocoRecord
        {
            IsDccProgrammingEnabled = true,
            MinSpeedCv = 12,
            MidSpeedCv = 34,
            MaxSpeedCv = 56,
            AccelerationCv = 78,
            BrakingCv = 90,
            Cv29Value = 0x26,
            IsInvertDirectionEnabled = true,
            IsAnalogOperationEnabled = true,
            IsBemfEnabled = true,
            IsDisableDynamicsForMeasurement = true,
            BrakeCorrection = 45
        };

        loco.IsDccProgrammingEnabled = false;

        Assert.Equal(0, loco.MinSpeedCv);
        Assert.Equal(0, loco.MidSpeedCv);
        Assert.Equal(0, loco.MaxSpeedCv);
        Assert.Equal(0, loco.AccelerationCv);
        Assert.Equal(0, loco.BrakingCv);
        Assert.Equal(0, loco.Cv29Value);
        Assert.False(loco.IsInvertDirectionEnabled);
        Assert.False(loco.IsAnalogOperationEnabled);
        Assert.False(loco.IsBemfEnabled);
        Assert.False(loco.IsDisableDynamicsForMeasurement);
        Assert.Equal(0, loco.BrakeCorrection);
    }

    [Fact]
    public void LocomotivesWindowViewModel_NaplniKalibracneComboBoxyZoZivychIndikatorovBlokov()
    {
        var settings = new TrackFlow.Services.SettingsManager();
        settings.NewProject();

        var liveElements = new LayoutElement[]
        {
            new BlockElement
            {
                Label = "Stanica A",
                Indicators =
                {
                    new BlockIndicator { Name = "Kontakt 1", Type = BlockIndicatorType.Contact },
                    new BlockIndicator { Name = "Kontakt 2", Type = BlockIndicatorType.Contact }
                }
            },
            new BlockElement
            {
                Label = "Depo",
                Indicators =
                {
                    new BlockIndicator { Name = "Flagman", Type = BlockIndicatorType.Flagman }
                }
            }
        };

        var viewModel = new LocomotivesWindowViewModel(settings, liveElements);

        Assert.Equal(new[]
        {
            "Flagman",
            "Kontakt 1",
            "Kontakt 2"
        }, viewModel.SpeedEditor.StartBlockOptions.Select(option => option.DisplayName));
        Assert.Equal(new[] { "⚑", "●", "●" }, viewModel.SpeedEditor.StartBlockOptions.Select(option => option.IconGlyph));
        Assert.Equal(new[]
        {
            "avares://TrackFlow/Assets/Appicons/16/flag_d.png",
            "avares://TrackFlow/Assets/Appicons/16/cont_ind_d.png",
            "avares://TrackFlow/Assets/Appicons/16/cont_ind_d.png"
        }, viewModel.SpeedEditor.StartBlockOptions.Select(option => option.IconPath));
        Assert.Equal(viewModel.SpeedEditor.StartBlockOptions.Select(option => option.DisplayName), viewModel.SpeedEditor.MiddleBlockOptions.Select(option => option.DisplayName));
        Assert.Equal(viewModel.SpeedEditor.StartBlockOptions.Select(option => option.DisplayName), viewModel.SpeedEditor.EndBlockOptions.Select(option => option.DisplayName));
        Assert.Null(viewModel.SpeedEditor.SelectedStartBlock);
    }

    [Fact]
    public void SpeedEditor_SyncIndicatorActiveStatesPrepinaIkonuPodlaFeedbacku()
    {
        const string activeIcon = "avares://TrackFlow/Assets/Appicons/16/cont_ind.png";
        const string inactiveIcon = "avares://TrackFlow/Assets/Appicons/16/cont_ind_d.png";
        var indicatorId = Guid.NewGuid();

        var viewModel = new LocomotiveSpeedEditorViewModel();
        viewModel.SyncProjectIndicators(new[]
        {
            new CalibrationIndicatorOption("Kontakt 1", "●", activeIcon, inactiveIcon, isActive: false, indicatorId: indicatorId)
        });

        Assert.Equal(inactiveIcon, viewModel.StartBlockOptions.Single().IconPath);

        var block = new BlockElement();
        block.Indicators.Add(new BlockIndicator
        {
            Id = indicatorId,
            Name = "Kontakt 1",
            Type = BlockIndicatorType.Contact,
            IsActive = true
        });

        viewModel.SyncIndicatorActiveStates(new[] { block });
        Assert.Equal(activeIcon, viewModel.StartBlockOptions.Single().IconPath);

        block.Indicators[0].IsActive = false;
        viewModel.SyncIndicatorActiveStates(new[] { block });
        Assert.Equal(inactiveIcon, viewModel.StartBlockOptions.Single().IconPath);
    }

    [Fact]
    public void SpeedEditor_ProfilJeViazanyNaVybranuLokomotivuAVybranySmerZalozky()
    {
        var first = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        var second = new LocoRecord { Id = "loco-2", Name = "Zamracena", Number = "749" };
        var viewModel = new LocomotiveSpeedEditorViewModel();

        viewModel.SyncLocomotives(new[] { first, second }, first);
        viewModel.SelectedProfileTabIndex = 0;
        viewModel.AddPointManuallyCommand.Execute(null);
        viewModel.HandleChartPointerPressed(new Point(220, 420));

        Assert.Single(viewModel.SpeedProfileRows);
        Assert.Single(viewModel.ForwardMeasurementPoints);
        Assert.Empty(viewModel.BackwardMeasurementPoints);
        Assert.Single(first.ForwardSpeedProfilePoints);
        Assert.Empty(first.BackwardSpeedProfilePoints);
        Assert.Empty(second.ForwardSpeedProfilePoints);
        Assert.Equal("Dopredu", first.ForwardSpeedProfilePoints[0].Direction);

        viewModel.SyncLocomotives(new[] { first, second }, second);

        Assert.Empty(viewModel.SpeedProfileRows);
        Assert.Empty(viewModel.ForwardMeasurementPoints);
        Assert.Empty(viewModel.BackwardMeasurementPoints);

        viewModel.SelectedProfileTabIndex = 1;
        viewModel.AddPointManuallyCommand.Execute(null);
        viewModel.HandleChartPointerPressed(new Point(320, 360));

        Assert.Single(viewModel.SpeedProfileRows);
        Assert.Empty(viewModel.ForwardMeasurementPoints);
        Assert.Single(viewModel.BackwardMeasurementPoints);
        Assert.Empty(second.ForwardSpeedProfilePoints);
        Assert.Single(second.BackwardSpeedProfilePoints);
        Assert.Equal("Dozadu", second.BackwardSpeedProfilePoints[0].Direction);

        viewModel.SyncLocomotives(new[] { first, second }, first);

        Assert.Single(viewModel.ForwardMeasurementPoints);
        Assert.Empty(viewModel.BackwardMeasurementPoints);
        Assert.Equal(first.ForwardSpeedProfilePoints[0].Step, viewModel.ForwardMeasurementPoints[0].Step);
    }

    [Fact]
    public void SpeedEditor_PridanieBoduManualneNajprvZapneRezimAKlikDoGrafuVytvoriManualnyBod()
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        var viewModel = new LocomotiveSpeedEditorViewModel();

        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);
        viewModel.SelectedProfileTabIndex = 0;

        viewModel.AddPointManuallyCommand.Execute(null);

        Assert.True(viewModel.IsManualPlacementMode);
        Assert.Empty(viewModel.SpeedProfileRows);
        Assert.Empty(viewModel.ForwardMeasurementPoints);

        var handled = viewModel.HandleChartPointerPressed(new Point(470, 283));

        Assert.True(handled);
        Assert.False(viewModel.IsDraggingChartPoint);
        Assert.Single(viewModel.SpeedProfileRows);
        Assert.Equal("M", viewModel.SpeedProfileRows[0].TypeBadgeText);
        Assert.Single(viewModel.ForwardMeasurementPoints);
        var point = viewModel.ForwardMeasurementPoints[0];
        Assert.True(point.IsManual);
        Assert.Equal("Manuálne zadané", point.Status);
        Assert.Equal(point.Step, locomotive.ForwardSpeedProfilePoints[0].Step);
        Assert.Equal(point.CalculatedSpeedKmh, locomotive.ForwardSpeedProfilePoints[0].CalculatedSpeedKmh);
        Assert.StartsWith("M 58 544 L ", viewModel.ForwardCurvePathData, StringComparison.Ordinal);
        Assert.StartsWith("M 58 544 L 58 544 L ", viewModel.ForwardAreaPathData, StringComparison.Ordinal);
        Assert.EndsWith("544 Z", viewModel.ForwardAreaPathData, StringComparison.Ordinal);
    }

    [Fact]
    public void SpeedEditor_ManualnyBodDopreduNevytvoriPrazdnyRiadokVDozaduTabulke()
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        var viewModel = new LocomotiveSpeedEditorViewModel();

        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);
        viewModel.SelectedProfileTabIndex = 0;
        viewModel.AddPointManuallyCommand.Execute(null);
        viewModel.HandleChartPointerPressed(new Point(300, 400));

        Assert.Single(viewModel.ForwardSpeedProfileRows);
        Assert.Empty(viewModel.BackwardSpeedProfileRows);
        Assert.Equal(viewModel.ForwardMeasurementPoints[0].Step, viewModel.ForwardSpeedProfileRows[0].Step);
    }

    [Fact]
    public void SpeedEditor_ManualnyBodDozaduNevytvoriPrazdnyRiadokVDopreduTabulke()
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        var viewModel = new LocomotiveSpeedEditorViewModel();

        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);
        viewModel.SelectedProfileTabIndex = 1;
        viewModel.AddPointManuallyCommand.Execute(null);
        viewModel.HandleChartPointerPressed(new Point(320, 360));

        Assert.Empty(viewModel.ForwardSpeedProfileRows);
        Assert.Single(viewModel.BackwardSpeedProfileRows);
        Assert.Equal(viewModel.BackwardMeasurementPoints[0].Step, viewModel.BackwardSpeedProfileRows[0].Step);
    }

    [Theory]
    [InlineData("DCC 14")]
    [InlineData("DCC 28")]
    [InlineData("DCC 126")]
    [InlineData(null)]
    public void SpeedEditor_RozsahXosiJeVzdyFixnych28BodovBezOhladuNaDecoderType(string? decoderType)
    {
        // Graf je fixne 28-bodový – typ dekodéra neovplyvňuje rozsah X osi.
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754", DecoderType = decoderType };
        var viewModel = new LocomotiveSpeedEditorViewModel();

        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);

        Assert.Equal(28, viewModel.CurrentChartMaxStep);
        Assert.Equal("Rýchlostný stupeň dekodéra (0-28)", viewModel.DecoderStepAxisTitle);
        Assert.Equal(new[] { "0", "4", "8", "12", "16", "20", "24", "28" }, viewModel.XAxisLabels.Select(label => label.Text).ToArray());
        Assert.Equal(8, viewModel.XAxisTickMarks.Count);
        Assert.True(viewModel.XAxisLabels.Zip(viewModel.XAxisLabels.Skip(1), (left, right) => right.Left > left.Left).All(result => result));
        Assert.True(viewModel.XAxisLabels.First().Left >= 50);
        Assert.True(viewModel.XAxisLabels.Last().Left >= 860);
    }

    [Fact]
    public void SpeedEditor_Dcc28MapujeMajorKrok4NaSkutocnuXPolohuCiaryTickuAPopisu()
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754", DecoderType = "DCC 28" };
        var viewModel = new LocomotiveSpeedEditorViewModel();

        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);

        var expectedAxisX = 58 + (4d / 28d) * 824d;
        var label = viewModel.XAxisLabels.Single(item => item.Text == "4");
        var firstGridLine = viewModel.VerticalGridLines.First();
        var tick = viewModel.XAxisTickMarks.Single(item => Math.Abs(item.StartPoint.X - expectedAxisX) < 0.01);

        Assert.InRange(firstGridLine.StartPoint.X, expectedAxisX - 0.01, expectedAxisX + 0.01);
        Assert.InRange(firstGridLine.EndPoint.X, expectedAxisX - 0.01, expectedAxisX + 0.01);
        Assert.InRange(tick.StartPoint.X, expectedAxisX - 0.01, expectedAxisX + 0.01);
        Assert.InRange(tick.EndPoint.X, expectedAxisX - 0.01, expectedAxisX + 0.01);
        Assert.InRange(label.Left, (expectedAxisX - 4) - 0.01, (expectedAxisX - 4) + 0.01);
    }

    [Fact]
    public void LocomotivesWindowViewModel_GrafJeVzdyFixnych28BodovAjKeďLokomotivaMAInyDecoderType()
    {
        // ComboBox „Krok dekodéra" bol odstránený – SpeedEditor má fixný rozsah 28.
        var settings = new TrackFlow.Services.SettingsManager();
        settings.NewProject();
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754", DecoderType = "DCC 126" };
        settings.ProjectLocomotives.Add(locomotive);
        var viewModel = new LocomotivesWindowViewModel(settings)
        {
            Selected = locomotive
        };

        // Rozsah X osi je vždy 28 bez ohľadu na DecoderType lokomotivy.
        Assert.Equal(28, viewModel.SpeedEditor.CurrentChartMaxStep);
        Assert.Equal("Rýchlostný stupeň dekodéra (0-28)", viewModel.SpeedEditor.DecoderStepAxisTitle);
        Assert.Equal(new[] { "0", "4", "8", "12", "16", "20", "24", "28" }, viewModel.SpeedEditor.XAxisLabels.Select(label => label.Text).ToArray());
    }

    [Fact]
    public void SpeedEditor_RozsahYosiSaRiadiMaximalnouRychlostouLokomotivy()
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754", MaxSpeed = 80 };
        var viewModel = new LocomotiveSpeedEditorViewModel();

        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);

        Assert.Equal(80, viewModel.CurrentChartMaxSpeed);
        Assert.Equal(new[] { "0", "20", "40", "60", "80" }, viewModel.YAxisLabels.Select(label => label.Text).ToArray());
        Assert.Equal(4, viewModel.HorizontalGridLines.Count);

        viewModel.SelectedProfileTabIndex = 0;
        viewModel.AddPointManuallyCommand.Execute(null);
        viewModel.HandleChartPointerPressed(new Point(470, 283));

        Assert.Single(viewModel.SpeedProfileRows);
        Assert.Single(viewModel.ForwardMeasurementPoints);
        Assert.Equal(40.0, viewModel.ForwardMeasurementPoints[0].CalculatedSpeedKmh, 1);
    }

    [Fact]
    public void LocomotivesWindowViewModel_ZmenaMaxRychlostiOkamzitePrepocitaRozsahYosiGrafu()
    {
        var settings = new TrackFlow.Services.SettingsManager();
        settings.NewProject();
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754", MaxSpeed = 120 };
        settings.ProjectLocomotives.Add(locomotive);
        var viewModel = new LocomotivesWindowViewModel(settings)
        {
            Selected = locomotive
        };

        viewModel.MaxSpeed = 100;

        Assert.Equal(100, viewModel.SpeedEditor.CurrentChartMaxSpeed);
        Assert.Equal(new[] { "0", "20", "40", "60", "80", "100" }, viewModel.SpeedEditor.YAxisLabels.Select(label => label.Text).ToArray());
    }

    [Fact]
    public void SpeedEditor_LokomotivaBezMaxRychlostiPouzijeFallback120Kmh()
    {
        var fast = new LocoRecord { Id = "loco-fast", Name = "Rychla", Number = "1", MaxSpeed = 80 };
        var withoutMax = new LocoRecord { Id = "loco-empty", Name = "Nezadana", Number = "2", MaxSpeed = 0 };
        var viewModel = new LocomotiveSpeedEditorViewModel();

        viewModel.SyncLocomotives(new[] { fast, withoutMax }, fast);
        Assert.Equal(80, viewModel.CurrentChartMaxSpeed);

        viewModel.SyncLocomotives(new[] { fast, withoutMax }, withoutMax);

        Assert.Equal(120, viewModel.CurrentChartMaxSpeed);
        Assert.Equal(new[] { "0", "20", "40", "60", "80", "100", "120" }, viewModel.YAxisLabels.Select(label => label.Text).ToArray());
    }

    [Fact]
    public void LocomotivesWindowViewModel_VymazanieMaxRychlostiPouzijeFallback120Kmh()
    {
        var settings = new TrackFlow.Services.SettingsManager();
        settings.NewProject();
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754", MaxSpeed = 80 };
        settings.ProjectLocomotives.Add(locomotive);
        var viewModel = new LocomotivesWindowViewModel(settings)
        {
            Selected = locomotive
        };

        viewModel.MaxSpeedText = string.Empty;

        Assert.Equal(0, viewModel.MaxSpeed);
        Assert.Equal(120, viewModel.SpeedEditor.CurrentChartMaxSpeed);
    }

    [Fact]
    public void SpeedEditor_EditaciaHodnotyVTabulkeOkamziteAktualizujeGrafAjModel()
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        var viewModel = new LocomotiveSpeedEditorViewModel();

        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);
        viewModel.SelectedProfileTabIndex = 0;
        viewModel.AddPointManuallyCommand.Execute(null);
        viewModel.HandleChartPointerPressed(new Point(300, 400));

        var row = viewModel.SpeedProfileRows[0];
        row.FwdRawSpeedText = "12,4";

        var point = viewModel.ForwardMeasurementPoints[0];
        Assert.Equal(12.4, point.CalculatedSpeedKmh);
        Assert.Equal(12.4, point.RawSpeedKmh);
        Assert.True(point.IsManual);
        Assert.Equal(12.4, viewModel.SpeedProfileRows[0].FwdRawSpeed);
        Assert.Equal("M", viewModel.SpeedProfileRows[0].TypeBadgeText);
        Assert.Equal(12.4, locomotive.ForwardSpeedProfilePoints[0].CalculatedSpeedKmh);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ForwardCurvePathData));
    }



    [Fact]
    public void SpeedEditor_UlozenieProfiluVyžadujeVyberSmerovejZalozky()
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        var viewModel = new LocomotiveSpeedEditorViewModel();
        var persistInvoked = false;

        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);
        viewModel.SelectedProfileTabIndex = 2;
        viewModel.PersistProfileChanges = () =>
        {
            persistInvoked = true;
            return true;
        };

        viewModel.SaveProfileCommand.Execute(null);

        Assert.False(persistInvoked);
        Assert.Equal("Uloženie profilu: vyberte Profil dopredu alebo Profil dozadu.", viewModel.CalibrationStatusText);
    }

    [Fact]
    public void SpeedEditor_UlozenieProfiluPotvrdiProjektPreVybranuLokomotivuASmer()
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        var viewModel = new LocomotiveSpeedEditorViewModel();
        var persistCallCount = 0;

        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);
        viewModel.SelectedProfileTabIndex = 1;
        viewModel.AddPointManuallyCommand.Execute(null);
        viewModel.HandleChartPointerPressed(new Point(260, 340));
        viewModel.PersistProfileChanges = () =>
        {
            persistCallCount++;
            return true;
        };

        viewModel.SaveProfileCommand.Execute(null);

        Assert.Single(locomotive.BackwardSpeedProfilePoints);
        Assert.Equal(1, persistCallCount);
        Assert.Equal("Profil dozadu lokomotívy Brejlovec / 754 bol uložený.", viewModel.CalibrationStatusText);
    }

    [Fact]
    public void SpeedEditor_PoUlozeniZachovaTuIstuSaduDiagnostickychTextovAjPoZnovuotvoreniOkna()
    {
        var locomotive = CreateLocomotiveWithDiagnosticsPoints();
        var viewModel = new LocomotiveSpeedEditorViewModel();

        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);
        viewModel.SelectedProfileTabIndex = 0;
        viewModel.PersistProfileChanges = () => true;
        viewModel.SaveProfileCommand.Execute(null);

        var savedStatus = viewModel.EngineStatusText;
        var savedAnalysis = viewModel.AnalysisSummaryText;
        var savedRecommendation = viewModel.AiRecommendationText;
        var savedTweaks = viewModel.RecommendedCvTweaksText;
        var savedSeverity = viewModel.EngineStatusSeverity;
        var savedProblemType = viewModel.EngineStatusProblemType;
        var savedCauseType = viewModel.EngineStatusCauseType;

        Assert.Equal(savedStatus, locomotive.SavedDiagnosticsEngineStatusText);
        Assert.Equal(savedAnalysis, locomotive.SavedDiagnosticsAnalysisSummaryText);
        Assert.Equal(savedRecommendation, locomotive.SavedDiagnosticsAiRecommendationText);
        Assert.Equal(savedTweaks, locomotive.SavedDiagnosticsRecommendedCvTweaksText);

        var reopenedViewModel = new LocomotiveSpeedEditorViewModel();
        reopenedViewModel.SyncLocomotives(new[] { locomotive }, locomotive);

        Assert.Equal(savedStatus, reopenedViewModel.EngineStatusText);
        Assert.Equal(savedAnalysis, reopenedViewModel.AnalysisSummaryText);
        Assert.Equal(savedRecommendation, reopenedViewModel.AiRecommendationText);
        Assert.Equal(savedTweaks, reopenedViewModel.RecommendedCvTweaksText);
        Assert.Equal(savedSeverity, reopenedViewModel.EngineStatusSeverity);
        Assert.Equal(savedProblemType, reopenedViewModel.EngineStatusProblemType);
        Assert.Equal(savedCauseType, reopenedViewModel.EngineStatusCauseType);
    }

    [Fact]
    public void SpeedEditor_NeulozeneZmenyNemeniaDiagnostickeTextyAzDoDalsiehoUlozenia()
    {
        var locomotive = CreateLocomotiveWithDiagnosticsPoints();
        var viewModel = new LocomotiveSpeedEditorViewModel();

        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);
        viewModel.SelectedProfileTabIndex = 0;
        viewModel.PersistProfileChanges = () => true;
        viewModel.SaveProfileCommand.Execute(null);

        var savedStatus = viewModel.EngineStatusText;
        var savedAnalysis = viewModel.AnalysisSummaryText;
        var savedRecommendation = viewModel.AiRecommendationText;
        var savedTweaks = viewModel.RecommendedCvTweaksText;

        viewModel.AddPointManuallyCommand.Execute(null);
        viewModel.HandleChartPointerPressed(new Point(320, 360));

        Assert.Equal(savedStatus, viewModel.EngineStatusText);
        Assert.Equal(savedAnalysis, viewModel.AnalysisSummaryText);
        Assert.Equal(savedRecommendation, viewModel.AiRecommendationText);
        Assert.Equal(savedTweaks, viewModel.RecommendedCvTweaksText);
        Assert.Equal(savedAnalysis, locomotive.SavedDiagnosticsAnalysisSummaryText);
        Assert.Equal(savedRecommendation, locomotive.SavedDiagnosticsAiRecommendationText);
    }


    [Fact]
    public void SpeedEditor_PriRovnakejSeverityPoDalsomUlozeniZachovaHlavneTextyAMeniLenSpodneCvOdporucanie()
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        ReplaceDiagnosticsPoints(
            locomotive,
            (10, 20.0, 16.5),
            (60, 70.0, 70.0),
            (110, 120.0, 120.0));

        var viewModel = new LocomotiveSpeedEditorViewModel();
        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);
        viewModel.SelectedProfileTabIndex = 0;
        viewModel.PersistProfileChanges = () => true;
        viewModel.SaveProfileCommand.Execute(null);

        var savedAnalysis = viewModel.AnalysisSummaryText;
        var savedRecommendation = viewModel.AiRecommendationText;
        var savedTweaks = viewModel.RecommendedCvTweaksText;

        ReplaceDiagnosticsPoints(
            locomotive,
            (10, 20.0, 20.0),
            (60, 70.0, 70.0),
            (110, 120.0, 116.5));

        var reopenedViewModel = new LocomotiveSpeedEditorViewModel();
        reopenedViewModel.SyncLocomotives(new[] { locomotive }, locomotive);
        reopenedViewModel.SelectedProfileTabIndex = 0;
        reopenedViewModel.PersistProfileChanges = () => true;
        reopenedViewModel.SaveProfileCommand.Execute(null);

        Assert.Equal(AiDiagnosticSeverity.Warning, reopenedViewModel.EngineStatusSeverity);
        Assert.Equal(AiDiagnosticProblemType.HighSpeed, reopenedViewModel.EngineStatusProblemType);
        Assert.Equal(AiDiagnosticCauseType.TopCurveInstability, reopenedViewModel.EngineStatusCauseType);
        Assert.Equal(savedAnalysis, reopenedViewModel.AnalysisSummaryText);
        Assert.Equal(savedRecommendation, reopenedViewModel.AiRecommendationText);
        Assert.NotEqual(savedTweaks, reopenedViewModel.RecommendedCvTweaksText);
        Assert.Equal(reopenedViewModel.AnalysisSummaryText, locomotive.SavedDiagnosticsAnalysisSummaryText);
        Assert.Equal(reopenedViewModel.AiRecommendationText, locomotive.SavedDiagnosticsAiRecommendationText);
        Assert.Equal(reopenedViewModel.RecommendedCvTweaksText, locomotive.SavedDiagnosticsRecommendedCvTweaksText);
    }


    [Fact]
    public void SpeedEditor_UlozenieJednehoSmeruNevymazeOpacnyProfilLokomotivy()
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        locomotive.ForwardSpeedProfilePoints.Add(new LocoSpeedProfilePoint
        {
            Step = 20,
            Direction = "Dopredu",
            TimeSeconds = 5,
            RawSpeedKmh = 30,
            CalculatedSpeedKmh = 30,
            Status = "Automatika"
        });
        locomotive.BackwardSpeedProfilePoints.Add(new LocoSpeedProfilePoint
        {
            Step = 18,
            Direction = "Dozadu",
            TimeSeconds = 5,
            RawSpeedKmh = 28,
            CalculatedSpeedKmh = 28,
            Status = "Automatika"
        });

        var viewModel = new LocomotiveSpeedEditorViewModel();
        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);
        viewModel.SelectedProfileTabIndex = 1;
        viewModel.AddPointManuallyCommand.Execute(null);
        viewModel.HandleChartPointerPressed(new Point(260, 340));
        viewModel.PersistProfileChanges = () => true;

        viewModel.SaveProfileCommand.Execute(null);

        Assert.Single(locomotive.ForwardSpeedProfilePoints);
        Assert.Equal(20, locomotive.ForwardSpeedProfilePoints[0].Step);
        Assert.True(locomotive.BackwardSpeedProfilePoints.Count >= 1);
    }

    [Fact]
    public void LocomotivesWindowViewModel_UlozenieProfiluZKalibracnehoOknaZachovaObaSmeryNaLokomotive()
    {
        var settings = new TrackFlow.Services.SettingsManager();
        settings.NewProject();

        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        locomotive.ForwardSpeedProfilePoints.Add(new LocoSpeedProfilePoint
        {
            Step = 12,
            Direction = "Dopredu",
            TimeSeconds = 4,
            RawSpeedKmh = 22,
            CalculatedSpeedKmh = 22,
            Status = "Automatika"
        });
        locomotive.BackwardSpeedProfilePoints.Add(new LocoSpeedProfilePoint
        {
            Step = 11,
            Direction = "Dozadu",
            TimeSeconds = 4,
            RawSpeedKmh = 21,
            CalculatedSpeedKmh = 21,
            Status = "Automatika"
        });
        settings.ProjectLocomotives.Add(locomotive);

        var viewModel = new LocomotivesWindowViewModel(settings)
        {
            Selected = locomotive
        };

        viewModel.SpeedEditor.SelectedProfileTabIndex = 1;
        viewModel.SpeedEditor.AddPointManuallyCommand.Execute(null);
        viewModel.SpeedEditor.HandleChartPointerPressed(new Point(280, 320));

        viewModel.SpeedEditor.SaveProfileCommand.Execute(null);

        var persisted = settings.ProjectLocomotives.Single();
        Assert.Single(persisted.ForwardSpeedProfilePoints);
        Assert.Equal(12, persisted.ForwardSpeedProfilePoints[0].Step);
        Assert.True(persisted.BackwardSpeedProfilePoints.Count >= 1);
    }

    [Fact]
    public void LocomotivesWindowViewModel_UlozenieProfiluZKalibracnehoOknaNevycistiGrafyATabulkyVEditore()
    {
        var settings = new TrackFlow.Services.SettingsManager();
        settings.NewProject();

        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        locomotive.ForwardSpeedProfilePoints.Add(new LocoSpeedProfilePoint
        {
            Step = 12,
            Direction = "Dopredu",
            TimeSeconds = 4,
            RawSpeedKmh = 22,
            CalculatedSpeedKmh = 22,
            Status = "Automatika"
        });
        locomotive.BackwardSpeedProfilePoints.Add(new LocoSpeedProfilePoint
        {
            Step = 11,
            Direction = "Dozadu",
            TimeSeconds = 4,
            RawSpeedKmh = 21,
            CalculatedSpeedKmh = 21,
            Status = "Automatika"
        });
        settings.ProjectLocomotives.Add(locomotive);

        var viewModel = new LocomotivesWindowViewModel(settings)
        {
            Selected = locomotive
        };

        Assert.Single(viewModel.SpeedEditor.ForwardMeasurementPoints);
        Assert.Single(viewModel.SpeedEditor.BackwardMeasurementPoints);

        viewModel.SpeedEditor.SelectedProfileTabIndex = 1;
        viewModel.SpeedEditor.AddPointManuallyCommand.Execute(null);
        viewModel.SpeedEditor.HandleChartPointerPressed(new Point(280, 320));

        viewModel.SpeedEditor.SaveProfileCommand.Execute(null);

        Assert.NotNull(viewModel.SpeedEditor.SelectedLocomotive);
        Assert.Equal("loco-1", viewModel.SpeedEditor.SelectedLocomotive!.Source!.Id);
        Assert.Single(viewModel.SpeedEditor.ForwardMeasurementPoints);
        Assert.True(viewModel.SpeedEditor.BackwardMeasurementPoints.Count >= 1);
        Assert.True(viewModel.SpeedEditor.SpeedProfileRows.Count >= 1);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.SpeedEditor.ForwardCurvePathData));
        Assert.False(string.IsNullOrWhiteSpace(viewModel.SpeedEditor.BackwardCurvePathData));
    }

    [Fact]
    public void SpeedEditor_UlozenieProfiluJePovoleneLenPreSmeroveZalozky()
    {
        var viewModel = new LocomotiveSpeedEditorViewModel();

        viewModel.SelectedProfileTabIndex = 0;
        Assert.True(viewModel.CanSaveActiveProfile);
        Assert.True(viewModel.CanAddPointManually);
        Assert.True(viewModel.AddPointManuallyCommand.CanExecute(null));
        Assert.True(viewModel.IsForwardProfileSelected);
        Assert.False(viewModel.IsBackwardProfileSelected);
        Assert.False(viewModel.IsBothProfilesSelected);

        viewModel.SelectedProfileTabIndex = 1;
        Assert.True(viewModel.CanSaveActiveProfile);
        Assert.True(viewModel.CanAddPointManually);
        Assert.True(viewModel.AddPointManuallyCommand.CanExecute(null));
        Assert.False(viewModel.IsForwardProfileSelected);
        Assert.True(viewModel.IsBackwardProfileSelected);
        Assert.False(viewModel.IsBothProfilesSelected);
        Assert.Equal("#D92424", viewModel.SelectedProfileDisplayForeground);

        viewModel.SelectedProfileTabIndex = 2;
        Assert.False(viewModel.CanSaveActiveProfile);
        Assert.False(viewModel.CanAddPointManually);
        Assert.False(viewModel.AddPointManuallyCommand.CanExecute(null));
        Assert.False(viewModel.IsForwardProfileSelected);
        Assert.False(viewModel.IsBackwardProfileSelected);
        Assert.True(viewModel.IsBothProfilesSelected);
        Assert.Equal("#475569", viewModel.SelectedProfileDisplayForeground);
    }

    [Fact]
    public void SpeedEditor_InicializaciaProfiluVymazeObeTabulkyAGrafLokomotivy()
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        locomotive.ForwardSpeedProfilePoints.Add(new LocoSpeedProfilePoint
        {
            Step = 10,
            Direction = "Dopredu",
            TimeSeconds = 4,
            RawSpeedKmh = 20,
            CalculatedSpeedKmh = 20,
            Status = "Automatika"
        });
        locomotive.BackwardSpeedProfilePoints.Add(new LocoSpeedProfilePoint
        {
            Step = 12,
            Direction = "Dozadu",
            TimeSeconds = 4,
            RawSpeedKmh = 19,
            CalculatedSpeedKmh = 19,
            Status = "Automatika"
        });

        var dirtyCalled = false;
        var viewModel = new LocomotiveSpeedEditorViewModel
        {
            MarkProfileDirty = () => dirtyCalled = true
        };
        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);

        Assert.Single(viewModel.ForwardMeasurementPoints);
        Assert.Single(viewModel.BackwardMeasurementPoints);
        Assert.True(viewModel.SpeedProfileRows.Count >= 1);

        viewModel.InitializeProfiles();

        Assert.Empty(viewModel.ForwardMeasurementPoints);
        Assert.Empty(viewModel.BackwardMeasurementPoints);
        Assert.Empty(viewModel.SpeedProfileRows);
        Assert.True(string.IsNullOrWhiteSpace(viewModel.ForwardCurvePathData));
        Assert.True(string.IsNullOrWhiteSpace(viewModel.BackwardCurvePathData));
        Assert.Empty(locomotive.ForwardSpeedProfilePoints);
        Assert.Empty(locomotive.BackwardSpeedProfilePoints);
        Assert.True(dirtyCalled);
        Assert.Contains("boli inicializované", viewModel.CalibrationStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void LocomotivesWindowViewModel_PridanieBoduDoProfiluOkamziteOznaciProjektAkoDirtyAZachovaProfilNaVybranejLokomotive()
    {
        var settings = new TrackFlow.Services.SettingsManager();
        settings.NewProject();

        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        settings.ProjectLocomotives.Add(locomotive);

        var viewModel = new LocomotivesWindowViewModel(settings);
        settings.Dirty.MarkClean();

        viewModel.SpeedEditor.SelectedProfileTabIndex = 0;
        viewModel.SpeedEditor.AddPointManuallyCommand.Execute(null);
        viewModel.SpeedEditor.HandleChartPointerPressed(new Point(250, 390));

        Assert.True(settings.CurrentProject!.IsDirty);
        Assert.True(viewModel.SpeedEditor.HasPendingProfileProjectChanges);
        Assert.Single(viewModel.SpeedEditor.SpeedProfileRows);
        Assert.Single(locomotive.ForwardSpeedProfilePoints);
        Assert.Equal(locomotive.ForwardSpeedProfilePoints[0].Step, viewModel.SpeedEditor.ForwardMeasurementPoints[0].Step);
    }

    [Fact]
    public void LocomotivesWindowViewModel_OtvorenieEditoraLokomotivNevytvoriDirtyStavProjektu()
    {
        var settings = new TrackFlow.Services.SettingsManager();
        settings.NewProject();

        settings.ProjectLocomotives.Add(new LocoRecord
        {
            Id = "loco-1",
            Name = "Brejlovec",
            Number = "754",
            DecoderType = "DCC 28",
            MaxSpeed = 120,
            Scale = "H0 (1:87)"
        });

        settings.Dirty.MarkClean();

        var viewModel = new LocomotivesWindowViewModel(settings);

        Assert.False(settings.CurrentProject!.IsDirty);
        Assert.False(viewModel.IsDirty);
        Assert.Equal(LocomotivesWindowViewModel.EditorMode.Viewing, viewModel.Mode);
        Assert.False(viewModel.SpeedEditor.HasPendingProfileProjectChanges);
    }

    [Fact]
    public void LocomotivesWindowViewModel_IndikaciaNeulozenychProfilovychZmienZmiznePoVycisteniProjektu()
    {
        var settings = new TrackFlow.Services.SettingsManager();
        settings.NewProject();

        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        settings.ProjectLocomotives.Add(locomotive);

        var viewModel = new LocomotivesWindowViewModel(settings);
        settings.Dirty.MarkClean();

        viewModel.SpeedEditor.SelectedProfileTabIndex = 0;
        viewModel.SpeedEditor.AddPointManuallyCommand.Execute(null);
        viewModel.SpeedEditor.HandleChartPointerPressed(new Point(250, 390));

        Assert.True(viewModel.SpeedEditor.HasPendingProfileProjectChanges);

        settings.Dirty.MarkClean();

        Assert.False(viewModel.SpeedEditor.HasPendingProfileProjectChanges);
    }

    [Fact]
    public void MainWindow_DoktorSaPriStarteNeotvaraAutomaticky()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "MainWindow.axaml.cs"));

        Assert.DoesNotContain("Opened += (_, _) => OpenOrFocusDoctorWindow();", code, StringComparison.Ordinal);
        Assert.Contains("private void OpenOrFocusDoctorWindow()", code, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation = WindowStartupLocation.CenterOwner", code, StringComparison.Ordinal);
        Assert.Contains("_doctorWindow.Show(this);", code, StringComparison.Ordinal);
        Assert.Contains("_clockView.Show(this);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_PriZatvoreniZatvoriAjClockViewADalsiePomocneOkna()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "MainWindow.axaml.cs"));

        Assert.Contains("_clockView?.Close();", code, StringComparison.Ordinal);
        Assert.Contains("foreach (var window in new List<Window>(desktop.Windows))", code, StringComparison.Ordinal);
        Assert.Contains("if (!ReferenceEquals(window, this))", code, StringComparison.Ordinal);
        Assert.Contains("desktop.Shutdown();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_OtvorenieLokomotivZDoribbonuNerobiLayoutSyncKtoryByOznacilProjektAkoDirty()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "MainWindow.axaml.cs"));

        Assert.Contains("private async Task ShowLocomotivesDialogAsync()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_vm?.LayoutEditor.SyncToProject();", code, StringComparison.Ordinal);
        Assert.Contains("new LocomotivesWindowViewModel(_vm.SettingsManager, _vm.LayoutEditor.Elements, _vm.Dcc)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_OtvorenieLokomotivTiezNerobiLayoutSyncPredDialogom()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.DoesNotContain("mainVm.LayoutEditor.SyncToProject();", code, StringComparison.Ordinal);
        Assert.Contains("new TrackFlow.ViewModels.Library.LocomotivesWindowViewModel(resolvedMainVm.SettingsManager, resolvedMainVm.LayoutEditor.Elements, resolvedMainVm.Dcc)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_RegistrujeGlobalneHandleryNeobsluzenychVynimiek()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Program.cs"));

        Assert.Contains("AppDomain.CurrentDomain.UnhandledException +=", code, StringComparison.Ordinal);
        Assert.Contains("TaskScheduler.UnobservedTaskException +=", code, StringComparison.Ordinal);
        Assert.Contains("ReportUnhandledException(\"AppDomain.CurrentDomain.UnhandledException\"", code, StringComparison.Ordinal);
        Assert.Contains("trackflow-unhandled-", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_ClosingHandlerHlasiAsyncVoidVynimkyDoDiagnostiky()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "MainWindow.axaml.cs"));

        Assert.Contains("Program.ReportUnhandledException(\"MainWindow.OnWindowClosing\"", code, StringComparison.Ordinal);
        Assert.Contains("TrackFlowDoctorService.Instance.Diagnose(", code, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = true;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void LocomotivesWindow_VybraneAsyncVoidHandleryMajuExceptionReporting()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Library", "LocomotivesWindow.axaml.cs"));

        Assert.Contains("Program.ReportUnhandledException(\"LocomotivesWindow.OpenCalibrationWindow_Click\"", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"LocomotivesWindow.OpenProgrammingTrackSettings_Click\"", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"LocomotivesWindow.OnBrowseSoundFileClick\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void LocomotivesWindow_KritickeClickHandleryPouzivajuTaskWrapperNamiestoAsyncVoid()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Library", "LocomotivesWindow.axaml.cs"));

        Assert.Contains("private void ReadCvButton_Click(object? _, RoutedEventArgs __)", code, StringComparison.Ordinal);
        Assert.Contains("_ = ReadCvButton_ClickAsync();", code, StringComparison.Ordinal);
        Assert.Contains("private async Task ReadCvButton_ClickAsync()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private async void ReadCvButton_Click", code, StringComparison.Ordinal);

        Assert.Contains("private void OpenCalibrationWindow_Click(object? _, Avalonia.Interactivity.RoutedEventArgs __)", code, StringComparison.Ordinal);
        Assert.Contains("_ = OpenCalibrationWindow_ClickAsync();", code, StringComparison.Ordinal);
        Assert.Contains("private async Task OpenCalibrationWindow_ClickAsync()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private async void OpenCalibrationWindow_Click", code, StringComparison.Ordinal);

        Assert.Contains("public void OpenProgrammingTrackSettings_Click(object? _, Avalonia.Interactivity.RoutedEventArgs __)", code, StringComparison.Ordinal);
        Assert.Contains("_ = OpenProgrammingTrackSettings_ClickAsync();", code, StringComparison.Ordinal);
        Assert.Contains("private async Task OpenProgrammingTrackSettings_ClickAsync()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("public async void OpenProgrammingTrackSettings_Click", code, StringComparison.Ordinal);
    }

    [Fact]
    public void LocomotivesWindow_ReadCvDialogStartupNepouzivaAsyncEventLambduAMaExceptionReporting()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Library", "LocomotivesWindow.axaml.cs"));

        Assert.DoesNotContain("dialog.Opened += async (_, _) =>", code, StringComparison.Ordinal);
        Assert.Contains("void OnDialogOpened(object? sender, EventArgs args)", code, StringComparison.Ordinal);
        Assert.Contains("_ = StartReadDecoderValuesDialogAsync(dialog, programmingClient, timeoutMsPerCv, interCvDelayMs);", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"LocomotivesWindow.StartReadDecoderValuesDialogAsync\"", code, StringComparison.Ordinal);
        Assert.Contains("Štart čítania CV v progres-dialógu zlyhal", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DccProgrammingApi_UzNepouzivaRiskantnyDefaultLocoAddress3AServiceTrackCestyPosielajuNeutralnyPlaceholder()
    {
        var contract = File.ReadAllText(GetWorkspaceFilePath("Services", "Dcc", "IDccProgrammingClient.cs"));
        var z21 = File.ReadAllText(GetWorkspaceFilePath("Services", "Dcc", "Z21Client.cs"));
        var serial = File.ReadAllText(GetWorkspaceFilePath("Services", "Dcc", "SerialDccClient.cs"));
        var window = File.ReadAllText(GetWorkspaceFilePath("Views", "Library", "LocomotivesWindow.axaml.cs"));
        var viewModel = File.ReadAllText(GetWorkspaceFilePath("ViewModels", "Library", "LocomotivesWindowViewModel.cs"));

        Assert.DoesNotContain("int locoAddress = 3", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("int locoAddress = 3", z21, StringComparison.Ordinal);
        Assert.DoesNotContain("int locoAddress = 3", serial, StringComparison.Ordinal);

        Assert.DoesNotContain("GetSelectedLocomotiveAddressForPom() ?? 3", window, StringComparison.Ordinal);

        Assert.Equal(2, CountOccurrences(viewModel, "const int serviceTrackAddressPlaceholder = 0;"));
        Assert.Contains("locoAddress: serviceTrackAddressPlaceholder", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedLocomotive?.DccAddress ?? 3", viewModel, StringComparison.Ordinal);
    }


    [Fact]
    public void BlockPropertiesWindow_AsyncVoidDialogHandlerMaExceptionReporting()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Editor", "BlockPropertiesWindow.axaml.cs"));

        Assert.Contains("Program.ReportUnhandledException(\"BlockPropertiesWindow.OpenIndicatorPropertiesWindow\"", code, StringComparison.Ordinal);
        Assert.Contains("TrackFlowDoctorService.Instance.Diagnose(\"Editor\", $\"Otvorenie vlastností indikátora zlyhalo:", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_UzNeobsahujeMrtvyLegacyRenameTrainClickFlow()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.DoesNotContain("private async void RenameTrain_Click", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private void RenameTrain_Click(object? sender, RoutedEventArgs e)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = RenameTrainAsync();", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private async Task RenameTrainAsync()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private static async Task<string?> ShowFallbackRenameDialogAsync(Window? owner, string initialName)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Program.ReportUnhandledException(\"VehicleStripItem.RenameTrain_Click\"", code, StringComparison.Ordinal);
        Assert.Contains("private async Task OpenRenameMenuAsync()", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OpenRenameMenuAsync\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_OnPointerMovedDragVetvyMajuExceptionReporting()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnPointerMoved.LocoDrag\"", code, StringComparison.Ordinal);
        Assert.Contains("TrackFlowDoctorService.Instance.Diagnose(\"Súprava\", $\"Ťahanie lokomotívy zlyhalo:", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnPointerMoved.WagonDrag\"", code, StringComparison.Ordinal);
        Assert.Contains("TrackFlowDoctorService.Instance.Diagnose(\"Súprava\", $\"Ťahanie vagóna zlyhalo:", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_OnPointerMovedPouzivaTaskWrapperSVrchnymExceptionReportingomPreCelyDragFlow()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("private void OnPointerMoved(object? _, PointerEventArgs e)", code, StringComparison.Ordinal);
        Assert.Contains("_ = OnPointerMovedAsync(e);", code, StringComparison.Ordinal);
        Assert.Contains("private async Task OnPointerMovedAsync(PointerEventArgs e)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private async void OnPointerMoved(object? _, PointerEventArgs e)", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnPointerMoved\"", code, StringComparison.Ordinal);
        Assert.Contains("TrackFlowDoctorService.Instance.Diagnose(\"Súprava\", $\"Spracovanie pointer move pri drag-u zlyhalo:", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_DalsieDragLifecycleCatchBlokyUzNepohlcujuVynimkyPotichu()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnDragOver.Indicators\"", code, StringComparison.Ordinal);
        Assert.Contains("Aktualizácia drag indikátorov zlyhala", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnDragLeave.CursorReset\"", code, StringComparison.Ordinal);
        Assert.Contains("Reset kurzora pri drag-leave zlyhal", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnDragLeave.IndicatorsReset\"", code, StringComparison.Ordinal);
        Assert.Contains("Reset drag indikátorov zlyhal", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnPointerReleased.PointerCaptureRelease\"", code, StringComparison.Ordinal);
        Assert.Contains("Uvoľnenie pointer capture zlyhalo", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnPointerReleased.CursorReset\"", code, StringComparison.Ordinal);
        Assert.Contains("Reset kurzora pri pointer release zlyhal", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_PointerCaptureACursorHelperCatchBlokyMajuExceptionReporting()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnPointerPressed.LocoPointerCapture\"", code, StringComparison.Ordinal);
        Assert.Contains("Zachytenie pointera pre lokomotívu zlyhalo", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnPointerPressed.WagonPointerCapture\"", code, StringComparison.Ordinal);
        Assert.Contains("Zachytenie pointera pre vagón zlyhalo", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnPointerMoved.LocoDrag.CursorSet\"", code, StringComparison.Ordinal);
        Assert.Contains("Nastavenie drag kurzora pre lokomotívu zlyhalo", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnPointerMoved.LocoDrag.CursorReset\"", code, StringComparison.Ordinal);
        Assert.Contains("Reset drag kurzora pre lokomotívu zlyhal", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnPointerMoved.WagonDrag.CursorSet\"", code, StringComparison.Ordinal);
        Assert.Contains("Nastavenie drag kurzora pre vagón zlyhalo", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnPointerMoved.WagonDrag.CursorReset\"", code, StringComparison.Ordinal);
        Assert.Contains("Reset drag kurzora pre vagón zlyhal", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_OnDropARenameActivateCatchBlokyMajuExceptionReporting()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnDrop.CursorReset\"", code, StringComparison.Ordinal);
        Assert.Contains("Reset kurzora po drop-e zlyhal", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnDrop.IndicatorsReset\"", code, StringComparison.Ordinal);
        Assert.Contains("Reset drag indikátorov po drop-e zlyhal", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OnRenameMenuClick.ActivateExistingWindow\"", code, StringComparison.Ordinal);
        Assert.Contains("Aktivácia existujúceho rename okna zlyhala", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_RenameMenuNepouzivaAsyncDispatcherLambduAMaExceptionReporting()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.DoesNotContain("Dispatcher.UIThread.Post(async () =>", code, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post(() => _ = OpenRenameMenuAsync(), DispatcherPriority.ApplicationIdle);", code, StringComparison.Ordinal);
        Assert.Contains("private async Task OpenRenameMenuAsync()", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.OpenRenameMenuAsync\"", code, StringComparison.Ordinal);
        Assert.Contains("Otvorenie rename okna zlyhalo", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_ShowPropertiesNepouzivaAsyncRelayCommandLambduAMaExceptionReporting()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.DoesNotContain("var showProps = new RelayCommand<object?>(async param =>", code, StringComparison.Ordinal);
        Assert.Contains("var showProps = new RelayCommand<object?>(param => _ = ShowPropertiesAsync(param));", code, StringComparison.Ordinal);
        Assert.Contains("private async Task ShowPropertiesAsync(object? param)", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.ShowPropertiesAsync\"", code, StringComparison.Ordinal);
        Assert.Contains("Otvorenie vlastností vozidla zlyhalo", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_DetachThisWagonHandlerUzNepohlcujeVynimkyPotichu()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("Program.ReportUnhandledException(\"VehicleStripItem.HandleDetachThisWagonMenuClick\"", code, StringComparison.Ordinal);
        Assert.Contains("Odpojenie konkrétneho vagóna zlyhalo", code, StringComparison.Ordinal);
        Assert.DoesNotContain("catch\r\n        {\r\n            // potlačiť chyby v UI handleri\r\n        }", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_LifecycleHandleryPouzivajuPomenovaneMetodyADeterministickyCleanup()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.DoesNotContain("this.DataContextChanged += (_, _) =>", code, StringComparison.Ordinal);
        Assert.DoesNotContain("this.AttachedToVisualTree += (_, _) =>", code, StringComparison.Ordinal);
        Assert.DoesNotContain("this.DetachedFromVisualTree += (_, _) =>", code, StringComparison.Ordinal);
        Assert.Contains("this.DataContextChanged += OnDataContextChanged;", code, StringComparison.Ordinal);
        Assert.Contains("this.AttachedToVisualTree += OnAttachedToVisualTree;", code, StringComparison.Ordinal);
        Assert.Contains("this.DetachedFromVisualTree += OnDetachedFromVisualTree;", code, StringComparison.Ordinal);
        Assert.Contains("private bool _isInVisualTree;", code, StringComparison.Ordinal);
        Assert.Contains("private void OnDataContextChanged(object? _, EventArgs __)", code, StringComparison.Ordinal);
        Assert.Contains("private void OnAttachedToVisualTree(object? _, VisualTreeAttachmentEventArgs __)", code, StringComparison.Ordinal);
        Assert.Contains("private void OnDetachedFromVisualTree(object? _, VisualTreeAttachmentEventArgs __)", code, StringComparison.Ordinal);
        Assert.Contains("DetachFromCurrentLoco();", code, StringComparison.Ordinal);
        Assert.Contains("AttachToCurrentLoco();", code, StringComparison.Ordinal);
        Assert.Contains("private void AttachToCurrentLoco()", code, StringComparison.Ordinal);
        Assert.Contains("private void DetachFromCurrentLoco()", code, StringComparison.Ordinal);
        Assert.Contains("previousSettings.AppSettingsChanged -= OnAppSettingsChanged;", code, StringComparison.Ordinal);
        Assert.Contains("_settings.AppSettingsChanged -= OnAppSettingsChanged;", code, StringComparison.Ordinal);
        Assert.Contains("_settings.AppSettingsChanged += OnAppSettingsChanged;", code, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post(RefreshVisibleWagonsLimit);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_WagonCommandyPouzivajuPomenovaneHelperyNamiestoInlineLambd()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("var detach = new RelayCommand(DetachLastWagon, () => HasWagons);", code, StringComparison.Ordinal);
        Assert.Contains("var clear = new RelayCommand(ClearAttachedWagons, () => HasWagons);", code, StringComparison.Ordinal);
        Assert.Contains("private void DetachLastWagon()", code, StringComparison.Ordinal);
        Assert.Contains("private void ClearAttachedWagons()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("public System.Windows.Input.ICommand RenameTrainCommand { get; }", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RenameTrainCommand = rename;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("NotifyPropertyChanged(nameof(RenameTrainCommand));", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (RenameTrainCommand is RelayCommand renameRc)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("public System.Windows.Input.ICommand DetachThisWagonCommand { get; }", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DetachThisWagonCommand = new RelayCommand<Wagon>(HandleDetachThisWagonCommand);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private void HandleDetachThisWagonCommand(Wagon? wagon)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerPressedCommandProperty", code, StringComparison.Ordinal);
        Assert.DoesNotContain("public System.Windows.Input.ICommand? PointerPressedCommand", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_LocomotivePropertyChangedUzNeobsahujeRedundantnyNullGuardNaEventArgs()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("private void Locomotive_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (e == null) return;", code, StringComparison.Ordinal);
        Assert.Contains("if (e.PropertyName == nameof(TrackFlow.Models.Locomotive.IsActive))", code, StringComparison.Ordinal);
        Assert.Contains("if (e.PropertyName == nameof(TrackFlow.Models.Locomotive.IsFlipped))", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_DragEnterJeNaviazanyPriamoNaOnDragOverBezForwardingHandlera()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));
        var xaml = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml"));

        Assert.Contains("DragDrop.DragEnter=\"OnDragOver\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DragDrop.DragEnter=\"OnDragEnter\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("private void OnDragEnter(object? sender, DragEventArgs e)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_UzNeobsahujeNepouzityObjectModelUsingAniMrtvyRightCountAleZachovavaDropCommandFallback()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.DoesNotContain("using System.Collections.ObjectModel;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("var rightCount = totalWagons - locoPos;", code, StringComparison.Ordinal);
        Assert.Contains("public static readonly StyledProperty<System.Windows.Input.ICommand?> DropCommandProperty =", code, StringComparison.Ordinal);
        Assert.Contains("public System.Windows.Input.ICommand? DropCommand", code, StringComparison.Ordinal);
        Assert.Contains("var cmd = GetValue(DropCommandProperty) as System.Windows.Input.ICommand;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_HelperPreObnovuDisplayNamePouzivaStylistickySpravneUiVMeveMetody()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("private void UpdateDisplayNameInUi()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private void UpdateDisplayNameInUI()", code, StringComparison.Ordinal);
        Assert.Contains("UpdateDisplayNameInUi();", code, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateDisplayNameInUI();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_AnimacneHelperyUzNeobsahujuRedundantneNullGuardyNaNonNullBorderParametri()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("private async Task AnimateActivateAsync(Border border)", code, StringComparison.Ordinal);
        Assert.Contains("private async Task AnimateDeactivateAsync(Border border)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (border == null) return;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_WagonsRightUzNepouzivaRedundantnyLeftCountAliasAleZachovavaProporcnyLimit()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.DoesNotContain("var leftCount = locoPos;", code, StringComparison.Ordinal);
        Assert.Contains("var leftLimit = locoPos > 0 ? Math.Max(1, (int)Math.Ceiling((double)AttachedPreviewLimit * locoPos / totalWagons)) : 0;", code, StringComparison.Ordinal);
        Assert.Contains("var rightLimit = Math.Max(1, AttachedPreviewLimit - leftLimit);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_AnimacneSekvenciePouzivajuImplicitneVytvoreniePolaNamiestoRedundantnehoDoubleArray()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("var seq = new[] { 0.96, 1.06, 1.0 };", code, StringComparison.Ordinal);
        Assert.Contains("var seq = new[] { 0.98, 1.0 };", code, StringComparison.Ordinal);
        Assert.DoesNotContain("var seq = new double[] { 0.96, 1.06, 1.0 };", code, StringComparison.Ordinal);
        Assert.DoesNotContain("var seq = new double[] { 0.98, 1.0 };", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_OnDropFallbackParamPouzivaImplicitnePoleAZachovavaDropCommandVetvu()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("var param = new[] { (object?)target, wagon };", code, StringComparison.Ordinal);
        Assert.DoesNotContain("var param = new object?[] { target, wagon };", code, StringComparison.Ordinal);
        Assert.Contains("if (cmd.CanExecute(param))", code, StringComparison.Ordinal);
        Assert.Contains("cmd.Execute(param);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_DetachSpecificWagonUzNeobsahujeRedundantnyNullGuardNaNonNullParametri()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("private void DetachSpecificWagon(Wagon wagon)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (wagon == null) return;", code, StringComparison.Ordinal);
        Assert.Contains("DetachSpecificWagon(wagon);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_OnPointerMovedNullWagonVetvaUzNerobiDvojiteVynulovaniePendingWagon()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("var wagon = _pendingWagon;", code, StringComparison.Ordinal);
        Assert.Contains("_pendingWagon = null;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (wagon == null)\r\n        {\r\n            _isPointerDown = false;\r\n            _pendingWagon = null;", code, StringComparison.Ordinal);
        Assert.Contains("if (wagon == null)", code, StringComparison.Ordinal);
        Assert.Contains("_dragStarted = false;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_ShowOverflowIndicatorPouzivaJedenNullSafeVyrazNamiestoDvojkrokovehoGuardu()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("public bool ShowOverflowIndicator => (_currentLoco?.AttachedWagons.Count ?? 0) > AttachedPreviewLimit && AttachedPreviewLimit < int.MaxValue;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_currentLoco == null) return false;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("return _currentLoco.AttachedWagons.Count > AttachedPreviewLimit && AttachedPreviewLimit < int.MaxValue;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_DetachLastWagonUzNeobsahujeRedundantnyRemovedNullGuardPredNavratomDoDepa()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("var removed = _currentLoco.AttachedWagons[idx];", code, StringComparison.Ordinal);
        Assert.Contains("if (svm != null)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (svm != null && removed != null)", code, StringComparison.Ordinal);
        Assert.Contains("svm.DepotWagons.Add(removed);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_AttachedOverflowTextPouzivaExpressionBodiedNullSafeVyrazNamiestoBlokovehoGettera()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("public string AttachedOverflowText => ((_currentLoco?.AttachedWagons.Count ?? 0) - AttachedPreviewLimit) > 0 ? $\"+{(_currentLoco?.AttachedWagons.Count ?? 0) - AttachedPreviewLimit}\" : string.Empty;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("public string AttachedOverflowText\r\n    {\r\n        get\r\n        {", code, StringComparison.Ordinal);
        Assert.DoesNotContain("var extra = (_currentLoco?.AttachedWagons.Count ?? 0) - AttachedPreviewLimit;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("return extra > 0 ? $\"+{extra}\" : string.Empty;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_ShowPropertiesAsyncZdielaJedenMainVmNullGuardPreLocomotiveAWagonVetvu()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("if ((param is Locomotive || param is Wagon) && mainVm == null)", code, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(code, "mainVm == null"));
        Assert.DoesNotContain("if (mainVm == null) return;", code, StringComparison.Ordinal);
        Assert.Contains("var resolvedMainVm = mainVm!;", code, StringComparison.Ordinal);
        Assert.Contains("resolvedMainVm.SettingsManager", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new TrackFlow.ViewModels.Library.LocomotivesWindowViewModel(mainVm.SettingsManager", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new TrackFlow.ViewModels.Library.VagonsWindowViewModel(mainVm.SettingsManager)", code, StringComparison.Ordinal);
        Assert.Contains("if (param is Locomotive)", code, StringComparison.Ordinal);
        Assert.Contains("if (param is Wagon)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_UpdateDisplayNameInUiPouzivaNullConditionalAktualizaciuTextBlockuBezLokalnejPremennej()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("this.FindControl<TextBlock>(\"DisplayNameTextBlock\")?.SetCurrentValue(TextBlock.TextProperty, DisplayName);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (this.FindControl<TextBlock>(\"DisplayNameTextBlock\") is TextBlock tb)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("tb.Text = DisplayName;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_DisplayNamePouzivaNullCoalescingVyrazNamiestoTernarnehoTvaru()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("public string DisplayName => _currentLoco?.DisplayName ?? Title ?? string.Empty;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("public string DisplayName => _currentLoco != null ? _currentLoco.DisplayName : (Title ?? string.Empty);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_AttachedWagonsPouzivaNullCoalescingVyrazNamiestoTernarnehoCastu()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("public System.Collections.IEnumerable AttachedWagons => (System.Collections.IEnumerable?)_currentLoco?.AttachedWagons ?? Array.Empty<Wagon>();", code, StringComparison.Ordinal);
        Assert.DoesNotContain("public System.Collections.IEnumerable AttachedWagons => _currentLoco != null ? (System.Collections.IEnumerable)_currentLoco.AttachedWagons : Array.Empty<Wagon>();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_ShowOverflowIndicatorPouzivaExpressionBodiedTvarNamiestoBlokovehoGettera()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("public bool ShowOverflowIndicator => (_currentLoco?.AttachedWagons.Count ?? 0) > AttachedPreviewLimit && AttachedPreviewLimit < int.MaxValue;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("public bool ShowOverflowIndicator\r\n    {\r\n        get\r\n        {", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_SettingsFieldUzNepouzivaRedundantnePlneKvalifikovanyTyp()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("private SettingsManager? _settings;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private TrackFlow.Services.SettingsManager? _settings;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_OnFlipOrientationMenuClickPouzivaJednorazovoVyhodnotenyLocoAlias()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("var loco = _currentLoco ??= DataContext as Locomotive;", code, StringComparison.Ordinal);
        Assert.Contains("if (loco == null)", code, StringComparison.Ordinal);
        Assert.Contains("loco.IsFlipped = !loco.IsFlipped;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private void OnFlipOrientationMenuClick(object? sender, RoutedEventArgs e)\r\n    {\r\n        if (_currentLoco == null)\r\n            _currentLoco = DataContext as Locomotive;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_currentLoco.IsFlipped = !_currentLoco.IsFlipped;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_UiHandlerySNepouzitymiParametramiPouzivajuDiscardNazvy()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("private void OnFlipOrientationMenuClick(object? _, RoutedEventArgs __)", code, StringComparison.Ordinal);
        Assert.Contains("private void OnDragOver(object? _, DragEventArgs e)", code, StringComparison.Ordinal);
        Assert.Contains("private void OnDragLeave(object? _, DragEventArgs __)", code, StringComparison.Ordinal);
        Assert.Contains("private void OnPointerPressed(object? _, PointerPressedEventArgs e)", code, StringComparison.Ordinal);
        Assert.Contains("private void OnPointerMoved(object? _, PointerEventArgs e)", code, StringComparison.Ordinal);
        Assert.Contains("private void OnPointerReleased(object? _, PointerReleasedEventArgs e)", code, StringComparison.Ordinal);
        Assert.Contains("private void OnDrop(object? _, DragEventArgs e)", code, StringComparison.Ordinal);
        Assert.Contains("private void OnRenameMenuClick(object? _, RoutedEventArgs __)", code, StringComparison.Ordinal);
        Assert.Contains("private void OnDetachFirstWagonMenuClick(object? _, Avalonia.Interactivity.RoutedEventArgs __)", code, StringComparison.Ordinal);
        Assert.Contains("public void HandleDetachThisWagonMenuClick(object? sender, RoutedEventArgs __)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_DragStateFieldyUzNepouzivajuRedundantneDefaultInicializacie()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("private bool _isPointerDown;", code, StringComparison.Ordinal);
        Assert.Contains("private bool _dragStarted;", code, StringComparison.Ordinal);
        Assert.Contains("private Wagon? _pendingWagon;", code, StringComparison.Ordinal);
        Assert.Contains("private Locomotive? _pendingLoco; // Pre drag lokomotív", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool _isPointerDown = false;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool _dragStarted = false;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private Wagon? _pendingWagon = null;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private Locomotive? _pendingLoco = null; // Pre drag lokomotív", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_OnPointerMovedWagonDragCleanupUzNerobiDruheZbytocneNulovaniePendingWagonNaKonciVetvy()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("var wagon = _pendingWagon;", code, StringComparison.Ordinal);
        Assert.Contains("_pendingWagon = null;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("// obnoviť stav\r\n        _isPointerDown = false;\r\n        _pendingWagon = null;\r\n        _dragStarted = false;", code, StringComparison.Ordinal);
        Assert.Contains("// obnoviť stav", code, StringComparison.Ordinal);
        Assert.Contains("_isPointerDown = false;", code, StringComparison.Ordinal);
        Assert.Contains("_dragStarted = false;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_DalsiBatchCleanupovZjednodusujeDepotRemoveAResolveLokomotivyPreDetachHandlery()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("svm.DepotWagons.Remove(wagon);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (svm.DepotWagons.Contains(wagon))", code, StringComparison.Ordinal);

        Assert.Contains("var loco = _currentLoco ??= DataContext as Locomotive;", code, StringComparison.Ordinal);
        Assert.Contains("if (loco == null) return;", code, StringComparison.Ordinal);
        Assert.Contains("if (loco.AttachedWagons.Count == 0) return;", code, StringComparison.Ordinal);
        Assert.Contains("var idx = loco.LocoPosition > 0 ? loco.LocoPosition - 1 : 0;", code, StringComparison.Ordinal);
        Assert.Contains("var wagon = loco.AttachedWagons[idx];", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_currentLoco == null)\r\n            _currentLoco = DataContext as Locomotive;\r\n        if (_currentLoco == null) return;", code, StringComparison.Ordinal);

        Assert.Contains("Wagon? wagon = mi?.DataContext as Wagon;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Wagon? wagon = null;\r\n            if (mi != null)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_DetachBatchOdstranujeNedostupnyIdxCheckThisDataContextAOpakovaneCitanieCurrentLoco()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("if (idx >= loco.AttachedWagons.Count) return;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (idx < 0 || idx >= loco.AttachedWagons.Count) return;", code, StringComparison.Ordinal);

        Assert.Contains("wagon = DataContext as Wagon;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("wagon = this.DataContext as Wagon;", code, StringComparison.Ordinal);

        Assert.Contains("var loco = _currentLoco;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_currentLoco == null) return;", code, StringComparison.Ordinal);
        Assert.Contains("var idx = loco.AttachedWagons.IndexOf(wagon);", code, StringComparison.Ordinal);
        Assert.Contains("loco.AttachedWagons.RemoveAt(idx);", code, StringComparison.Ordinal);
        Assert.Contains("if (loco.AttachedWagons.Count == 0)", code, StringComparison.Ordinal);
        Assert.Contains("loco.TrainName = null;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void VehicleStripItem_NullHandlingBatchPouzivaNotNullPatternyPreIndikatoryARenameDialog()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Shared", "VehicleStripItem.axaml.cs"));

        Assert.Contains("if (left is not null) left.Opacity = 0;", code, StringComparison.Ordinal);
        Assert.Contains("if (right is not null) right.Opacity = 0;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("var left = this.FindControl<Rectangle>(\"LeftIndicator\");\r\n                var right = this.FindControl<Rectangle>(\"RightIndicator\");\r\n                if (left != null) left.Opacity = 0;\r\n                if (right != null) right.Opacity = 0;", code, StringComparison.Ordinal);

        Assert.Contains("if (dlg.FindControl<TextBox>(\"NameTextBox\") is { } ntb)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (dlg.FindControl<TextBox>(\"NameTextBox\") is TextBox ntb)", code, StringComparison.Ordinal);
        Assert.Contains("if (pLoco is not null) pLoco.Name = newName;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (pLoco != null) pLoco.Name = newName;", code, StringComparison.Ordinal);
    }


    [Fact]
    public void App_RegistrujeAvaloniaUiHandlerNeobsluzenychVynimiek()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("App.axaml.cs"));

        Assert.Contains("RegisterGlobalExceptionHandlers();", code, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;", code, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void LocomotivesWindow_ChartPointerHandleryChytajuVynimkyALenPriDraguZachytavajuKurzor()
    {
        var code = File.ReadAllText(GetWorkspaceFilePath("Views", "Library", "LocomotiveCalibrationWindow.axaml.cs"));
        var xaml = File.ReadAllText(GetWorkspaceFilePath("Views", "Library", "LocomotiveCalibrationWindow.axaml"));

        Assert.Contains("ForwardSpeedProfileChartInteractionCanvas", xaml, StringComparison.Ordinal);
        Assert.Contains("FindControl<Canvas>(\"ForwardSpeedProfileChartInteractionCanvas\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("BackwardSpeedProfileChartInteractionCanvas", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BackwardSpeedProfileChartInteractionCanvas", code, StringComparison.Ordinal);
        Assert.Contains("if (_vm.IsDraggingChartPoint)", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"LocomotiveCalibrationWindow.OnSpeedChartPointerPressed\"", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"LocomotiveCalibrationWindow.OnSpeedChartPointerMoved\"", code, StringComparison.Ordinal);
        Assert.Contains("Program.ReportUnhandledException(\"LocomotiveCalibrationWindow.OnSpeedChartPointerReleased\"", code, StringComparison.Ordinal);
        Assert.Contains("new ConfirmDialog(", code, StringComparison.Ordinal);
        Assert.Contains("Naozaj chcete inicializovať profil? Všetky doteraz namerané RAW dáta pre oba smery budú vymazané.", code, StringComparison.Ordinal);
        Assert.Contains("LocomotiveCalibrationWindow.OnInitializeProfileClick", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DoctorWindow_HlavickaPouzivaKrizNamiestoFonendoskopu()
    {
        var xaml = File.ReadAllText(GetWorkspaceFilePath("Views", "DoctorWindow.axaml"));

        Assert.Contains("Text=\"✚\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("🩺 On-line diagnostika", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"On-line diagnostika\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_PouzivaKrizAjNaTlacidlePreOtvorenieDoktora()
    {
        var xaml = File.ReadAllText(GetWorkspaceFilePath("Views", "MainWindow.axaml"));

        Assert.Contains("Text=\"✚\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"🩺\"", xaml, StringComparison.Ordinal);
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

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static LocomotiveSpeedEditorViewModel CreateSpeedEditorForDiagnostics(double forwardSpeed, double backwardSpeed)
        => CreateSpeedEditorForDiagnostics((10, forwardSpeed, backwardSpeed));

    private static LocomotiveSpeedEditorViewModel CreateAndSaveSpeedEditorForDiagnostics(double forwardSpeed, double backwardSpeed)
    {
        var viewModel = CreateSpeedEditorForDiagnostics(forwardSpeed, backwardSpeed);
        viewModel.SelectedProfileTabIndex = 0;
        viewModel.PersistProfileChanges = () => true;
        viewModel.SaveProfileCommand.Execute(null);
        return viewModel;
    }

    private static LocomotiveSpeedEditorViewModel CreateSpeedEditorForDiagnostics(params (int Step, double ForwardSpeed, double BackwardSpeed)[] points)
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        ReplaceDiagnosticsPoints(locomotive, points);

        var viewModel = new LocomotiveSpeedEditorViewModel();
        viewModel.SyncLocomotives(new[] { locomotive }, locomotive);
        return viewModel;
    }

    private static LocoRecord CreateLocomotiveWithDiagnosticsPoints()
    {
        var locomotive = new LocoRecord { Id = "loco-1", Name = "Brejlovec", Number = "754" };
        ReplaceDiagnosticsPoints(locomotive, (10, 20.0, 18.0));

        return locomotive;
    }

    private static void ReplaceDiagnosticsPoints(LocoRecord locomotive, params (int Step, double ForwardSpeed, double BackwardSpeed)[] points)
    {
        locomotive.ForwardSpeedProfilePoints.Clear();
        locomotive.BackwardSpeedProfilePoints.Clear();

        foreach (var point in points)
        {
            locomotive.ForwardSpeedProfilePoints.Add(new LocoSpeedProfilePoint
            {
                Step = point.Step,
                Direction = "Dopredu",
                CalculatedSpeedKmh = point.ForwardSpeed,
                RawSpeedKmh = point.ForwardSpeed,
                TimeSeconds = 1,
                Status = "Automatika"
            });
            locomotive.BackwardSpeedProfilePoints.Add(new LocoSpeedProfilePoint
            {
                Step = point.Step,
                Direction = "Dozadu",
                CalculatedSpeedKmh = point.BackwardSpeed,
                RawSpeedKmh = point.BackwardSpeed,
                TimeSeconds = 1,
                Status = "Automatika"
            });
        }
    }

    private static void AssertDiagnosticTextContainsAny(LocomotiveSpeedEditorViewModel viewModel, params string[] expectedFragments)
    {
        var combinedText = string.Join("\n", new[]
        {
            viewModel.AnalysisSummaryText,
            viewModel.AiRecommendationText,
            viewModel.RecommendedCvTweaksText
        });

        Assert.True(
            expectedFragments.Any(fragment => combinedText.Contains(fragment, StringComparison.OrdinalIgnoreCase)),
            $"Text diagnostiky neobsahuje žiadny z očakávaných fragmentov ({string.Join(", ", expectedFragments)}). Aktuálny text: {combinedText}");
    }
}