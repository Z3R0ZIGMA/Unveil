using System.Net;
using System.Text;
using Unveil.Core.Battery;
using Unveil.Core.Verdict;
using static Unveil.Core.Formatting;

namespace Unveil.Core.Reports;

/// <summary>
/// Single self-contained HTML report (no scripts, no external files, no network). Dark theme on screen
/// (plan.md section 5.1) and a white print stylesheet. Customer summary at the top, technical detail below.
/// </summary>
public static class HtmlReportWriter
{
    public static void Write(ReportDocument doc, string path) =>
        File.WriteAllText(path, Render(doc), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    public static string Render(ReportDocument doc)
    {
        var s = doc.Snapshot;
        var v = doc.Verdict;
        var html = new StringBuilder(32 * 1024);

        var device = string.Join(" ", new[] { s.System.Manufacturer, s.System.DisplayModel }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var title = $"Battery report - {(device.Length > 0 ? device : "Unknown laptop")} - {Date(doc.GeneratedAt)}";

        html.Append($"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="generator" content="{E(doc.Generator)}">
            <title>{E(title)}</title>
            <style>{Css}</style>
            </head>
            <body>
            <main>
            """);

        Header(html, doc, device);
        VerdictBanner(html, v);

        if (s.HasBattery)
        {
            KeyFigures(html, doc);
        }

        if (doc.Comparison is { Match: not ComparisonMatch.NoMatch } comparison)
        {
            ComparisonTable(html, comparison);
        }

        List(html, "What to do", "actions", v.Actions.Select(a => (Severity.Neutral, a)), numbered: true);
        List(html, "Why", "evidence", v.Evidence.Select(f => (f.Severity, f.Text)), numbered: false);

        if (doc.SettingChecks.Count > 0)
        {
            SettingsTable(html, doc.SettingChecks);
        }

        if (doc.Activity is { Apps.Count: > 0 } activity)
        {
            AppsTable(html, activity);
        }

        if (doc.Admin is { IsAdministrator: true } admin)
        {
            if (admin.Srum?.Last24Hours is { } day)
            {
                EnergyWindowTable(html, "Energy use by app: last 24 hours", day);
            }

            if (admin.Srum?.Last7Days is { } week)
            {
                EnergyWindowTable(html, $"Energy use by app: since {Date(week.From)}", week);
            }

            if (admin.Sleep is { } sleep)
            {
                SleepTable(html, sleep);
            }

            if (admin.Energy is { } energy)
            {
                EnergyDiagnosticsList(html, energy);
            }
        }

        if (s.Report?.CapacityHistory is { Count: > 1 } history)
        {
            CapacityChart(html, history);
        }

        html.Append("<h2 class=\"section-title\">Technical details</h2>\n");
        SystemTable(html, s);
        foreach (var b in s.Batteries)
        {
            BatteryTable(html, b, s.Batteries.Count);
        }

        if (s.Combined is { } total)
        {
            CombinedTable(html, total);
        }

        UsageTable(html, doc);
        if (s.Report?.CapacityHistory is { Count: > 0 } h2)
        {
            HistoryTable(html, h2);
        }

        Notes(html, s);
        Footer(html, doc);

        html.Append("</main>\n</body>\n</html>\n");
        return html.ToString();
    }

    // ------------------------------------------------------------------ sections

    private static void Header(StringBuilder html, ReportDocument doc, string device)
    {
        html.Append("<header class=\"top\">\n<div class=\"brand\">");
        html.Append($"""
            <div><div class="company">Unveil</div>
            <div class="muted">Battery report &middot; {E(DateAndTime(doc.GeneratedAt))}</div></div></div>
            <div class="device"><div>{E(device.Length > 0 ? device : "Unknown laptop")}</div>
            """);
        if (doc.Snapshot.System.SerialNumber is { } serial)
        {
            html.Append($"<div class=\"muted\">Serial number {E(serial)}</div>");
        }

        html.Append("</div>\n</header>\n");
    }

    private static void VerdictBanner(StringBuilder html, Verdict.Verdict v)
    {
        html.Append($"""
            <section class="verdict {SevClass(v.Severity)}" aria-label="Verdict">
            <div class="verdict-icon" aria-hidden="true">{Icon(v.Severity)}</div>
            <div><div class="verdict-label">{SevLabel(v.Severity)}</div>
            <h1>{E(v.Title)}</h1>
            <p>{E(v.Message)}</p></div>
            </section>

            """);
    }

    private static void KeyFigures(StringBuilder html, ReportDocument doc)
    {
        var s = doc.Snapshot;
        var multiple = s.Combined is not null;
        var health = multiple ? s.Combined!.Health : s.Batteries[0].Health;
        var full = multiple ? s.Combined!.FullChargeCapacity : s.Batteries[0].FullChargeCapacity;
        var design = multiple ? s.Combined!.DesignCapacity : s.Batteries[0].DesignCapacity;
        var label = VerdictEngine.Label(health);

        html.Append("<section class=\"figures\">\n");

        // Health gauge
        html.Append($"<div class=\"card figure\"><h2>{(multiple ? "Combined battery health" : "Battery health")}</h2>");
        html.Append(Gauge(health.HealthPercent, label));
        html.Append(health.HealthPercent is null
            ? $"<p class=\"muted\">{E(health.UnknownReason)}</p>"
            : $"<p class=\"muted\">Holds {E(Number(full))} of its original {E(Number(design))} mWh</p>");
        html.Append("</div>\n");

        // Runtime
        var runtime = s.Report?.RuntimeEstimates;
        html.Append("<div class=\"card figure\"><h2>Runtime on a full charge</h2>");
        if (runtime?.AtFullChargeCapacity?.ActiveRuntime is { } now)
        {
            html.Append($"<div class=\"big\">{E(Duration(now))}</div>");
            html.Append(runtime.AtDesignCapacity?.ActiveRuntime is { } whenNew
                ? $"<p class=\"muted\">{E(Duration(whenNew))} when new (Windows estimate)</p>"
                : "<p class=\"muted\">Windows estimate</p>");
        }
        else
        {
            html.Append("<div class=\"big muted\">-</div><p class=\"muted\">Not enough usage history for an estimate</p>");
        }

        html.Append("</div>\n");

        // Cycles
        var cycles = s.Batteries.Select(b => b.CycleCount).ToList();
        html.Append("<div class=\"card figure\"><h2>Charge cycles</h2>");
        if (cycles.All(c => c is null))
        {
            html.Append("<div class=\"big muted\">-</div><p class=\"muted\">Not reported by manufacturer</p>");
        }
        else
        {
            html.Append($"<div class=\"big\">{E(string.Join(" / ", cycles.Select(c => c is null ? "-" : Number(c))))}</div>");
            html.Append($"<p class=\"muted\">Typical rating {E(Number(VerdictSettings.Default.CycleCountRating))} cycles</p>");
        }

        html.Append("</div>\n</section>\n");
    }

    private static void List(StringBuilder html, string heading, string cls, IEnumerable<(Severity Severity, string Text)> items, bool numbered)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        var tag = numbered ? "ol" : "ul";
        html.Append($"<section class=\"card\"><h2>{E(heading)}</h2><{tag} class=\"{cls}\">\n");
        foreach (var (severity, text) in list)
        {
            html.Append(numbered
                ? $"<li>{E(text)}</li>\n"
                : $"<li class=\"{SevClass(severity)}\"><span class=\"dot\" aria-hidden=\"true\">{Icon(severity)}</span>" +
                  $"<span class=\"sr\">{SevLabel(severity)}: </span>{E(text)}</li>\n");
        }

        html.Append($"</{tag}></section>\n");
    }

    private static void CapacityChart(StringBuilder html, IReadOnlyList<CapacityHistoryEntry> history)
    {
        var points = history
            .Where(e => e.Start is not null && e.FullChargeCapacity is > 0)
            .OrderBy(e => e.Start)
            .ToList();
        if (points.Count < 2)
        {
            return;
        }

        const double W = 720, H = 260, Left = 64, Right = 96, Top = 16, Bottom = 32;
        var plotW = W - Left - Right;
        var plotH = H - Top - Bottom;

        var design = points.Select(p => p.DesignCapacity).LastOrDefault(d => d is > 0);
        var values = points.Select(p => (double)p.FullChargeCapacity!.Value).ToList();
        if (design is { } d)
        {
            values.Add(d);
        }

        var step = NiceStep((values.Max() - values.Min()) / 4);
        var yMin = Math.Floor(values.Min() * 0.97 / step) * step;
        var yMax = Math.Ceiling(values.Max() * 1.02 / step) * step;
        var t0 = points[0].Start!.Value.ToUnixTimeSeconds();
        var t1 = points[^1].Start!.Value.ToUnixTimeSeconds();

        double X(DateTimeOffset t) => Left + (t1 == t0 ? 0 : (t.ToUnixTimeSeconds() - t0) / (double)(t1 - t0) * plotW);
        double Y(double v) => Top + (yMax - v) / (yMax - yMin) * plotH;
        static string F(double n) => n.ToString("0.#", Inv);

        var first = points[0];
        var last = points[^1];
        html.Append($"""
            <section class="card"><h2>Capacity history</h2>
            <p class="muted">How much the battery could hold when fully charged, over time. A steady, slow decline is normal ageing.</p>
            <svg class="chart" viewBox="0 0 {F(W)} {F(H)}" role="img" aria-label="Full charge capacity from {E(Number(first.FullChargeCapacity))} mWh on {E(Date(first.Start))} to {E(Number(last.FullChargeCapacity))} mWh on {E(Date(last.Start))}">

            """);

        // Grid + y labels
        for (var y = yMin; y <= yMax + step / 2; y += step)
        {
            html.Append($"<line class=\"grid\" x1=\"{F(Left)}\" x2=\"{F(W - Right)}\" y1=\"{F(Y(y))}\" y2=\"{F(Y(y))}\"/>");
            html.Append($"<text class=\"axis\" x=\"{F(Left - 8)}\" y=\"{F(Y(y) + 4)}\" text-anchor=\"end\">{E(Number((long)y))}</text>\n");
        }

        // X labels: first, middle, last
        foreach (var p in new[] { first, points[points.Count / 2], last }.Distinct())
        {
            var anchor = p == first ? "start" : p == last ? "end" : "middle";
            html.Append($"<text class=\"axis\" x=\"{F(X(p.Start!.Value))}\" y=\"{F(H - 8)}\" text-anchor=\"{anchor}\">{E(Date(p.Start))}</text>\n");
        }

        // Battery replaced markers
        foreach (var p in points.Where(p => p.BatteryChanged))
        {
            var x = X(p.Start!.Value);
            html.Append($"<line class=\"event\" x1=\"{F(x)}\" x2=\"{F(x)}\" y1=\"{F(Top)}\" y2=\"{F(H - Bottom)}\"/>");
            html.Append($"<text class=\"axis\" x=\"{F(x + 4)}\" y=\"{F(Top + 10)}\">Battery replaced</text>\n");
        }

        // Design capacity reference line, labelled directly
        if (design is { } dc)
        {
            html.Append($"<line class=\"design\" x1=\"{F(Left)}\" x2=\"{F(W - Right)}\" y1=\"{F(Y(dc))}\" y2=\"{F(Y(dc))}\"/>");
            html.Append($"<text class=\"label\" x=\"{F(W - Right + 6)}\" y=\"{F(Y(dc) + 4)}\">Design</text>\n");
        }

        // Full charge capacity line
        var path = string.Join(" ", points.Select((p, i) =>
            $"{(i == 0 ? "M" : "L")}{F(X(p.Start!.Value))},{F(Y(p.FullChargeCapacity!.Value))}"));
        html.Append($"<path class=\"line\" d=\"{path}\"/>\n");

        // Hover targets (native tooltips, larger than the mark)
        foreach (var p in points)
        {
            var health = p.Health.HealthPercent is { } hp ? $" ({Percent(hp)} of design)" : "";
            html.Append($"<circle class=\"hit\" cx=\"{F(X(p.Start!.Value))}\" cy=\"{F(Y(p.FullChargeCapacity!.Value))}\" r=\"7\">" +
                        $"<title>{E(Date(p.Start))}: {E(Number(p.FullChargeCapacity))} mWh{E(health)}</title></circle>");
        }

        // Latest value: visible marker + direct label
        var lx = X(last.Start!.Value);
        var ly = Y(last.FullChargeCapacity!.Value);
        html.Append($"\n<circle class=\"marker\" cx=\"{F(lx)}\" cy=\"{F(ly)}\" r=\"4.5\"/>");
        html.Append($"<text class=\"label strong\" x=\"{F(lx + 10)}\" y=\"{F(ly + 4)}\">{E(Number(last.FullChargeCapacity))}</text>\n");

        html.Append("</svg>\n<p class=\"muted small\">Full charge capacity in mWh. Hover a point for its date and value; the full table is under Technical details.</p></section>\n");
    }

    private static void AppsTable(StringBuilder html, Power.ActivityResult activity)
    {
        html.Append("<section class=\"card\"><h2>Apps using the most power</h2>");
        html.Append($"<p class=\"muted\">Measured over {E(Duration(activity.Duration))} with the laptop idle. Power Impact combines CPU, GPU and disk/network activity; the bar is each app's share.</p>");
        html.Append("<table class=\"data apps\"><thead><tr><th>App</th><th class=\"num\">Share</th><th class=\"bar-col\"></th>" +
                    "<th class=\"num\">CPU</th><th class=\"num\">GPU</th><th class=\"num\">Disk + network</th></tr></thead><tbody>\n");
        foreach (var app in activity.Apps.Take(15))
        {
            var note = app.Offender is { } o ? $"<div class=\"note sev-warning\"><span class=\"dot\" aria-hidden=\"true\">!</span>{E(o.Category)}</div>" : "";
            var count = (app.ProcessCount > 1 ? $" <span class=\"muted\">({app.ProcessCount} processes)</span>" : "") +
                        (app.Note is { } windowsNote ? $"<div class=\"muted small\">{E(windowsNote)}</div>" : "");
            html.Append($"<tr><td>{E(app.DisplayName)}{count}{note}</td>" +
                        $"<td class=\"num\">{E(Percent(app.SharePercent, "0"))}</td>" +
                        $"<td class=\"bar-col\"><div class=\"bar\"><span style=\"width:{Math.Clamp(app.SharePercent, 0, 100).ToString("0.#", Inv)}%\"></span></div></td>" +
                        $"<td class=\"num\">{E(Percent(app.CpuPercent))}</td>" +
                        $"<td class=\"num\">{(activity.GpuMeasured ? E(Percent(app.GpuPercent)) : "-")}</td>" +
                        $"<td class=\"num\">{E(Rate(app.IoBytesPerSecond))}</td></tr>\n");
        }

        html.Append("</tbody></table></section>\n");
    }

    private static void ComparisonTable(StringBuilder html, ReportComparison c)
    {
        html.Append($"<section class=\"card\"><h2>Compared with the report from {E(Date(c.PreviousGeneratedAt))}</h2>");
        html.Append($"<p>{E(c.Summary)}</p>");
        html.Append("<table class=\"data compare\"><thead><tr><th></th>" +
                    $"<th class=\"num\">{E(Date(c.PreviousGeneratedAt))}</th><th class=\"num\">Now</th><th class=\"num\">Change</th></tr></thead><tbody>\n");
        foreach (var row in c.Rows())
        {
            html.Append($"<tr><td>{E(row.Label)}</td><td class=\"num\">{E(row.Before)}</td><td class=\"num\">{E(row.Now)}</td>" +
                        $"<td class=\"num\">{E(row.Change)}</td></tr>\n");
        }

        html.Append("</tbody></table>");
        foreach (var note in c.Notes)
        {
            html.Append($"<p class=\"muted small\">{E(note)}</p>");
        }

        html.Append("</section>\n");
    }

    private static void SettingsTable(StringBuilder html, IReadOnlyList<SystemInfo.SettingCheck> checks)
    {
        html.Append("<section class=\"card\"><h2>Power settings</h2><table class=\"data settings\"><tbody>\n");
        foreach (var c in checks)
        {
            var advice = c.Severity == Severity.Warning && c.Advice is { } a ? $"<div class=\"note\">{E(a)}</div>" : "";
            html.Append($"<tr class=\"{SevClass(c.Severity)}\"><td class=\"icon-col\"><span class=\"dot\" aria-hidden=\"true\">{Icon(c.Severity)}</span>" +
                        $"<span class=\"sr\">{SevLabel(c.Severity)}</span></td><td>{E(c.Name)}</td><td>{E(c.Value)}{advice}</td></tr>\n");
        }

        html.Append("</tbody></table></section>\n");
    }

    private static void EnergyWindowTable(StringBuilder html, string heading, Admin.EnergyWindow window)
    {
        html.Append($"<section class=\"card\"><h2>{E(heading)}</h2>");
        html.Append($"<p class=\"muted\">From Windows' energy history{(window.OnBatteryOnly ? ", time on battery only" : " (the laptop was not on battery in this period)")}. " +
                    $"{E(Percent(window.UnassignedPercent, "0"))} of the energy could not be assigned to an app and is not included.</p>");
        html.Append("<table class=\"data apps\"><thead><tr><th>App</th><th class=\"num\">Share</th><th class=\"bar-col\"></th><th>Mostly</th><th class=\"num\">In background</th></tr></thead><tbody>\n");
        foreach (var app in window.Apps)
        {
            var note = app.Offender is { } o ? $"<div class=\"note sev-warning\"><span class=\"dot\" aria-hidden=\"true\">!</span>{E(o.Category)}</div>" : "";
            html.Append($"<tr><td>{E(app.Name)}{note}</td><td class=\"num\">{E(Percent(app.SharePercent, "0"))}</td>" +
                        $"<td class=\"bar-col\"><div class=\"bar\"><span style=\"width:{Math.Clamp(app.SharePercent, 0, 100).ToString("0.#", Inv)}%\"></span></div></td>" +
                        $"<td>{E(app.MainComponent)}</td><td class=\"num\">{E(Percent(app.BackgroundPercent, "0"))}</td></tr>\n");
        }

        html.Append("</tbody></table></section>\n");
    }

    private static void SleepTable(StringBuilder html, Admin.SleepStudy sleep)
    {
        html.Append("<section class=\"card\"><h2>Sleep (Modern Standby)</h2>");
        html.Append(sleep.JudgedPercentPerHour is { } rate
            ? $"<p class=\"muted\">On battery it used {E(Percent(rate))} of a charge per hour while asleep, over {E(Duration(sleep.BatterySleepTime))}. Deep sleep is the share of time in the lowest hardware power state; higher is better.</p>"
            : $"<p class=\"muted\">Only {E(Duration(sleep.BatterySleepTime))} of sleep on battery (sessions of 30 minutes or more): too little to judge, at least {E(Duration(Admin.SleepStudy.MinimumJudgedSleep))} is needed. Sessions are listed for reference.</p>");
        if (sleep.Sessions.Count == 0)
        {
            html.Append("</section>\n");
            return;
        }

        html.Append("<table class=\"data\"><thead><tr><th>Started</th><th class=\"num\">Length</th><th>Power</th><th class=\"num\">Used</th>" +
                    "<th class=\"num\">Per hour</th><th class=\"num\">Deep sleep</th><th>Kept awake most by</th></tr></thead><tbody>\n");
        foreach (var s in sleep.Sessions.Reverse().Take(20))
        {
            var top = s.TopBlockers.FirstOrDefault();
            html.Append($"<tr><td>{E(DateAndTime(s.Start))}</td><td class=\"num\">{E(Duration(s.Duration))}</td><td>{(s.OnAc ? "Plugged in" : "Battery")}</td>" +
                        $"<td class=\"num\">{E(s.EnergyUsed is { } u ? Number(u) + " mWh" : "-")}</td>" +
                        $"<td class=\"num\">{E(s.PercentPerHour is { } pph ? Percent(pph) : "-")}</td>" +
                        $"<td class=\"num\">{E(Percent(s.HardwareLowPowerPercent, "0"))}</td><td>{E(top?.Name ?? "-")}</td></tr>\n");
        }

        html.Append("</tbody></table></section>\n");
    }

    private static void EnergyDiagnosticsList(StringBuilder html, Admin.EnergyDiagnostics energy)
    {
        html.Append("<section class=\"card\"><h2>Windows energy diagnostics</h2>");
        html.Append($"<p class=\"muted\">{energy.ErrorCount} errors, {energy.WarningCount} warnings and {energy.InformationalCount} informational items " +
                    $"over {E(Duration(energy.Duration))}. Errors and warnings are listed; repeated items are grouped.</p><ul class=\"evidence\">");
        foreach (var f in energy.Findings)
        {
            var severity = f.Severity == Admin.EnergySeverity.Error ? Severity.Danger : Severity.Warning;
            var detail = f.Occurrences.Count switch
            {
                0 => "",
                1 => string.Join(", ", f.Occurrences[0].Take(2).Select(kv => $"{kv.Key}: {kv.Value}")),
                _ => $"{f.Occurrences.Count} times: " + string.Join(", ", f.Occurrences.Take(6).Select(o => o.FirstOrDefault().Value).Where(v => !string.IsNullOrEmpty(v))),
            };
            html.Append($"<li class=\"{SevClass(severity)}\"><span class=\"dot\" aria-hidden=\"true\">{Icon(severity)}</span>" +
                        $"<span class=\"sr\">{SevLabel(severity)}: </span><span><strong>{E(f.Name)}</strong>." +
                        (f.Description.Length > 0 ? $" {E(f.Description)}" : "") +
                        (detail.Length > 0 ? $" <span class=\"muted\">({E(detail)})</span>" : "") + "</span></li>\n");
        }

        html.Append("</ul></section>\n");
    }

    private static string Rate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024 * 1024 => (bytesPerSecond / 1024 / 1024).ToString("0.0", Inv) + " MB/s",
        >= 1024 => (bytesPerSecond / 1024).ToString("0", Inv) + " KB/s",
        _ => "0 KB/s",
    };

    private static void SystemTable(StringBuilder html, BatterySnapshot s) =>
        Table(html, "Laptop", [
            ("Manufacturer", s.System.Manufacturer),
            ("Model", s.System.DisplayModel),
            ("Family", s.System.SystemFamily),
            ("Serial number", s.System.SerialNumber),
            ("BIOS", s.System.BiosVersion),
            ("Type", s.System.PcSystemTypeName),
            ("Platform", s.Report?.System.PlatformRole is { } role
                ? role + (s.Report.System.ModernStandby is { } ms ? $", Modern Standby: {(ms ? "yes" : "no")}" : "")
                : null),
            ("Windows build", s.Report?.System.OsBuild),
            ("Checked as", s.IsAdministrator ? "Administrator" : "Standard user"),
        ]);

    private static void BatteryTable(StringBuilder html, BatteryInfo b, int count)
    {
        var unit = b.IsRelativeCapacity ? "units (relative)" : "mWh";
        Table(html, count > 1 ? $"Battery {b.Index} of {count}" : "Battery", [
            ("Name / part no.", b.Name),
            ("Manufacturer", b.Manufacturer),
            ("Serial number", b.SerialNumber),
            ("Chemistry", BatteryText.Chemistry(b)),
            ("Manufacture date", b.ManufactureDate),
            ("Design capacity", b.DesignCapacity is null ? null : $"{Number(b.DesignCapacity)} {unit}"),
            ("Full charge capacity", b.FullChargeCapacity is null ? null : $"{Number(b.FullChargeCapacity)} {unit}"),
            ("Health", BatteryText.Health(b.Health)),
            ("Cycle count", BatteryText.CycleCount(b.CycleCount)),
            ("Current charge", BatteryText.Charge(b.RemainingCapacity, b.ChargePercent, unit)),
            ("Voltage", b.VoltageMillivolts is { } mv ? $"{(mv / 1000.0).ToString("0.00", Inv)} V" : null),
            ("Power", BatteryText.PowerState(b)),
            ("Data from", BatteryText.Sources(b.Sources)),
        ]);
    }

    private static void CombinedTable(StringBuilder html, BatteryTotals t) =>
        Table(html, $"Combined ({t.Count} batteries)", [
            ("Design capacity", Capacity(t.DesignCapacity)),
            ("Full charge capacity", Capacity(t.FullChargeCapacity)),
            ("Health", BatteryText.Health(t.Health)),
            ("Current charge", BatteryText.Charge(t.RemainingCapacity, t.ChargePercent)),
        ]);

    private static void UsageTable(StringBuilder html, ReportDocument doc)
    {
        var u = doc.Usage;
        var est = doc.Snapshot.Report?.RuntimeEstimates;
        var rows = new List<(string, string?)>();

        if (est?.AtFullChargeCapacity is { } atFull)
        {
            rows.Add(("Active runtime estimate", $"{Duration(atFull.ActiveRuntime)} now, {Duration(est.AtDesignCapacity?.ActiveRuntime)} when new"));
            if (atFull.ModernStandbyRuntime is not null)
            {
                rows.Add(("Standby runtime estimate", $"{Duration(atFull.ModernStandbyRuntime)} now, {Duration(est.AtDesignCapacity?.ModernStandbyRuntime)} when new"));
            }
        }

        if (u.InUse is { } use)
        {
            rows.Add(("In use on battery", $"{Watts(use.AverageWatts)} average over {Duration(use.Duration)}"));
        }

        if (u.Asleep is { } sleep)
        {
            rows.Add(("Asleep on battery", $"{Watts(sleep.AverageWatts, "0.00")} average over {Duration(sleep.Duration)}" +
                                           (sleep.PercentPerHour is { } pph ? $" ({Percent(pph)} of a full charge per hour)" : "") +
                                           (u.AsleepIncludesHibernation ? "; all sleep, including hibernation" : "")));
        }

        if (doc.Drain is { } drain)
        {
            rows.Add(("Live drain test", drain.AverageWatts is { } w
                ? $"{Watts(w)} average over {Duration(drain.Duration)} ({drain.SampleCount} readings, {Watts(drain.MinWatts)} to {Watts(drain.MaxWatts)})" +
                  (doc.DrainProfile is { } profile ? $"; thresholds for a {profile.ToLowerInvariant()}" : "")
                : "Charger connected; power draw not measured"));
        }

        if (rows.Count > 0)
        {
            var period = u.From is not null ? $" ({Date(u.From)} to {Date(u.To)})" : "";
            Table(html, "Power use" + period, rows);
        }
    }

    private static void HistoryTable(StringBuilder html, IReadOnlyList<CapacityHistoryEntry> history)
    {
        html.Append($"""
            <section class="card"><details><summary><h2>Capacity history table ({history.Count} periods)</h2></summary>
            <table class="data"><thead><tr><th>Period start</th><th class="num">Full charge (mWh)</th><th class="num">Design (mWh)</th><th class="num">Health</th><th class="num">Cycles</th></tr></thead><tbody>

            """);
        foreach (var e in history.Reverse())
        {
            html.Append($"<tr><td>{E(Date(e.Start))}{(e.BatteryChanged ? " (battery replaced)" : "")}</td>" +
                        $"<td class=\"num\">{E(Number(e.FullChargeCapacity))}</td><td class=\"num\">{E(Number(e.DesignCapacity))}</td>" +
                        $"<td class=\"num\">{E(Percent(e.Health.HealthPercent))}</td><td class=\"num\">{E(e.CycleCount is > 0 ? Number(e.CycleCount) : "-")}</td></tr>\n");
        }

        html.Append("</tbody></table></details></section>\n");
    }

    private static void Notes(StringBuilder html, BatterySnapshot s)
    {
        html.Append("<section class=\"card\"><h2>Data collection notes</h2>");
        if (s.Log.Count == 0)
        {
            html.Append("<p class=\"muted\">All sources were read without problems.</p></section>\n");
            return;
        }

        html.Append("<ul class=\"notes\">");
        foreach (var e in s.Log)
        {
            html.Append($"<li><span class=\"muted\">{E(e.Level.ToString())} &middot; {E(e.Source)}:</span> {E(e.Message)}</li>");
        }

        html.Append("</ul></section>\n");
    }

    private static void Footer(StringBuilder html, ReportDocument doc)
    {
        html.Append("<footer>");
        html.Append($"<p class=\"muted\">Generated by {E(doc.Generator)} on {E(DateAndTime(doc.GeneratedAt))}. All data was collected on this computer; nothing was sent anywhere.</p>");
        html.Append("</footer>\n");
    }

    // ------------------------------------------------------------------ pieces

    private static void Table(StringBuilder html, string heading, IReadOnlyList<(string Label, string? Value)> rows)
    {
        html.Append($"<section class=\"card\"><h2>{E(heading)}</h2><table class=\"kv\"><tbody>\n");
        foreach (var (label, value) in rows)
        {
            html.Append($"<tr><th>{E(label)}</th><td>{(value is null ? "<span class=\"muted\">Not reported</span>" : E(value))}</td></tr>\n");
        }

        html.Append("</tbody></table></section>\n");
    }

    private static string Gauge(double? health, HealthLabel label)
    {
        const double R = 52, C = 2 * Math.PI * R;
        var fraction = Math.Clamp((health ?? 0) / 100.0, 0, 1);
        var cls = label switch
        {
            HealthLabel.Good => "sev-good",
            HealthLabel.Worn => "sev-warning",
            HealthLabel.Replace => "sev-danger",
            _ => "sev-neutral",
        };
        var text = health is { } h ? h.ToString("0", Inv) + "%" : "?";
        var word = label switch
        {
            HealthLabel.Good => "Good",
            HealthLabel.Worn => "Worn",
            HealthLabel.Replace => "Replace",
            _ => "Unknown",
        };

        return $"""
            <div class="gauge {cls}">
            <svg viewBox="0 0 128 128" role="img" aria-label="Battery health {E(text)}, {word}">
            <circle class="gauge-track" cx="64" cy="64" r="{R.ToString(Inv)}"/>
            <circle class="gauge-fill" cx="64" cy="64" r="{R.ToString(Inv)}" stroke-dasharray="{(C * fraction).ToString("0.##", Inv)} {C.ToString("0.##", Inv)}" transform="rotate(-90 64 64)"/>
            <text x="64" y="70" text-anchor="middle" class="gauge-value">{E(text)}</text>
            </svg>
            <div class="gauge-word"><span class="dot" aria-hidden="true">{Icon(LabelSeverity(label))}</span>{word}</div>
            </div>
            """;
    }

    private static Severity LabelSeverity(HealthLabel label) => label switch
    {
        HealthLabel.Good => Severity.Good,
        HealthLabel.Worn => Severity.Warning,
        HealthLabel.Replace => Severity.Danger,
        _ => Severity.Neutral,
    };

    private static double NiceStep(double raw)
    {
        if (raw <= 0)
        {
            return 1000;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;
        var nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 2.5 ? 2.5 : normalized <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    private static string SevClass(Severity s) => s switch
    {
        Severity.Good => "sev-good",
        Severity.Warning => "sev-warning",
        Severity.Danger => "sev-danger",
        _ => "sev-neutral",
    };

    private static string SevLabel(Severity s) => s switch
    {
        Severity.Good => "Good",
        Severity.Warning => "Attention",
        Severity.Danger => "Action needed",
        _ => "Info",
    };

    private static string Icon(Severity s) => s switch
    {
        Severity.Good => "&#10003;",     // check mark
        Severity.Warning => "!",
        Severity.Danger => "&#10005;",   // cross
        _ => "i",
    };

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    // Colours: plan.md section 5.1. Status colours only ever appear on bars, icons and marks; text stays in text colours.
    private const string Css = """

        :root {
          --window: #1e1f22; --panel: #2b2d31; --border: #3a3d43; --text: #e6e6e6; --muted: #8b8f97;
          --accent: #3b82f6; --accent-hover: #2563eb; --danger: #ef4444; --selection-text: #ffffff;
          --success: #22c55e; --warning: #f59e0b; --neutral: #8b8f97;
          color-scheme: dark;
        }
        * { box-sizing: border-box; }
        html, body { margin: 0; background: var(--window); color: var(--text); }
        body { font: 15px/1.5 "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif; }
        main { max-width: 980px; margin: 0 auto; padding: 24px 16px 48px; }
        h1, h2 { margin: 0; font-weight: 600; }
        h1 { font-size: 26px; line-height: 1.25; }
        h2 { font-size: 15px; color: var(--text); margin-bottom: 10px; }
        p { margin: 6px 0 0; }
        .muted { color: var(--muted); }
        .small { font-size: 13px; }
        .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }

        .top { display: flex; justify-content: space-between; gap: 16px; flex-wrap: wrap; align-items: flex-end; margin-bottom: 20px; }
        .brand { display: flex; gap: 12px; align-items: center; }
        .company { font-size: 20px; font-weight: 600; }
        .device { text-align: right; font-weight: 600; }
        .device .muted { font-weight: 400; }

        .card { background: var(--panel); border: 1px solid var(--border); border-radius: 6px; padding: 16px 18px; margin-bottom: 14px; }
        .section-title { font-size: 18px; margin: 32px 0 12px; }

        .verdict { display: flex; gap: 16px; align-items: flex-start; background: var(--panel); border: 1px solid var(--border);
                   border-left: 6px solid var(--sev); border-radius: 6px; padding: 18px 20px; margin-bottom: 14px; }
        .verdict-icon { flex: none; width: 40px; height: 40px; border-radius: 50%; background: var(--sev); color: #1e1f22;
                        display: grid; place-items: center; font-size: 22px; font-weight: 700; }
        .verdict-label { text-transform: uppercase; letter-spacing: .06em; font-size: 12px; color: var(--muted); font-weight: 600; }
        .verdict p { font-size: 16px; }

        .sev-good { --sev: var(--success); } .sev-warning { --sev: var(--warning); }
        .sev-danger { --sev: var(--danger); } .sev-neutral { --sev: var(--neutral); }

        .figures { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 14px; margin-bottom: 0; }
        .figure { text-align: center; }
        .big { font-size: 38px; font-weight: 600; line-height: 1.2; margin-top: 18px; }
        .gauge svg { width: 128px; height: 128px; display: block; margin: 0 auto; }
        .gauge-track { fill: none; stroke: var(--border); stroke-width: 12; }
        .gauge-fill { fill: none; stroke: var(--sev); stroke-width: 12; stroke-linecap: round; }
        .gauge-value { fill: var(--text); font-size: 30px; font-weight: 600; font-family: inherit; }
        .gauge-word { font-weight: 600; margin-top: 2px; }

        ol.actions { margin: 0; padding-left: 22px; } ol.actions li { margin: 4px 0; }
        ul.evidence, ul.notes { list-style: none; margin: 0; padding: 0; }
        ul.evidence li { display: flex; gap: 10px; align-items: baseline; margin: 6px 0; }
        .dot { flex: none; display: inline-grid; place-items: center; width: 18px; height: 18px; border-radius: 50%;
               background: var(--sev); color: #1e1f22; font-size: 11px; font-weight: 700; position: relative; top: 2px; margin-right: 6px; }
        ul.evidence .dot { margin-right: 0; }
        ul.notes li { margin: 4px 0; font-size: 14px; }

        .chart { width: 100%; height: auto; display: block; margin-top: 8px; }
        .chart .grid { stroke: var(--border); stroke-width: 1; }
        .chart .axis { fill: var(--muted); font-size: 12px; font-family: inherit; }
        .chart .label { fill: var(--muted); font-size: 12px; font-family: inherit; }
        .chart .label.strong { fill: var(--text); font-weight: 600; }
        .chart .design { stroke: var(--muted); stroke-width: 1.5; stroke-dasharray: 5 4; }
        .chart .event { stroke: var(--muted); stroke-width: 1; stroke-dasharray: 2 3; }
        .chart .line { fill: none; stroke: var(--accent); stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; }
        .chart .marker { fill: var(--accent); stroke: var(--panel); stroke-width: 2; }
        .chart .hit { fill: transparent; stroke: none; cursor: crosshair; }
        .chart .hit:hover { fill: var(--accent); stroke: var(--panel); stroke-width: 2; }

        table { width: 100%; border-collapse: collapse; font-size: 14px; }
        table.kv th { text-align: left; font-weight: 400; color: var(--muted); width: 38%; padding: 6px 12px 6px 0; vertical-align: top; }
        table.kv td { padding: 6px 0; }
        table.kv tr + tr th, table.kv tr + tr td { border-top: 1px solid var(--border); }
        table.data th { text-align: left; color: var(--muted); font-weight: 400; padding: 6px 8px; border-bottom: 1px solid var(--border); }
        table.data td { padding: 5px 8px; border-bottom: 1px solid var(--border); }
        .num { text-align: right; font-variant-numeric: tabular-nums; }
        table.apps td { vertical-align: top; }
        table.settings td { vertical-align: top; }
        table.settings .icon-col { width: 28px; }
        table.settings td:nth-child(2) { color: var(--muted); width: 34%; }
        .bar-col { width: 22%; }
        .bar { height: 8px; border-radius: 4px; background: var(--border); margin-top: 7px; overflow: hidden; }
        .bar span { display: block; height: 100%; border-radius: 4px; background: var(--accent); }
        .note { font-size: 13px; color: var(--muted); margin-top: 2px; }
        .note .dot { width: 14px; height: 14px; font-size: 9px; margin-right: 4px; top: 1px; }
        details summary { cursor: pointer; list-style: revert; }
        details summary h2 { display: inline; }
        details[open] summary { margin-bottom: 10px; }

        footer { margin-top: 28px; font-size: 13px; }

        @media (max-width: 600px) { .device { text-align: left; } .big { font-size: 32px; } h1 { font-size: 22px; } }

        @media print {
          :root { --window: #ffffff; --panel: #ffffff; --border: #d4d7dc; --text: #111111; --muted: #555a63;
                  --accent: #2563eb; --neutral: #6b7280; color-scheme: light; }
          body { font-size: 12px; }
          main { max-width: none; padding: 0; }
          .card, .verdict { break-inside: avoid; }
          details::details-content { content-visibility: visible; display: block; }
          .chart .hit { display: none; }
        }

        """;
}
