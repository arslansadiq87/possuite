// Pos.Domain/Entities/Customer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public class Customer : BaseEntity
    {
        public string? DisplayName { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }

    }
}
