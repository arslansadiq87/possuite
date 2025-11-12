using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Models.Hr;   // DTOs
using Pos.Domain.Hr;

namespace Pos.Domain.Services.Hr
{
    /// <summary>
    /// Single source of truth for payroll runs (draft, finalize, pay).
    /// No UI or database specifics; persistence handled in implementations.
    /// </summary>
    public interface IPayrollService
    {
        /// <summary>Create a draft payroll run for the given period and populate items for all active staff.</summary>
        Task<PayrollRunDto> CreateDraftAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct = default);

        /// <summary>Get a run by id as DTO (or null if missing).</summary>
        Task<PayrollRunDto?> GetRunAsync(int runId, CancellationToken ct = default);

        /// <summary>List all items of a run as DTOs (includes StaffName).</summary>
        Task<IReadOnlyList<PayrollItemDto>> GetItemsAsync(int runId, CancellationToken ct = default);

        /// <summary>Batch update numeric fields for items in a run; recomputes totals.</summary>
        Task UpdateItemsAsync(int runId, IReadOnlyList<PayrollItemUpdateRequest> updates, CancellationToken ct = default);

        /// <summary>Compute authoritative totals for a run.</summary>
        Task<PayrollRunSummaryDto> GetSummaryAsync(int runId, CancellationToken ct = default);

        /// <summary>Finalize a payroll run (posts accruals, locks edits).</summary>
        Task FinalizeAsync(int runId, CancellationToken ct = default);

        /// <summary>Pay a finalized payroll run (posts payments).</summary>
        Task PayAsync(int runId, CancellationToken ct = default);
    }
}
