using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace TrackFlow.Converters;

/// <summary>
/// Konvertuje IsBasicMode bool na popis režimu (text).
/// true → "Dekodér emuluje výhybky a obsadí blok 4 adries:"
/// false → "Režim DCC Extended Accessory (obsadí 1 adresu):"
/// </summary>
public class DecoderModeDescriptionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isBasic)
        {
            return isBasic 
                ? "Režim spätnej kompatibility (vyžaduje 4 adresy príslušenstva)" 
                : "Režim DCC Extended Accessory (obsadí 1 adresu):";
        }
        return "(Popis nedostupný)";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

