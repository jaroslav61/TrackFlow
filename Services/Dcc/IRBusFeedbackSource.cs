using System;
using TrackFlow.Models;

namespace TrackFlow.Services.Dcc;

/// <summary>
/// Klient DCC centrály, ktorý vie publikovať zmeny spätnoväzbových vstupov
/// (R-Bus / S88 occupancy feedback).
/// </summary>
public interface IRBusFeedbackSource
{
    event Action<RBusFeedbackState>? RBusFeedbackChanged;
}

/// <summary>
/// Jeden konkrétny stav vstupu spätnoväzbového modulu.
/// ModuleAddress a PortNumber sú 1-based hodnoty zodpovedajúce UI.
/// </summary>
public readonly record struct RBusFeedbackState(
    int ModuleAddress,
    int PortNumber,
    bool IsActive);

/// <summary>
/// Agregovaná zmena feedbacku na úrovni DCC connection služby.
/// Obsahuje aj kontext profilu centrály.
/// </summary>
public readonly record struct DccFeedbackStateChange(
    Guid? ProfileId,
    DccCentralType Type,
    int ModuleAddress,
    int PortNumber,
    bool IsActive);

