namespace Unveil.Core.Power;

/// <summary>
/// Plain names for Windows' own processes, whose exe names ("dwm", "svchost") mean nothing to a customer.
/// They are part of Windows and cannot be closed; a high share usually just means everything else was idle.
/// </summary>
public static class WindowsProcesses
{
    /// <param name="Name">What to show instead of the exe name.</param>
    /// <param name="Role">What it does, in a few words.</param>
    public sealed record Info(string Name, string Role)
    {
        /// <summary>e.g. "Part of Windows: draws the screen".</summary>
        public string Note => $"Part of Windows: {Role}";
    }

    private static readonly Dictionary<string, Info> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dwm"] = new("Desktop Window Manager", "draws everything on the screen"),
        ["System"] = new("Windows kernel and drivers", "hardware drivers and the core of Windows"),
        ["svchost"] = new("Windows services", "runs background services (expand to see them)"),
        ["Memory Compression"] = new("Memory Compression", "compresses memory so more apps fit"),
        ["MemCompression"] = new("Memory Compression", "compresses memory so more apps fit"),
        ["Registry"] = new("Windows registry", "stores settings"),
        ["csrss"] = new("Windows client/server runtime", "console windows and shutdown"),
        ["lsass"] = new("Windows security", "sign-in and security checks"),
        ["services"] = new("Windows service manager", "starts and stops services"),
        ["wininit"] = new("Windows start-up", "starts Windows"),
        ["winlogon"] = new("Windows sign-in", "sign-in and the lock screen"),
        ["smss"] = new("Windows session manager", "starts user sessions"),
        ["fontdrvhost"] = new("Font driver host", "draws fonts"),
        ["WmiPrvSE"] = new("WMI provider host", "answers management queries (often from monitoring tools)"),
        ["audiodg"] = new("Windows audio", "processes sound"),
        ["taskhostw"] = new("Windows task host", "runs scheduled tasks"),
        ["RuntimeBroker"] = new("Runtime Broker", "checks permissions for Store apps"),
        ["dllhost"] = new("COM Surrogate", "runs helpers such as thumbnail generators"),
        ["conhost"] = new("Console window host", "hosts command-line windows"),
        ["rundll32"] = new("Windows host process (rundll32)", "runs small Windows components"),
        ["sihost"] = new("Shell infrastructure host", "the taskbar and notifications"),
        ["ctfmon"] = new("Text input (CTF loader)", "keyboard and input methods"),
        ["spoolsv"] = new("Print spooler", "printing"),
        ["ShellExperienceHost"] = new("Windows shell", "the Start menu and taskbar flyouts"),
        ["StartMenuExperienceHost"] = new("Start menu", "the Start menu"),
        ["SearchHost"] = new("Windows search box", "the taskbar search"),
        ["TextInputHost"] = new("Touch keyboard and emoji panel", "text input"),
        ["LockApp"] = new("Lock screen", "the lock screen"),
        ["WUDFHost"] = new("Windows driver host", "runs some hardware drivers"),
    };

    /// <summary>The plain name and role for a Windows process (exe name without ".exe"), or null for anything else.</summary>
    public static Info? Describe(string? exeName) =>
        exeName is not null && Known.TryGetValue(exeName.Trim(), out var info) ? info : null;
}
