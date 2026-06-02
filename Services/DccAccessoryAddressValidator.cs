namespace TrackFlow.Services;

/// <summary>
/// Spoločná validácia DCC accessory adries (výhybky, signály, senzory).
/// </summary>
public static class DccAccessoryAddressValidator
{
    public const int MinAddress = 0;
    public const int MaxAddress = 2048;
    public const int FirstAssignedAddress = 1;
    public const string ValidationErrorText = "⚠ DCC adresa musí byť v rozsahu 0 – 2048 (0 = nepridelená).";

    public static bool IsValid(int address)
        => address >= MinAddress && address <= MaxAddress;

    public static bool IsAssigned(int address)
        => address >= FirstAssignedAddress && address <= MaxAddress;

    public static string GetValidationError(int address)
        => IsValid(address)
            ? string.Empty
            : ValidationErrorText;
}

