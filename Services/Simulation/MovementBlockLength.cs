using System;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services.Simulation;

/// <summary>
/// Stateless helper pre rozhodnutie efektívnej dĺžky bloku v milimetroch
/// (simulačný režim vs. reálna geometria projektu).
/// Mechanická 1:1 extrakcia z OperationViewModel (behavior-preserving).
/// </summary>
internal static class MovementBlockLength
{
    /// <summary>
    /// Virtuálna dĺžka bloku v simulácii (mm).
    /// Lokálny stateless konštanta — žiadny shared mutable state.
    /// </summary>
    internal const double SimBlockLengthMm = 2000.0;

    public static double ResolveMovementBlockLengthMm(BlockElement block, bool isSimulationMode)
        => isSimulationMode ? SimBlockLengthMm : Math.Max(500.0, Math.Max(1, block.lengthMm) * 10.0);
}

