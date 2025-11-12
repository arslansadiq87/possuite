using Pos.Domain;

namespace Pos.Domain.Services.Security
{
    /// <summary>Permissions mapped against minimum UserRole by the IAuthorizationService policy.</summary>
    public enum Perm
    {
        Purchases_View_All,
        Purchases_Edit,
        PurchaseReturns_Process,
        Sales_View_All,
        Sales_Returns_Process,
        Sales_Void,
        Shift_Open,
        Shift_Close,
        Drawer_Kick,
        Users_Manage,
        Catalog_Manage,
        Outlets_Manage,
        Reports_View_All
    }
}
