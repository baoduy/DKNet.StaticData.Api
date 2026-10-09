using DKNet.EfCore.DataAuthorization;
using DKNet.StaticData.Domains.Share;

namespace DKNet.StaticData.Domains.Features.ManualSample.Entities;

/// <summary>
/// The lifecycle state of a <see cref="PurchaseOrder"/>.
/// </summary>
public enum PurchaseOrderStatus
{
    Draft,
    Placed,
    Cancelled
}

/// <summary>
/// Purchase order aggregate root. Every layer of this sample — including this entity's event — is hand-written;
/// no declarative event/CRUD/DTO-generation attribute is used anywhere.
/// </summary>
/// <remarks>
/// Implements <see cref="IOwnedBy"/> so <c>DataOwnerAuthQuery</c>'s global read filter (row-level isolation)
/// applies to it; <c>DataOwnerHook</c> stamps <see cref="OwnedBy"/> from the caller's ownership key on insert.
/// </remarks>
public sealed class PurchaseOrder : AggregateRoot, IOwnedBy
{
    #region Constructors

    public PurchaseOrder(string customerName, decimal amount, string byUser)
        : base(byUser)
    {
        CustomerName = customerName;
        Amount = amount;
        Status = PurchaseOrderStatus.Placed;

        AddEvent(new PurchaseOrderCreatedEvent(Id, CustomerName, Amount));
    }

    /// <summary>
    /// Rehydrates a <see cref="PurchaseOrder"/> with a known identity — used by static reference-data seeding only.
    /// Does not re-raise <see cref="PurchaseOrderCreatedEvent"/>. <see cref="OwnedBy"/> is set to
    /// <paramref name="byUser"/>, because a seeding context has no caller for <c>DataOwnerHook</c> to stamp from.
    /// </summary>
    internal PurchaseOrder(Guid id, string customerName, decimal amount, string byUser)
        : base(id, byUser)
    {
        CustomerName = customerName;
        Amount = amount;
        Status = PurchaseOrderStatus.Placed;
        OwnedBy = byUser;
    }

    private PurchaseOrder()
    {
    }

    #endregion

    #region Properties

    public string CustomerName { get; private set; } = null!;

    public decimal Amount { get; private set; }

    public PurchaseOrderStatus Status { get; private set; }

    /// <summary>Gets the ownership key of the caller who created this order — stamped by <c>DataOwnerHook</c>.</summary>
    public string OwnedBy { get; private set; } = string.Empty;

    #endregion

    #region Methods

    public void ChangeAmount(decimal amount, string userId)
    {
        Amount = amount;
        SetUpdatedBy(userId);
    }

    public void Cancel(string userId)
    {
        Status = PurchaseOrderStatus.Cancelled;
        SetUpdatedBy(userId);
    }

    #endregion
}
