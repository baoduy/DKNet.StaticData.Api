namespace DKNet.StaticData.Client.Contracts;

/// <summary>A downloaded file: its bytes, stored name and stored content type. The caller disposes it.</summary>
public sealed class StaticDataFileContent(Stream content, string fileName, string contentType) : IAsyncDisposable, IDisposable
{
    /// <summary>The stored bytes.</summary>
    public Stream Content { get; } = content;

    /// <summary>The exact stored file name.</summary>
    public string FileName { get; } = fileName;

    /// <summary>The stored content type.</summary>
    public string ContentType { get; } = contentType;

    public void Dispose() => Content.Dispose();

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
