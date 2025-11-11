// Pos.Domain/Services/IOutletService.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Services
{
    public interface IOutletService
    {
        Task<int> CreateAsync(Outlet outlet, CancellationToken ct = default);
        Task UpdateAsync(Outlet outlet, CancellationToken ct = default);
        Task<List<Outlet>> GetAllAsync(CancellationToken ct = default);
    }
}
