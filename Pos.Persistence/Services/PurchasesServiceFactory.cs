// Pos.Client.Wpf/Services/PurchasesServiceFactory.cs
using Microsoft.EntityFrameworkCore;
using Pos.Persistence;
using Pos.Persistence.Services;
using Pos.Persistence.Sync;
using Pos.Domain.Services; // <-- so the factory can return the interface

namespace Pos.Client.Wpf.Services
{
    public interface IPurchasesServiceFactory
    {
        IPurchasesService Create();   // <-- return the interface (recommended)
    }

    public sealed class PurchasesServiceFactory : IPurchasesServiceFactory
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;
        private readonly IInventoryReadService _inv;   // <-- add
        private readonly IGlPostingService _gl;        // <-- add

        public PurchasesServiceFactory(
            IDbContextFactory<PosClientDbContext> dbf,
            IOutboxWriter outbox, IInventoryReadService inv, IGlPostingService gl)
        {
            _dbf = dbf;
            _outbox = outbox;
            _inv = inv;                                // <-- add
            _gl = gl;
        }

        public IPurchasesService Create()
        {
            // IMPORTANT: do NOT new a DbContext here anymore.
            // PurchasesService now expects the factory.
            return new PurchasesService(_dbf, _outbox, _inv, _gl);
        }
    }
}
