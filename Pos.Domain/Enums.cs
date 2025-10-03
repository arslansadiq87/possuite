// Pos.Domain/Enums.cs
namespace Pos.Domain
{
    public enum PaymentMethod { Cash = 1, Card = 2, Mixed = 3 }

    public enum CustomerKind { WalkIn = 1, Registered = 2 }

    public enum UserRole
    {
        Salesman = 0,
        Cashier = 1,
        Supervisor = 2,
        Manager = 3,
        Admin = 4
    }

    public enum SaleStatus { Draft = 0, Final = 1, Voided = 2, Revised = 3 }


}
