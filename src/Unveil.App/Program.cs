using Unveil.Core.Rmm;

namespace Unveil.App;

/// <summary>
/// Entry point. <c>Unveil.exe --rmm</c> runs the whole check with no window (N-sight runs it as Local System through
/// rmm/Unveil-NSight.ps1) and returns the result as the exit code. Anything else starts the normal app.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--rmm", StringComparison.OrdinalIgnoreCase)))
        {
            // No WPF at all in this mode: nothing may ever wait for a click on a screen nobody can see.
            return RmmRunner.Run(RmmOptions.Parse(args)).ExitCode;
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
