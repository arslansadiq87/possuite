using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pos.Client.Wpf.Contracts
{
    public interface IScanFocusable
    {
        void FocusScan();   // put caret where scanning should continue
    }
}
