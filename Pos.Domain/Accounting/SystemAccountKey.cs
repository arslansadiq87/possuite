namespace Pos.Domain.Accounting
{
    public enum SystemAccountKey
    {
        // Headers (no OutletId)
        CashInHandHeader = 111,  // matches your template
        BankHeader = 112,  // if your template uses 112 for banks; adjust if different
        InventoryHeader = 113,

        // Outlet-scoped assets (child accounts under 111)
        CashInHandOutlet = 11101,   // child of 111 (choice of suffix is yours)
        CashInTillOutlet = 11102,   // child of 111

        // Others: set to the line you actually post to
        AccountsReceivable = 0,       // set if you have a specific leaf code
        AccountsPayable = 0,       // set if you have a specific leaf code
        SalesRevenue = 411,     // “Gross sales value” in your template
        Cogs = 5111,    // “Actual cost of sold stock” (if that’s where you post)
        TaxPayable = 0,       // set if you post to a specific liability code
        SalariesExpense = 5201,    // or a child like wages 52011 if you post there
        CashOverShort = 541      // matches “Cash short”
    }
}
