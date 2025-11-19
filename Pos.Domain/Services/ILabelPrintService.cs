using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Domain.Services
{
    public interface ILabelPrintService
    {
        // Simple version: print from settings (no live sample fields)
        Task PrintSampleAsync(BarcodeLabelSettings settings, CancellationToken ct = default);

        // Extended: print exactly what the preview shows (live sample fields)
        Task PrintSampleAsync(
            BarcodeLabelSettings settings,
            string sampleCode,
            string sampleName,
            string samplePrice,
            string sampleSku,
            bool showBusinessName,
            string businessName,
            CancellationToken ct = default);
    }
}
