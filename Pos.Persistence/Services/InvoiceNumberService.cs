// Pos.Persistence/Services/InvoiceNumberService.cs
using Microsoft.EntityFrameworkCore;

namespace Pos.Persistence.Services
{
    public class InvoiceNumberService
    {
        private readonly PosClientDbContext _db;
        public InvoiceNumberService(PosClientDbContext db) => _db = db;

        public async Task<int> AllocateAsync(int counterId)
        {
            // Load row with concurrency token
            var seq = await _db.CounterSequences.SingleAsync(x => x.CounterId == counterId);
            var number = seq.NextInvoiceNumber;
            seq.NextInvoiceNumber++;
            await _db.SaveChangesAsync(); // will throw on conflict; your sync resolver can retry
            return number;
        }
    }
}
