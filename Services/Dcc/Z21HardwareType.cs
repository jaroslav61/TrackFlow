namespace TrackFlow.Services.Dcc;

/// <summary>
/// HwType hodnoty vrátené z21 centrálou v odpovedi na LAN_GET_HWINFO.
/// Podľa oficiálnej Roco "z21 LAN Protokoll" špecifikácie.
/// </summary>
public enum Z21HardwareType : uint
{
    Unknown        = 0x00000000,
    Z21Old         = 0x00000200, // z21 (čierna, prvá generácia 2012)
    Z21New         = 0x00000201, // z21 (čierna, 2014+)
    SmartRail      = 0x00000202, // SmartRail
    Z21Small       = 0x00000203, // z21 start (biela – 10813)
    Z21Start       = 0x00000204, // z21 start
    SingleBooster  = 0x00000205,
    DualBooster    = 0x00000206,
    Z21XL          = 0x00000211,
    Z21XLBooster   = 0x00000212,
    SwitchDecoder  = 0x00000301,
    SignalDecoder  = 0x00000302,
}

public static class Z21HardwareTypeExtensions
{
    /// <summary>
    /// True pre plnohodnotné čierne Z21 centrály so samostatnou programovacou koľajou.
    /// False pre z21 start / malé varianty, boostery a neznáme jednotky.
    /// </summary>
    public static bool IsBlackZ21(this Z21HardwareType hwType) => hwType switch
    {
        Z21HardwareType.Z21Old => true,
        Z21HardwareType.Z21New => true,
        Z21HardwareType.Z21XL  => true,
        _                      => false,
    };

    /// <summary>
    /// Ľudsky čitateľný popis HwType (alebo "0xXXXXXXXX" pre neznámu hodnotu).
    /// </summary>
    public static string ToDisplayName(this Z21HardwareType hwType) => hwType switch
    {
        Z21HardwareType.Z21Old        => "z21 (čierna, 2012)",
        Z21HardwareType.Z21New        => "z21 (čierna, 2014+)",
        Z21HardwareType.SmartRail     => "SmartRail",
        Z21HardwareType.Z21Small      => "z21 start",
        Z21HardwareType.Z21Start      => "z21 start",
        Z21HardwareType.SingleBooster => "z21 Single Booster",
        Z21HardwareType.DualBooster   => "z21 Dual Booster",
        Z21HardwareType.Z21XL         => "Z21 XL",
        Z21HardwareType.Z21XLBooster  => "Z21 XL Booster",
        Z21HardwareType.SwitchDecoder => "z21 Switch Decoder",
        Z21HardwareType.SignalDecoder => "z21 Signal Decoder",
        Z21HardwareType.Unknown       => "neznáma centrála",
        _                             => $"0x{(uint)hwType:X8}"
    };

    /// <summary>
    /// Vracia true ak daná centrála podporuje service-mode CV programovanie (Direct Mode).
    /// POZN.: z21 start síce nemá samostatný výstup PROG, ale podporuje Service Mode príkazy
    /// tak, že na chvíľu prepne hlavný výstup do programovacieho režimu (rovnako ako
    /// TrainController v režime „Main Track“). Preto sa správa ako podporujúca.
    /// False vracajú len jednotky, ktoré nemajú vlastný DCC generátor (boostery, decoderové
    /// moduly).
    /// </summary>
    public static bool SupportsServiceModeProgramming(this Z21HardwareType hwType) => hwType switch
    {
        // Bez vlastného DCC generátora – service-mode tu nedáva zmysel.
        Z21HardwareType.SingleBooster => false,
        Z21HardwareType.DualBooster   => false,
        Z21HardwareType.Z21XLBooster  => false,
        Z21HardwareType.SwitchDecoder => false,
        Z21HardwareType.SignalDecoder => false,

        // Plnohodnotné z21 centrály – samostatný výstup PROG.
        Z21HardwareType.Z21Old        => true,
        Z21HardwareType.Z21New        => true,
        Z21HardwareType.Z21XL         => true,

        // z21 start / SmartRail – nemajú samostatný PROG výstup, ale Service Mode príkazy
        // (LAN_X_CV_READ / WRITE) zvládajú cez krátkodobé prepnutie hlavnej trate.
        Z21HardwareType.Z21Small      => true,
        Z21HardwareType.Z21Start      => true,
        Z21HardwareType.SmartRail     => true,

        // Pri neznámej hodnote radšej dovolíme pokus – aspoň uvidíme reálnu odpoveď.
        _                             => true,
    };
}

