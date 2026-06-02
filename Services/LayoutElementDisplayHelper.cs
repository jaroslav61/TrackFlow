using System;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services;

/// <summary>
/// Jednotné, ľudsky čitateľné pomenovania prvkov pre diagnostiku/logy.
/// </summary>
public static class LayoutElementDisplayHelper
{
    public static string GetTypeText(LayoutElement element)
    {
        // Primárne podľa MarkerKey (je stabilnejší pre UI varianty).
        return element.MarkerKey switch
        {
            "TrackSegment" => "rovná koľaj",
            "Bumper" => "zarážadlo",
            "Curve_45" => "oblúk 45°",
            "Curve_90" => "oblúk 90°",
            "Turnout_L" => "výhybka ľavá",
            "Turnout_R" => "výhybka pravá",
            "TurnoutL90" => "výhybka ľavá 90°",
            "TurnoutR90" => "výhybka pravá 90°",
            "TurnoutCurve_L" => "oblúková výhybka ľavá",
            "TurnoutCurve_R" => "oblúková výhybka pravá",
            "Turnout_Y" => "Y-výhybka",
            "Turnout_3W" => "3-cestná výhybka",
            "Cross90" => "križovatka 90°",
            "Cross45" => "križovatka 45°",
            "DoubleSlip" => "križovatková výhybka",
            "Bridge90" => "most 90°",
            "Bridge45L" => "most 45° ľavý",
            "Bridge45R" => "most 45° pravý",
            "Signal" or "Signal5" or "Signal4" or "Signal2Main" or "Signal2Shunt" or "Signal2Route" or "Signal3Entry" => "návestidlo",
            "Sensor" => "senzor",
            "Block" => "blok",
            "Route" => "cesta",
            "Text" => "text",
            _ => element.ElementType switch
            {
                LayoutElementType.Block => "blok",
                LayoutElementType.Signal => "návestidlo",
                LayoutElementType.Sensor => "senzor",
                LayoutElementType.Turnout or LayoutElementType.TurnoutCurve or LayoutElementType.TurnoutY or LayoutElementType.Turnout3W or LayoutElementType.TurnoutL90 or LayoutElementType.TurnoutR90 or LayoutElementType.DoubleSlip => "výhybka",
                LayoutElementType.TrackSegment or LayoutElementType.Curve or LayoutElementType.CurveNarrow or LayoutElementType.Cross90 or LayoutElementType.Cross45 or LayoutElementType.Bridge90 or LayoutElementType.Bridge45L or LayoutElementType.Bridge45R => "koľaj",
                _ => "prvok"
            }
        };
    }

    public static string ShortId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "";

        if (id.Length <= 16)
            return id;

        var head = id.Substring(0, 8);
        var tail = id.Substring(Math.Max(0, id.Length - 5));
        return $"{head}…{tail}";
    }

    public static string Describe(LayoutElement element, bool includeId = true)
    {
        var type = GetTypeText(element);
        var name = string.IsNullOrWhiteSpace(element.Label) ? null : element.Label.Trim();

        if (!includeId)
            return name == null ? type : $"{type} '{name}'";

        var id = ShortId(element.Id);
        return name == null
            ? $"{type} [{id}]"
            : $"{type} '{name}' [{id}]";
    }
}


