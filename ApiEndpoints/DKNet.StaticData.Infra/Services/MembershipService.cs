using DKNet.StaticData.Infra.Contexts;

namespace DKNet.StaticData.Infra.Services;

internal sealed class MembershipService(CoreDbContext dbContext)
    : SequenceService(dbContext, Sequences.Membership), IMembershipService;