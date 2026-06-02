using System;
using System.Text.Json.Serialization;

namespace TrackFlow.Models.Layout;

/// <summary>
/// Abstraktný základ každého prvku koľajiska.
/// Serializácia cez System.Text.Json – polymorfizmus riešený JsonDerivedType.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TrackSegmentElement), "segment")]
[JsonDerivedType(typeof(CurveElement),        "curve")]
[JsonDerivedType(typeof(TurnoutElement),      "turnout")]
[JsonDerivedType(typeof(SignalElement),        "signal")]
[JsonDerivedType(typeof(SensorElement),        "sensor")]
[JsonDerivedType(typeof(BumperElement),        "bumper")]
[JsonDerivedType(typeof(BlockElement),         "block")]
[JsonDerivedType(typeof(RouteElement),         "route")]
[JsonDerivedType(typeof(TextElement),          "text")]
public abstract class LayoutElement
{
    /// <summary>Unikátne ID prvku v rámci rozloženia.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Pozícia X na plátne (pixely).</summary>
    public double X { get; set; }

    /// <summary>Pozícia Y na plátne (pixely).</summary>
    public double Y { get; set; }

    /// <summary>Rotácia v stupňoch (0, 45, 90, 135, 180, 225, 270, 315).</summary>
    public double Rotation { get; set; }

    /// <summary>Zobrazený názov prvku (napr. "Blok A1", "Výhybka 1").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Typ prvku – pre UI identifikáciu.</summary>
    [JsonIgnore]
    public abstract LayoutElementType ElementType { get; }

    /// <summary>Kľúč markera (napr. "Turnout_L", "Curve_45") – pre správne vykreslenie v editore.</summary>
    public string MarkerKey { get; set; } = string.Empty;

    /// <summary>
    /// Vytvorí kópiu prvku.
    /// 
    /// Defaultne generuje nové <see cref="Id"/> (používa sa pre Kopírovať/Vložiť/Duplikovať).
    /// </summary>
    public LayoutElement Clone() => CloneCore(preserveId: false);

    /// <summary>
    /// Vytvorí kópiu prvku so zachovaným <see cref="Id"/>.
    /// Užitočné pre budúce snapshoty (Vrátiť späť/Znova), kde sa ID prvkov nesmie meniť.
    /// </summary>
    public LayoutElement ClonePreserveId() => CloneCore(preserveId: true);

    private LayoutElement CloneCore(bool preserveId)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this, this.GetType());
        var clone = (LayoutElement)System.Text.Json.JsonSerializer.Deserialize(json, this.GetType())!;
        if (!preserveId)
            clone.Id = Guid.NewGuid().ToString("N");
        return clone;
    }
}
