using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.UiSettings.Support;

/// <summary>
/// "During" and "at the same moment" races of spec DRK-2245 §5, from <see cref="FilesApiFactory.BeforeSave"/>: after a
/// call's checks and before its save. The other call runs through the service's own route.
/// </summary>
internal static class SaveRaces
{
    /// <summary>
    /// Sends <paramref name="call"/> just before the service's next save and returns its answer. The answer task fails
    /// when no save comes within 30 seconds, so a call that never reached its save is reported by its own answer first.
    /// </summary>
    public static Task<HttpResponseMessage> DuringTheNextSave(FilesApi api, Func<Task<HttpResponseMessage>> call)
    {
        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Host.BeforeSave.Run(async () => answer.SetResult(await call()));
        return answer.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Holds each of the next 2 saves just before it runs until both have reached it, so both calls have made their
    /// checks before either saves.
    /// </summary>
    public static void HoldBothSavesTogether(FilesApi api)
    {
        var arrived = 0;
        var bothArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Host.BeforeSave.Run(async () =>
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                bothArrived.SetResult();
            }

            await bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }, times: 2);
    }

    /// <summary>Asserts the answers' statuses are exactly <paramref name="expected"/> in any order, describing every answer when not.</summary>
    public static async Task ShouldBePairAsync(HttpResponseMessage[] answers, params HttpStatusCode[] expected)
    {
        answers.Select(a => a.StatusCode).ShouldBe(expected, ignoreOrder: true,
            string.Join("; ", await Task.WhenAll(answers.Select(AnswerAssertions.DescribeAsync))));
    }
}
