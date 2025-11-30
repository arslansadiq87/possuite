using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;
using Pos.Domain.Models.Catalog;

namespace Pos.Domain.Services
{
    public interface ICategoryService
    {
        Task<List<CategoryRowDto>> SearchAsync(string? term, bool includeInactive, CancellationToken ct = default);
        Task SetActiveAsync(int categoryId, bool active, CancellationToken ct = default);
        Task<Category> SaveCategoryAsync(int? id, string name, bool isActive, CancellationToken ct = default);
        Task<Category?> GetCategoryAsync(int id, CancellationToken ct = default);
        Task<Category?> GetOrCreateAsync(string name, bool createIfMissing, CancellationToken ct = default);
    }
}
