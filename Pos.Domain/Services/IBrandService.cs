using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;
using Pos.Domain.Models.Catalog; // BrandRowDto

namespace Pos.Domain.Services
{
    public interface IBrandService
    {
        Task<List<BrandRowDto>> SearchAsync(string? term, bool includeInactive, CancellationToken ct = default);
        Task SetActiveAsync(int brandId, bool active, CancellationToken ct = default);
        Task<Brand> SaveBrandAsync(int? id, string name, bool isActive, CancellationToken ct = default);
        Task<Brand?> GetBrandAsync(int id, CancellationToken ct = default);
    }
}
