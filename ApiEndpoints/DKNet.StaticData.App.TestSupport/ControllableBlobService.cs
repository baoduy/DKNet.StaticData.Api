using System.Collections.Concurrent;
using DKNet.Svc.BlobStorage.Abstractions;

namespace DKNet.StaticData.App.TestSupport;

/// <summary>
/// The switches a test flips on <see cref="ControllableBlobService"/>: make blob storage fail to save, read or delete,
/// hold a save open, and see which storage keys were saved and deleted. Every switch is off until a test turns it on.
/// </summary>
public sealed class BlobStorageControl
{
    private readonly ConcurrentQueue<string> _saved = new();
    private readonly ConcurrentQueue<string> _deleted = new();

    /// <summary>Thrown by every save while set: blob storage cannot be reached.</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>Thrown by every read while set: blob storage cannot be reached.</summary>
    public Exception? ReadFailure { get; set; }

    /// <summary>Thrown by every delete while set: blob storage fails to delete.</summary>
    public Exception? DeleteFailure { get; set; }

    /// <summary>While set, every save waits for it to open before it stores the bytes.</summary>
    public BlobGate? SaveGate { get; set; }

    /// <summary>The storage key of every save that stored its bytes.</summary>
    public IReadOnlyCollection<string> SavedKeys => _saved.ToArray();

    /// <summary>The storage key of every delete that removed its bytes.</summary>
    public IReadOnlyCollection<string> DeletedKeys => _deleted.ToArray();

    public void Reset()
    {
        SaveFailure = null;
        ReadFailure = null;
        DeleteFailure = null;
        SaveGate = null;
        _saved.Clear();
        _deleted.Clear();
    }

    internal void Saved(string key) => _saved.Enqueue(key);

    internal void Deleted(string key) => _deleted.Enqueue(key);
}

/// <summary>Holds a save open: <see cref="Entered"/> completes when a save reaches it, the save goes on once <see cref="Open"/> runs.</summary>
public sealed class BlobGate
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Entered => _entered.Task;

    public void Open() => _open.TrySetResult();

    internal async Task PassAsync(CancellationToken cancellationToken)
    {
        _entered.TrySetResult();
        await _open.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
    }
}

/// <summary>
/// Wraps the host's own <see cref="IBlobService"/> — the real local-folder provider in the file tests — and fails,
/// holds or records its calls as <see cref="BlobStorageControl"/> says. With every switch off it only passes calls
/// through, so the bytes still land in the real folder.
/// </summary>
public sealed class ControllableBlobService(IBlobService inner, BlobStorageControl control) : IBlobService
{
    /// <summary>
    /// Replaces the host's <see cref="IBlobService"/> registration with this wrapper around it, keeping its lifetime.
    /// A host that registers no <see cref="IBlobService"/> is left as it is. Call from a test factory's
    /// <c>ConfigureTestServices</c>.
    /// </summary>
    public static void Wrap(IServiceCollection services, BlobStorageControl control)
    {
        var original = services.LastOrDefault(d => d.ServiceType == typeof(IBlobService));
        if (original is null)
        {
            return;
        }

        services.Remove(original);
        services.Add(new ServiceDescriptor(
            typeof(IBlobService),
            sp => new ControllableBlobService(Create(sp, original), control),
            original.Lifetime));
    }

    public async Task<string> SaveAsync(BlobDetails.BlobData blob, CancellationToken cancellationToken = default)
    {
        await BeforeSaveAsync(cancellationToken);
        var location = await inner.SaveAsync(blob, cancellationToken);
        control.Saved(blob.Name);
        return location;
    }

    public async Task<string> SaveAsync(BlobDetails.BlobStreamData blob, CancellationToken cancellationToken = default)
    {
        await BeforeSaveAsync(cancellationToken);
        var location = await inner.SaveAsync(blob, cancellationToken);
        control.Saved(blob.Name);
        return location;
    }

    public async Task<bool> DeleteAsync(BlobRequest blob, CancellationToken cancellationToken = default)
    {
        ThrowIfSet(control.DeleteFailure);
        var deleted = await inner.DeleteAsync(blob, cancellationToken);
        control.Deleted(blob.Name);
        return deleted;
    }

    public Task<Stream?> OpenReadAsync(BlobRequest blob, CancellationToken cancellationToken = default)
    {
        ThrowIfSet(control.ReadFailure);
        return inner.OpenReadAsync(blob, cancellationToken);
    }

    public Task<BlobDetails.BlobDataResult?> GetAsync(BlobRequest blob, CancellationToken cancellationToken = default)
    {
        ThrowIfSet(control.ReadFailure);
        return inner.GetAsync(blob, cancellationToken);
    }

    public Task<BlobDetails.BlobResult?> GetItemAsync(BlobRequest blob, CancellationToken cancellationToken = default)
    {
        ThrowIfSet(control.ReadFailure);
        return inner.GetItemAsync(blob, cancellationToken);
    }

    public Task<bool> CheckExistsAsync(BlobRequest blob, CancellationToken cancellationToken = default)
    {
        ThrowIfSet(control.ReadFailure);
        return inner.CheckExistsAsync(blob, cancellationToken);
    }

    public IAsyncEnumerable<BlobDetails.BlobResult> ListItemsAsync(
        BlobRequest blob,
        CancellationToken cancellationToken = default)
    {
        ThrowIfSet(control.ReadFailure);
        return inner.ListItemsAsync(blob, cancellationToken);
    }

    public Task<Uri> GetPublicAccessUrl(
        BlobRequest blob,
        TimeSpan? expiresFromNow = null,
        CancellationToken cancellationToken = default) =>
        inner.GetPublicAccessUrl(blob, expiresFromNow, cancellationToken);

    private async Task BeforeSaveAsync(CancellationToken cancellationToken)
    {
        ThrowIfSet(control.SaveFailure);
        if (control.SaveGate is { } gate)
        {
            await gate.PassAsync(cancellationToken);
        }
    }

    private static void ThrowIfSet(Exception? failure)
    {
        if (failure is not null)
        {
            throw failure;
        }
    }

    private static IBlobService Create(IServiceProvider services, ServiceDescriptor original) =>
        original.ImplementationInstance as IBlobService
        ?? original.ImplementationFactory?.Invoke(services) as IBlobService
        ?? (IBlobService)ActivatorUtilities.CreateInstance(services, original.ImplementationType!);
}
