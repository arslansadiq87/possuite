using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pos.Client.Wpf.Models;
using Pos.Domain.Entities;

namespace Pos.Client.Wpf.Printing
{
    public static class SaleReturnReceiptBuilder
    {
        public static byte[] Build(Sale sale, List<CartLine> cart, TillSession? till,
            string? storeName, string? cashierName, string? salesmanName, string? eReceiptBaseUrl)
        {
            // duplicate structure of EscPosReceiptBuilder, but:
            // - Title: "*** SALE RETURN ***"
            // - Line totals should reflect returns (negative) or use separate "Refund" fields you maintain
            // - Footer from template if you later pass it in (extend signature as needed)
            // For now, mirror EscPosReceiptBuilder and change headings.
            return EscPosReceiptBuilder.Build(sale, cart, till, storeName, cashierName, salesmanName, eReceiptBaseUrl);
        }
    }

}
