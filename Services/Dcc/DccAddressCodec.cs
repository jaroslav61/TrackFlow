using System;

namespace TrackFlow.Services.Dcc;

public static class DccAddressCodec
{
    public const int Cv29LongAddressBit = 0x20;
    public const int MinShortAddress = 1;
    public const int MaxShortAddress = 127;
    public const int MinLongAddress = 128;
    public const int MaxLongAddress = 9999;

    public static bool UsesLongAddress(int cv29Value) => (cv29Value & Cv29LongAddressBit) != 0;

    public static int SetLongAddressFlag(int cv29Value, bool useLongAddress)
        => useLongAddress
            ? cv29Value | Cv29LongAddressBit
            : cv29Value & ~Cv29LongAddressBit;

    public static bool IsSupportedAddress(int address)
        => address >= MinShortAddress && address <= MaxLongAddress;

    public static bool IsShortAddress(int address)
        => address >= MinShortAddress && address <= MaxShortAddress;

    public static bool IsLongAddress(int address)
        => address >= MinLongAddress && address <= MaxLongAddress;

    /// <summary>
    /// Zakóduje DCC adresu lokomotívy (1..9999) do dvojice bajtov pre Z21 / XpressNet
    /// protokol (LAN_X_SET_LOCO_DRIVE, LAN_X_SET_LOCO_FUNCTION, LAN_X_CV_POM_*).
    /// Krátka adresa (1..127): Hi=0x00, Lo=adresa.
    /// Dlhá adresa (128..9999): Hi=0xC0 | (adresa &gt;&gt; 8 &amp; 0x3F), Lo=adresa &amp; 0xFF.
    /// </summary>
    public static (byte Hi, byte Lo) EncodeLocoAddress(int address)
    {
        if (address < MinShortAddress || address > MaxLongAddress)
            throw new ArgumentOutOfRangeException(nameof(address), $"DCC adresa lokomotívy musí byť v rozsahu {MinShortAddress}..{MaxLongAddress}.");

        byte hi = address > MaxShortAddress
            ? (byte)(0xC0 | ((address >> 8) & 0x3F))
            : (byte)0x00;
        byte lo = (byte)(address & 0xFF);
        return (hi, lo);
    }

    public static (byte Cv17, byte Cv18) EncodeLongAddress(int address)
    {
        if (!IsLongAddress(address))
            throw new ArgumentOutOfRangeException(nameof(address), $"Dlhá DCC adresa musí byť v rozsahu {MinLongAddress}..{MaxLongAddress}.");

        byte cv17 = (byte)(0xC0 | ((address >> 8) & 0x3F));
        byte cv18 = (byte)(address & 0xFF);
        return (cv17, cv18);
    }

    public static int DecodeLongAddress(int cv17, int cv18)
    {
        if (cv17 < 0 || cv17 > 255)
            throw new ArgumentOutOfRangeException(nameof(cv17));
        if (cv18 < 0 || cv18 > 255)
            throw new ArgumentOutOfRangeException(nameof(cv18));
        if ((cv17 & 0xC0) != 0xC0)
            throw new ArgumentOutOfRangeException(nameof(cv17), "CV17 neobsahuje NMRA prefix pre dlhú adresu.");

        int address = ((cv17 & 0x3F) << 8) | cv18;
        if (!IsLongAddress(address))
            throw new InvalidOperationException($"Dekódovaná dlhá adresa {address} nie je v rozsahu {MinLongAddress}..{MaxLongAddress}.");

        return address;
    }
}


