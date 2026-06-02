namespace TrackFlow.Models;

public enum StartupFunctionBehavior
{
    SendAllFunctions       = 0,
    SendActivatedFunctions = 1,
    KeepPreviousState      = 2,
    AssumeOffState         = 3
}

