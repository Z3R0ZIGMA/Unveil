using Unveil.Cli;
using Unveil.CommandLine;

// Unveil.Cli.exe: the console version, for typing commands in a Command Prompt or PowerShell.
// "Unveil.exe --cli" runs the same code for RMM scripts (plan.md section 7).
var exitCode = CommandLineRunner.Run(args, new CommandLineContext { StartedByDoubleClick = ConsoleHost.StartedByDoubleClick });

// Started by double-click: keep the window open so the results can be read.
if (ConsoleHost.StartedByDoubleClick)
{
    ConsoleHost.WaitForKey();
}

return exitCode;
