using System;
using System.Collections.Generic;
using System.Reflection;
using TrackFlow.Services;
using TrackFlow.Views;
using Xunit;

namespace TrackFlow.Tests;

public class DoctorWindowFilterTests
{
    private static readonly MethodInfo ShouldDisplayEventMethod = typeof(DoctorWindow)
        .GetMethod("ShouldDisplayEvent", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Nepodarilo sa nájsť helper ShouldDisplayEvent v DoctorWindow.");

    [Fact]
    public void KlasickyLog_ZostaneViditelnyAjPriAktivnychMultiFiltroch()
    {
        var entry = new DiagnosticEvent
        {
            Source = "Senzor",
            Message = "OBSADENÝ: blok X1",
            Level = DiagnosticLevel.Warning
        };

        var visible = InvokeShouldDisplayEvent(entry, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WAIT",
            "ARBITER",
            "DEADLOCK"
        });

        Assert.True(visible);
    }

    [Fact]
    public void MultiTag_LogSaSkryjeAkJehoFilterNieJeAktivny()
    {
        var entry = new DiagnosticEvent
        {
            Source = "Prevádzka",
            Message = "[MULTI][PAT] patová situácia",
            Level = DiagnosticLevel.Warning
        };

        var visible = InvokeShouldDisplayEvent(entry, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WAIT"
        });

        Assert.False(visible);
    }

    [Fact]
    public void MultiTag_LogZostaneViditelnyAkJehoFilterJeAktivny()
    {
        var entry = new DiagnosticEvent
        {
            Source = "Prevádzka",
            Message = "[MULTI][CAKANIE] čakanie na shared blok",
            Level = DiagnosticLevel.Warning
        };

        var visible = InvokeShouldDisplayEvent(entry, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WAIT",
            "ARBITER"
        });

        Assert.True(visible);
    }

    [Fact]
    public void NepodporovanyMultiTag_ZostaneViditelnyPretozeFiltreRiadiLenZnamyZoznamTagov()
    {
        var entry = new DiagnosticEvent
        {
            Source = "Prevádzka",
            Message = "[MULTI][UNKNOWN] diagnostika mimo UI filtrov",
            Level = DiagnosticLevel.Info
        };

        var visible = InvokeShouldDisplayEvent(entry, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WAIT"
        });

        Assert.True(visible);
    }

    [Fact]
    public void StaryAnglickyMultiTag_JeStaleRozpoznanyKvôliSpatnejKompatibilite()
    {
        var entry = new DiagnosticEvent
        {
            Source = "Prevádzka",
            Message = "[MULTI][DEADLOCK] legacy patová situácia",
            Level = DiagnosticLevel.Warning
        };

        var visible = InvokeShouldDisplayEvent(entry, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WAIT"
        });

        Assert.False(visible);
    }

    private static bool InvokeShouldDisplayEvent(DiagnosticEvent entry, IReadOnlySet<string> activeFilters)
        => (bool)(ShouldDisplayEventMethod.Invoke(null, new object[] { entry, activeFilters })
            ?? throw new InvalidOperationException("ShouldDisplayEvent vrátil null."));
}

