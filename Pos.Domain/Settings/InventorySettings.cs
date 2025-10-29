// Pos.Domain/Settings/InventorySettings.cs
namespace Pos.Domain.Settings
{
    /// <summary>
    /// Centralized inventory-related configuration flags.
    /// Use these to toggle business logic features globally.
    /// </summary>
    public static class InventorySettings
    {
        /// <summary>
        /// If true: dispatch immediately posts both OUT (source) and IN (destination).
        /// If false: dispatch only posts OUT; a separate Receive step posts IN.
        /// </summary>
        public const bool OneStepTransfers = true;
    }
}
