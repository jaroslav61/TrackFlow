using System;
using System.Collections.Generic;
using TrackFlow.Models;

namespace TrackFlow.Services.Dcc;

public static class DccCentralDisplayName
{
    // Cache názvov z katalógu, aby bolo pomenovanie rovnaké v zozname, logoch aj status bare.
    // Zároveň špeciálne rozlišujeme „Z21“ vs „z21“ (bez textu legacy).
    private static readonly Lazy<IReadOnlyDictionary<DccCentralType, string>> _nameByType =
        new(BuildMap, isThreadSafe: true);

    public static string Get(DccCentralType type)
    {
        // Roco/Fleischmann: Z21 (full) vs z21 (start) – musí byť presne veľkosť písmena.
        if (type == DccCentralType.Z21)
            return "Z21";
        if (type == DccCentralType.Z21Legacy)
            return "z21";

        if (_nameByType.Value.TryGetValue(type, out var name) && !string.IsNullOrWhiteSpace(name))
            return name;

        return type.ToString();
    }

    private static IReadOnlyDictionary<DccCentralType, string> BuildMap()
    {
        var map = new Dictionary<DccCentralType, string>();

        foreach (var g in DccCentralCatalog.GetGroups())
        {
            foreach (var it in g.Items)
            {
                // necháme katalóg ako zdroj pravdy pre "plné" názvy ostatných centrál
                if (!map.ContainsKey(it.Type))
                    map[it.Type] = it.Name;
            }
        }

        // Fallbacky (ak by katalóg nemal niektoré položky)
        if (!map.ContainsKey(DccCentralType.GenericIpUdp))
            map[DccCentralType.GenericIpUdp] = "Generic IP/UDP";

        return map;
    }
}
