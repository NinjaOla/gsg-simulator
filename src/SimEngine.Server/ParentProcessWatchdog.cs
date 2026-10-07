using System.Diagnostics;

namespace SimEngine.Server;

/// <summary>Lets a child server process stop when the process that spawned it exits.</summary>
public static class ParentProcessWatchdog
{
    /// <summary>Completes when the process exits, or immediately if it is not running.</summary>
    public static async Task WaitForExitAsync(int processId, CancellationToken ct = default)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return;
        }

        using (process)
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
    }
}
