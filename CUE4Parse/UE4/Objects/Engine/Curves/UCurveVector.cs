using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Readers;
using Newtonsoft.Json;

namespace CUE4Parse.UE4.Objects.Engine.Curves;

public class UCurveVector : UCurveBase
{
    public readonly FRichCurve[] FloatCurves = new FRichCurve[3];

    public override void Deserialize(FAssetArchive Ar, long validPos)
    {
        base.Deserialize(Ar, validPos);

        // Channels left at their default value are not serialized, so map by ArrayIndex instead of by property order.
        foreach (var property in Properties)
        {
            if (property.Name.Text != nameof(FloatCurves) || property.ArrayIndex < 0 || property.ArrayIndex >= FloatCurves.Length)
                continue;
            if (property.Tag?.GenericValue is FScriptStruct { StructType: FStructFallback fallback })
            {
                FloatCurves[property.ArrayIndex] = new FRichCurve(fallback);
            }
        }

        for (var i = 0; i < FloatCurves.Length; i++)
        {
            FloatCurves[i] ??= new FRichCurve();
        }
    }

    protected internal override void WriteJson(JsonWriter writer, JsonSerializer serializer)
    {
        base.WriteJson(writer, serializer);

        writer.WritePropertyName("FloatCurves");
        writer.WriteStartArray();

        foreach (var richCurve in FloatCurves)
        {
            serializer.Serialize(writer, richCurve);
        }

        writer.WriteEndArray();
    }
}
