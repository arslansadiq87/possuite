// Pos.Domain/Enums.cs
namespace Pos.Domain
{
    public enum PaymentMethod { Cash = 1, Card = 2, Mixed = 3 }

    public enum CustomerKind { WalkIn = 1, Registered = 2 }

    //public enum UserRole { Admin = 1, Cashier = 2, Salesman = 3, Manager = 4 }
    
    public enum SaleStatus { Draft = 0, Final = 1, Voided = 2, Revised = 3 }
}
