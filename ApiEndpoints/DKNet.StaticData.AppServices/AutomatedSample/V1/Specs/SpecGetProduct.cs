using DKNet.EfCore.Specifications.Definitions;
using DKNet.StaticData.Domains.Features.AutomatedSample.Entities;

namespace DKNet.StaticData.AppServices.AutomatedSample.V1.Specs;

internal sealed class SpecGetProduct : Specification<Product>
{
    public SpecGetProduct(Guid? byId = null)
    {
        var predicator = CreatePredicate();

        if (byId is not null)
        {
            predicator = predicator.And(a => a.Id == byId);
        }
        else
        {
            // An unstarted predicate builder compiles to WHERE FALSE — without this, "no filter" would
            // silently match nothing instead of every product (see SpecGetPurchaseOrder).
            predicator = predicator.And(_ => true);
        }

        WithFilter(predicator);
    }
}
