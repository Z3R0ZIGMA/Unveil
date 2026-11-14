using System.Runtime.InteropServices;

namespace Unveil.Cli;

/// <summary>Detects whether the exe was started by double-clicking it in Explorer.</summary>
internal static class ConsoleHost
{
    /// <summary>
    /// True when this process owns its console window (Explorer created it for us), so the window
    /// would close the moment we exit. False when run from cmd/PowerShell or with redirected output (RMM).
    /// </summary>
    public static bool StartedByDoubleClick { get; } = Detect();

    private static bool Detect()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return false;
        }

        try
        {
            var processes = new uint[2];
            return GetConsoleProcessList(processes, (uint)processes.Length) == 1;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static void WaitForKey()
    {
        Console.WriteLine();
        Console.Write("Press any key to close this window...");
        try
        {
            Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException)
        {
            // No keyboard attached to the console; just exit.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList([Out] uint[] processList, uint processCount);
}
