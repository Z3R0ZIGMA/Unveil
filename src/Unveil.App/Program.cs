using System.IO;
using System.Runtime.InteropServices;
using Unveil.CommandLine;

namespace Unveil.App;

/// <summary>Entry point: "Unveil.exe --cli ..." runs the command line (plan.md section 7); anything else opens the window.</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--cli", StringComparison.OrdinalIgnoreCase)))
        {
            return RunCommandLine(args);
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    /// <summary>
    /// Unveil.exe is a window app, so it has no console of its own. RMM tools capture output through redirected handles,
    /// which work as they are. Typed in a Command Prompt, attach to it so the output shows (the prompt does not wait for a
    /// window app, so Unveil.Cli.exe is the better choice there).
    /// </summary>
    private static int RunCommandLine(string[] args)
    {
        if (!Console.IsOutputRedirected && AttachConsole(AttachParentProcess))
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            Console.WriteLine();
            Console.WriteLine("Tip: in a Command Prompt, use Unveil.Cli.exe instead (it waits and shows the exit code). Unveil.exe --cli is for RMM scripts.");
        }

        return CommandLineRunner.Run(args, new CommandLineContext { SummaryByDefault = true });
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);
}
