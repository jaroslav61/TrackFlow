namespace TrackFlow.Models;

/// <summary>
/// Režim aplikácie - Editor alebo Prevádzka.
/// </summary>
public enum AppMode
{
    /// <summary>Režim editácie schémy - povoliť drag&drop, rotáciu, mazanie prvkov.</summary>
    Editor,
    
    /// <summary>Režim ostrej prevádzky - blokovať editáciu, povoliť prepínanie výhybiek, ovládanie vlakov.</summary>
    Operation
}

