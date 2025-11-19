using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;

namespace Pos.Client.Wpf.Printing
{
    public static class VoucherReceiptBuilder
    {
        public static byte[] Build(Voucher v, ReceiptTemplate tpl)
        {
            var bytes = new List<byte>();
            // small helper lambdas like EscPosReceiptBuilder (Txt, Center, Bold...)
            // print header, voucher no/date, party (if any), lines, debit/credit totals, narration, footer
            // end with Feed + Cut
            return bytes.ToArray();
        }
    }

}
