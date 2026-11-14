namespace Unveil.Core.Power;

/// <summary>An app known to drain batteries, with plain-English advice. (Configurable via unveil.json from Phase 7.)</summary>
/// <param name="Category">e.g. "Browser", "Antivirus scan".</param>
/// <param name="Advice">What the tech can do about it.</param>
public sealed record KnownOffender(string Category, string Advice);

public static class KnownOffenders
{
    /// <summary>A browser running at least this many processes counts as "many tabs".</summary>
    public const int ManyBrowserProcesses = 25;

    private static readonly string[] Browsers = ["chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "arc"];

    private static readonly (string Category, string Advice, string[] Names)[] Groups =
    [
        ("Microsoft Teams", "Teams uses a lot of power in the background. Quit it when not needed, or turn off \"Auto-start\" in its settings.",
            ["ms-teams", "teams", "msteams", "msedgewebview2-teams"]),
        ("OneDrive syncing", "OneDrive is syncing files. Let it finish, or pause syncing while on battery.",
            ["onedrive", "filecoauth"]),
        ("Windows Search indexer", "Windows Search is indexing files. This normally settles down; if it keeps running, rebuild the index or limit indexed locations.",
            ["searchindexer", "searchprotocolhost", "searchfilterhost"]),
        ("Antivirus scan", "An antivirus scan was running. Schedule full scans for when the laptop is plugged in.",
            ["msmpeng", "mpdefendercoreservice", "nissrv", "mssense", "avp", "avpui", "ekrn", "bdservicehost", "vsserv", "mcshield", "mfefire", "ccsvchst", "nortonsecurity", "avastsvc", "avgsvc", "sophoshealth", "savservice", "csfalconservice", "sentinelagent"]),
        ("Windows Update", "Windows Update is installing updates. Let it finish while plugged in, then restart.",
            ["tiworker", "trustedinstaller", "wuauclt", "usoclient", "musnotification", "mousocoreworker", "usocoreworker", "wudfhost-update"]),
        ("Crypto miner", "A cryptocurrency miner was running. If the customer did not install it on purpose, treat it as malware and remove it.",
            ["xmrig", "nicehash", "nicehashminer", "excavator", "ethminer", "nbminer", "t-rex", "trex", "phoenixminer", "lolminer", "gminer", "teamredminer", "cgminer", "bfgminer", "nanominer"]),
        ("RGB / OEM utility", "Lighting and vendor utilities often run all the time. Turn off or uninstall the ones the customer does not use.",
            ["icue", "corsair.service", "lightingservice", "armourycrate.service", "armourycrate.usersessionhelper", "asusoptimization", "razer synapse service", "razercentralservice", "rzsdkservice", "lghub", "lghub_agent", "lghub_updater", "nahimicsvc64", "nahimicsvc32", "signalrgb", "openrgb", "msi.centralserver", "dragoncenter", "supportassistagent", "dell.techhub", "hpsysinfo", "hpcommrecovery", "lenovovantageservice", "lenovo.modern.imcontroller", "alienwarecc"]),
    ];

    private static readonly Dictionary<string, KnownOffender> ByName = Groups
        .SelectMany(g => g.Names.Select(n => (n, new KnownOffender(g.Category, g.Advice))))
        .ToDictionary(x => x.n, x => x.Item2, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the offender entry for an executable name (without ".exe"), or null.</summary>
    public static KnownOffender? Match(string name, int processCount)
    {
        var key = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

        if (Browsers.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            return processCount >= ManyBrowserProcesses
                ? new KnownOffender("Browser with many tabs",
                    $"The browser is running {processCount} processes (many tabs or extensions). Close tabs that are not needed and turn on the browser's efficiency or memory saver mode.")
                : null;
        }

        return ByName.GetValueOrDefault(key);
    }
}
