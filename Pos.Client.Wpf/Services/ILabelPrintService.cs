using System.Threading.Tasks;
using Pos.Domain.Entities;

public interface ILabelPrintService
{
    Task PrintSampleAsync(BarcodeLabelSettings s);
}
