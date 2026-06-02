using System;
using System.Reflection;
using TrackFlow.Models.Layout;
using TrackFlow.ViewModels.Operation;
using Xunit;

namespace TrackFlow.Tests;

public class TurnoutDisplayFormattingTests
{
    private static readonly MethodInfo TurnoutStateDisplayNameMethod = typeof(OperationViewModel).Assembly
        .GetType("TrackFlow.Services.Operation.OperationDisplayHelpers", throwOnError: true)!
        .GetMethod("TurnoutStateDisplayName", BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("Nepodarilo sa nájsť helper TurnoutStateDisplayName.");

    [Theory]
    [InlineData(TurnoutState.Straight, "priamo")]
    [InlineData(TurnoutState.Diverge, "do odbočky")]
    [InlineData(TurnoutState.DivergeLeft, "do odbočky vľavo")]
    [InlineData(TurnoutState.DivergeRight, "do odbočky vpravo")]
    [InlineData(TurnoutState.Cross, "krížom")]
    public void TurnoutStateDisplayName_VratiSlovenskePouzivatelskeNazvy(TurnoutState state, string expected)
    {
        var actual = (string)(TurnoutStateDisplayNameMethod.Invoke(null, new object[] { state })
            ?? throw new InvalidOperationException("TurnoutStateDisplayName vrátil null."));

        Assert.Equal(expected, actual);
    }
}

