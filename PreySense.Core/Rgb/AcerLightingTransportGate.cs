namespace PreySense.Rgb;

// A2 selects the target for A3. Keep that transaction together with respect to
// both the existing ENEK client and the descriptor-driven lighting clients.
internal static class AcerLightingTransportGate
{
    internal static readonly object Sync = new();
}
