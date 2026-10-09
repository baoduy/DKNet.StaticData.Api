using DKNet.StaticData.Domains.Services;

namespace DKNet.StaticData.App.TestSupport;

public sealed class TestMembershipService : IMembershipService
{
    private int _current;

    public ValueTask<string> NextValueAsync()
    {
        var next = Interlocked.Increment(ref _current);
        return ValueTask.FromResult($"TEST-MEM-{next:D6}");
    }
}
