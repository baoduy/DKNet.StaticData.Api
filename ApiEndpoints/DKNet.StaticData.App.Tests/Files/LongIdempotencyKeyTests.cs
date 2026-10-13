using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Files;

/// <summary>
/// Spec DRK-2235 §5 "Slice 2 fixes": an idempotency key of 151 to 255 characters works on the upload and on the group
/// create, which the DKNet 13.2.5 idempotency store, keeping only 150 characters of a key, did not allow.
/// </summary>
public sealed class LongIdempotencyKeyTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    public static IEnumerable<object[]> LongKeys => Databases.With(
        ["uploads \"passport.pdf\"", 151],
        ["uploads \"visa.pdf\"", 255],
        ["creates the group \"Onboarding pack\"", 255]);

    /// <summary>Scenario Outline: Long idempotency keys work.</summary>
    [Theory]
    [MemberData(nameof(LongKeys))]
    public async Task LongIdempotencyKeysWork(TestDatabase database, string action, int length)
    {
        var api = await hosts.OnAsync(database);
        var key = new string('k', length);

        using var response = action switch
        {
            "uploads \"passport.pdf\"" => await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf", Bytes.Of(1024), key),
            "uploads \"visa.pdf\"" => await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "visa.pdf", Bytes.Of(1024), key),
            "creates the group \"Onboarding pack\"" => await api.CreateGroupAsync(
                Callers.OnboardingSvc, "customer-1", FileGroupsApi.GroupBody("Onboarding pack"), key),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }
}
