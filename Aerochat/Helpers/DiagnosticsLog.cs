using System.Diagnostics;

namespace Aerochat.Helpers
{
    /// <summary>
    /// Lightweight diagnostics for exceptions that are intentionally not surfaced to the user.
    /// Uses <see cref="Trace.WriteLine(string?)"/> so messages appear with default trace listeners
    /// (e.g. Visual Studio Output) without pulling in a logging framework.
    /// </summary>
    internal static class DiagnosticsLog
    {
        public static void Swallowed(string context, Exception ex)
        {
            Trace.WriteLine($"[Aerochat] {context}: {ex}");
        }
    }
}
