using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace DKNet.StaticData.App.TestSupport;

/// <summary>
/// Records every activity (trace span) the process stops while the capture is alive, from every activity source, so a
/// test can prove a value never reached a trace: its name, tags, events and baggage are all searched.
/// </summary>
public sealed class TestTraceCapture : IDisposable
{
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    public TestTraceCapture()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyCollection<Activity> Activities => _stopped.ToArray();

    /// <summary>Whether <paramref name="text"/> appears in any recorded activity.</summary>
    public bool Holds(string text) => _stopped.Any(a => Texts(a).Any(t => t.Contains(text, StringComparison.Ordinal)));

    public void Dispose() => _listener.Dispose();

    private static IEnumerable<string> Texts(Activity activity) =>
        new[] { activity.DisplayName, activity.OperationName, activity.StatusDescription }
            .Concat(activity.TagObjects.Select(t => Convert.ToString(t.Value, CultureInfo.InvariantCulture)))
            .Concat(activity.Baggage.Select(b => b.Value))
            .Concat(activity.Events.SelectMany(e => new[] { e.Name }
                .Concat(e.Tags.Select(t => Convert.ToString(t.Value, CultureInfo.InvariantCulture)))))
            .OfType<string>();
}
