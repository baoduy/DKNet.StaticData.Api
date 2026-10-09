using DKNet.EfCore.Specifications.Definitions;
using DKNet.StaticData.Domains.Features.AutomatedSample.Entities;

namespace DKNet.StaticData.AppServices.AutomatedSample.V1.Specs;

internal sealed class SpecProductByName : Specification<Product>
{
    public SpecProductByName(string name)
    {
        WithFilter(CreatePredicate().And(p => p.Name == name));
    }
}
