using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Services
{
    public interface IReceiptTemplateService
    {
        Task<ReceiptTemplate> GetAsync(int? outletId, ReceiptDocType docType, CancellationToken ct = default);
        Task SaveAsync(ReceiptTemplate template, CancellationToken ct = default);
        Task<IReadOnlyList<ReceiptTemplate>> GetAllForOutletAsync(int? outletId, CancellationToken ct = default);
        Task<ReceiptTemplate> GetOrCreateDefaultAsync(int? outletId, ReceiptDocType docType, CancellationToken ct = default);
    }
}
