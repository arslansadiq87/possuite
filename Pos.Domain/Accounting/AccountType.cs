namespace Pos.Domain.Accounting
{
    public enum AccountType
    {
        Asset = 1,
        Liability = 2,
        Equity = 3,
        Revenue = 4,
        Expense = 5,
        ContraAsset = 6,
        ContraRevenue = 7
    }

    public enum NormalBalance { Debit, Credit }
}
