using DKNet.StaticData.Domains.Features.FileGroups.Entities;
using DKNet.StaticData.Domains.Features.Files.Entities;

namespace DKNet.StaticData.App.Tests.Unit.FileGroups;

/// <summary>Brief DRK-2239 §3 rows 1 and 2: each change of a group or a file's link raises its version by exactly 1.</summary>
public class FileGroupTests
{
    [Fact]
    public void ANewGroupIsAtVersion1WithItsFieldsAsGiven()
    {
        var group = FileGroup.Create("Onboarding pack", "customer-onboarding", "CUS-0042", "onboarding-svc");

        group.Id.Version.ShouldBe(7);
        group.Name.ShouldBe("Onboarding pack");
        group.Purpose.ShouldBe("customer-onboarding");
        group.ExternalRef.ShouldBe("CUS-0042");
        group.CreatedBy.ShouldBe("onboarding-svc");
        group.Version.ShouldBe(1);
        group.UpdatedBy.ShouldBeNull();
    }

    [Fact]
    public void AnUpdateReplacesAll3FieldsAndRaisesTheVersionBy1()
    {
        var group = FileGroup.Create("Onboarding pack", "customer-onboarding", "CUS-0042", "onboarding-svc");

        group.Update("Pack 2", null, null, "ingest-svc");
        group.Update("Pack 3", "visa", "CUS-7", "ingest-svc");

        group.Name.ShouldBe("Pack 3");
        group.Purpose.ShouldBe("visa");
        group.ExternalRef.ShouldBe("CUS-7");
        group.Version.ShouldBe(3);
        group.UpdatedBy.ShouldBe("ingest-svc");
        group.UpdatedOn.ShouldNotBeNull();
    }

    [Fact]
    public void LinkingAndUnlinkingAFileEachRaiseItsVersionBy1()
    {
        var file = StoredFile.Create("passport.pdf", ".pdf", "application/pdf", 1024, new string('a', 64), "onboarding-svc");
        var groupId = Guid.NewGuid();
        file.GroupId.ShouldBeNull();

        file.LinkTo(groupId, "ingest-svc");
        file.GroupId.ShouldBe(groupId);
        file.Version.ShouldBe(2);

        file.LinkTo(groupId, "ingest-svc");
        file.Version.ShouldBe(3);

        file.LinkTo(null, "ingest-svc");
        file.GroupId.ShouldBeNull();
        file.Version.ShouldBe(4);
        file.UpdatedBy.ShouldBe("ingest-svc");
    }
}
