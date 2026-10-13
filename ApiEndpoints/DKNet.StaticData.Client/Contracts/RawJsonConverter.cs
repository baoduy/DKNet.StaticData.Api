using System.Text.Json;
using System.Text.Json.Serialization;

namespace DKNet.StaticData.Client.Contracts;

/// <summary>
/// Reads any JSON value as its exact text and writes that text back unchanged: the same spaces, key order, number
/// text such as <c>1.0</c> and unescaped letters such as <c>é</c>. A JSON <c>null</c> reads as <see langword="null"/>,
/// never as the text <c>null</c>. The client's own copy of the service's converter, as the client holds no server code.
/// </summary>
internal sealed class RawJsonConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonElement.ParseValue(ref reader).GetRawText();

    /// <summary>Writes <paramref name="value"/> as is; text that is not one JSON value throws <see cref="JsonException"/>.</summary>
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value);
}
