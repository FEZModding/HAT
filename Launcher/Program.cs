namespace FEZ.HAT.AppHost;

// This assembly only supplies metadata used by the .NET SDK when it creates the
// native HAT apphost. The installer replaces HAT.dll with the patched game.
internal static class Program
{
    private static int Main() => throw new InvalidOperationException(
        "The HAT apphost template was launched without the patched game assembly.");
}