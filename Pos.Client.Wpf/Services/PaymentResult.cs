namespace Pos.Client.Wpf.Services
{
    public sealed class PaymentResult
    {
        public bool Confirmed { get; init; }
        public decimal Cash { get; init; }
        public decimal Card { get; init; }
    }
}
