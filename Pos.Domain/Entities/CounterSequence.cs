// Pos.Domain/Entities/CounterSequence.cs
using System.ComponentModel.DataAnnotations;
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public class CounterSequence : BaseEntity
    {
        public int CounterId { get; set; }      // matches your existing Counter
        public int NextInvoiceNumber { get; set; } = 1;
                
    }
}
