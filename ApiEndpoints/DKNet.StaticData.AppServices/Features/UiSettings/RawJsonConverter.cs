using System.Text.Json;

namespace DKNet.StaticData.AppServices.Features.UiSettings;

/// <summary>
///     Reads any JSON value as its exact text and writes that text back unchanged (spec DRK-2245 §3, brief DRK-2246 §9
///     Q2): the same spaces, key order, number text such as <c>1.0</c> and unescaped letters such as <c>é</c>. A JSON
///     <c>null</c> reads as <see langword="null" />, never as the text <c>null</c>.
/// </summary>
internal sealed class RawJsonConverter : JsonConverter<string>
{
    #region Methods

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var value = JsonDocument.ParseValue(ref reader);
        return value.RootElement.GetRawText();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value, skipInputValidation: true);

    #endregion
}
