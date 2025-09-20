using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    public sealed class CounterBindingService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IMachineIdentityService _mid;

        public CounterBindingService(IDbContextFactory<PosClientDbContext> dbf, IMachineIdentityService mid)
        {
            _dbf = dbf; _mid = mid;
        }

        public CounterBinding? GetCurrentBinding()
        {
            using var db = _dbf.CreateDbContext();
            var mid = _mid.GetMachineId();
            return db.CounterBindings
                .Include(b => b.Outlet)
                .Include(b => b.Counter)
                .AsNoTracking()
                .FirstOrDefault(b => b.MachineId == mid && b.IsActive);
        }

        public void AssignThisPcToCounter(int outletId, int counterId)
        {
            using var db = _dbf.CreateDbContext();
            var mid = _mid.GetMachineId();
            var mname = _mid.GetMachineName();

            // Validate outlet-counter consistency
            var counter = db.Counters.AsNoTracking().FirstOrDefault(c => c.Id == counterId);
            if (counter == null) throw new InvalidOperationException("Counter not found.");
            if (counter.OutletId != outletId) throw new InvalidOperationException("Counter does not belong to the selected outlet.");

            // Enforce 1:1 both ways
            var existingForMachine = db.CounterBindings.FirstOrDefault(b => b.MachineId == mid);
            if (existingForMachine != null)
            {
                // reassign same row to new outlet/counter
                existingForMachine.OutletId = outletId;
                existingForMachine.CounterId = counterId;
                existingForMachine.MachineName = mname;
                existingForMachine.IsActive = true;
                existingForMachine.LastSeenUtc = DateTime.UtcNow;
            }
            else
            {
                // ensure this counter isn't taken
                var taken = db.CounterBindings.Any(b => b.CounterId == counterId);
                if (taken) throw new InvalidOperationException("This counter is already assigned to another PC.");

                db.CounterBindings.Add(new CounterBinding
                {
                    MachineId = mid,
                    MachineName = mname,
                    OutletId = outletId,
                    CounterId = counterId,
                    IsActive = true,
                    LastSeenUtc = DateTime.UtcNow
                });
            }

            db.SaveChanges();
        }

        public void UnassignThisPc()
        {
            using var db = _dbf.CreateDbContext();
            var mid = _mid.GetMachineId();
            var row = db.CounterBindings.FirstOrDefault(b => b.MachineId == mid);
            if (row == null) return;
            db.CounterBindings.Remove(row);
            db.SaveChanges();
        }
    }
}
