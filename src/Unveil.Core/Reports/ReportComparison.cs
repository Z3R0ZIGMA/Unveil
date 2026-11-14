using System.Text.Json;
using Unveil.Core.Battery;
using static Unveil.Core.Formatting;

namespace Unveil.Core.Reports;

/// <summary>The few figures Unveil needs from an earlier JSON report to compare with (plan.md section 6, "Comparison").</summary>
public sealed record PreviousReport
{
    public DateTimeOffset GeneratedAt { get; init; }

    /// <summary>e.g. "Unveil 0.7.0".</summary>
    public string? Generator { get; init; }

    /// <summary>File name only (never the full path, which can contain the user name).</summary>
    public string? FileName { get; init; }

    public string? LaptopModel { get; init; }

    public string? LaptopSerial { get; init; }

    public IReadOnlyList<PreviousBattery> Batteries { get; init; } = [];

    /// <summary>Verdict badge text at the time, e.g. "Battery worn".</summary>
    public string? VerdictTitle { get; init; }

    /// <summary>Average draw in the live test, when one was run on battery.</summary>
    public double? TestWatts { get; init; }
}

public sealed record PreviousBattery
{
    public int Index { get; init; }

    public string? Name { get; init; }

    public string? SerialNumber { get; init; }

    public long? DesignCapacity { get; init; }

    public long? FullChargeCapacity { get; init; }

    public bool IsRelativeCapacity { get; init; }

    public int? CycleCount { get; init; }

    public HealthResult Health => BatteryHealth.Calculate(DesignCapacity, FullChargeCapacity, IsRelativeCapacity);
}

/// <summary>
/// Reads an exported Unveil JSON report. Only the fields needed for comparison are read, by name, so reports from older
/// and newer versions still load; anything missing is just null.
/// </summary>
public static class PreviousReportReader
{
    public static PreviousReport Read(string json, string? fileName = null)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The file is not valid JSON.", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                Get(root, "snapshot") is not { ValueKind: JsonValueKind.Object } snapshot ||
                Get(root, "generatedAt") is not { ValueKind: JsonValueKind.String } generated ||
                !generated.TryGetDateTimeOffset(out var generatedAt))
            {
                throw new InvalidDataException("The file is not an Unveil report.");
            }

            var system = Get(snapshot, "system");
            var batteries = new List<PreviousBattery>();
            if (Get(snapshot, "batteries") is { ValueKind: JsonValueKind.Array } list)
            {
                var position = 0;
                foreach (var b in list.EnumerateArray())
                {
                    position++;
                    if (b.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    batteries.Add(new PreviousBattery
                    {
                        Index = (int?)Long(b, "index") ?? position,
                        Name = Text(b, "name"),
                        SerialNumber = Text(b, "serialNumber"),
                        DesignCapacity = Long(b, "designCapacity"),
                        FullChargeCapacity = Long(b, "fullChargeCapacity"),
                        IsRelativeCapacity = Get(b, "isRelativeCapacity") is { ValueKind: JsonValueKind.True },
                        CycleCount = (int?)Long(b, "cycleCount"),
                    });
                }
            }

            var drain = Get(root, "drain");
            return new PreviousReport
            {
                GeneratedAt = generatedAt,
                Generator = Text(root, "generator"),
                FileName = fileName,
                LaptopModel = system is { } s1 ? Text(s1, "displayModel") ?? Text(s1, "model") : null,
                LaptopSerial = system is { } s2 ? Text(s2, "serialNumber") : null,
                Batteries = batteries,
                VerdictTitle = Get(root, "verdict") is { } v ? Text(v, "title") : null,
                TestWatts = drain is { } d ? Double(d, "averageWatts") : null,
            };
        }
    }

    public static PreviousReport ReadFile(string path) => Read(File.ReadAllText(path), Path.GetFileName(path));

    private static JsonElement? Get(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    private static string? Text(JsonElement e, string name) =>
        Get(e, name) is { ValueKind: JsonValueKind.String } v && v.GetString() is { } s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    private static long? Long(JsonElement e, string name) =>
        Get(e, name) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out var n) ? n : null;

    private static double? Double(JsonElement e, string name) =>
        Get(e, name) is { ValueKind: JsonValueKind.Number } v && v.TryGetDouble(out var n) ? n : null;
}

public enum ComparisonMatch
{
    /// <summary>The same battery (matched on battery serial, or on laptop serial when the battery has none).</summary>
    SameBattery,

    /// <summary>Same laptop, but the battery serial has changed: the battery was replaced since.</summary>
    BatteryReplaced,

    /// <summary>A different laptop or battery: nothing to compare.</summary>
    NoMatch,
}

/// <summary>One battery then and now.</summary>
public sealed record BatteryChange
{
    public int Index { get; init; }

    public string? Name { get; init; }

    /// <summary>False when the battery has no serial and was matched by the laptop serial and position.</summary>
    public bool MatchedBySerial { get; init; }

    public double? HealthBefore { get; init; }

    public double? HealthNow { get; init; }

    /// <summary>Health percentage points (negative = lost capacity). Null when either side is unknown.</summary>
    public double? HealthChange { get; init; }

    /// <summary>Health points per 30 days; only when the reports are at least 14 days apart.</summary>
    public double? HealthChangePerMonth { get; init; }

    public long? FullChargeBefore { get; init; }

    public long? FullChargeNow { get; init; }

    /// <summary>mWh. Null when either side is missing or the units differ.</summary>
    public long? FullChargeChange { get; init; }

    public int? CyclesBefore { get; init; }

    public int? CyclesNow { get; init; }

    public int? CyclesChange { get; init; }
}

/// <summary>This check compared with an earlier report for the same laptop.</summary>
public sealed record ReportComparison
{
    public ComparisonMatch Match { get; init; }

    public DateTimeOffset PreviousGeneratedAt { get; init; }

    public string? PreviousFileName { get; init; }

    public string? PreviousGenerator { get; init; }

    /// <summary>Days between the two reports.</summary>
    public double Days { get; init; }

    /// <summary>The batteries matched to the earlier report (for BatteryReplaced: the new battery against the old one).</summary>
    public IReadOnlyList<BatteryChange> Batteries { get; init; } = [];

    public string? VerdictBefore { get; init; }

    public string? VerdictNow { get; init; }

    public double? TestWattsBefore { get; init; }

    public double? TestWattsNow { get; init; }

    /// <summary>One plain sentence for the dashboard, report and ticket summary.</summary>
    public required string Summary { get; init; }

    /// <summary>Why something could not be compared, or how batteries were matched.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Then / now / change rows for the dashboard and the HTML report.</summary>
    public IReadOnlyList<ComparisonRow> Rows()
    {
        var rows = new List<ComparisonRow>();
        var replaced = Match == ComparisonMatch.BatteryReplaced;
        foreach (var b in Batteries)
        {
            var prefix = Batteries.Count > 1 ? $"Battery {b.Index}: " : "";
            rows.Add(new($"{prefix}{(replaced ? "Health (old battery / new)" : "Health")}",
                Percent(b.HealthBefore), Percent(b.HealthNow),
                b.HealthChange is { } h ? ReportComparer.Signed(h) + " points" : "-"));
            rows.Add(new($"{prefix}Full charge",
                Capacity(b.FullChargeBefore), Capacity(b.FullChargeNow),
                b.FullChargeChange is { } f ? ReportComparer.Signed(f) + " mWh" : "-"));
            if (b.CyclesBefore is not null || b.CyclesNow is not null)
            {
                rows.Add(new($"{prefix}Charge cycles",
                    Number(b.CyclesBefore), Number(b.CyclesNow),
                    b.CyclesChange is { } c ? ReportComparer.Signed(c) : "-"));
            }
        }

        if (VerdictBefore is not null || VerdictNow is not null)
        {
            rows.Add(new("Verdict", VerdictBefore ?? "-", VerdictNow ?? "-", ""));
        }

        if (TestWattsBefore is not null || TestWattsNow is not null)
        {
            rows.Add(new("Live test power draw",
                TestWattsBefore is { } wb ? Watts(wb) : "not run",
                TestWattsNow is { } wn ? Watts(wn) : "not run",
                TestWattsBefore is { } a && TestWattsNow is { } n ? ReportComparer.Signed(n - a) + " W" : ""));
        }

        return rows;
    }
}

/// <summary>One line of the comparison table.</summary>
public sealed record ComparisonRow(string Label, string Before, string Now, string Change);

/// <summary>Compares this check with an earlier report. Pure: no files, no clock.</summary>
public static class ReportComparer
{
    /// <summary>Below this many days a monthly rate would be noise.</summary>
    public const double MinimumDaysForRate = 14;

    public static ReportComparison Compare(ReportDocument current, PreviousReport previous)
    {
        var s = current.Snapshot;
        var days = Math.Max(0, (current.GeneratedAt - previous.GeneratedAt).TotalDays);
        var notes = new List<string>();
        var sameLaptop = SerialsMatch(s.System.SerialNumber, previous.LaptopSerial);
        var laptopSerialsDiffer = IsRealSerial(s.System.SerialNumber) && IsRealSerial(previous.LaptopSerial) && !sameLaptop;

        // Pair batteries: by battery serial first; by position when a serial is missing and the laptop is the same.
        var pairs = new List<(BatteryInfo Now, PreviousBattery Before, bool BySerial)>();
        var replaced = new List<(BatteryInfo Now, PreviousBattery Before)>();
        var unused = previous.Batteries.ToList();
        foreach (var b in s.Batteries)
        {
            if (unused.FirstOrDefault(p => SerialsMatch(p.SerialNumber, b.SerialNumber)) is { } bySerial)
            {
                pairs.Add((b, bySerial, true));
                unused.Remove(bySerial);
            }
        }

        foreach (var b in s.Batteries.Where(b => pairs.All(p => p.Now != b)))
        {
            var samePosition = unused.FirstOrDefault(p => p.Index == b.Index) ?? (unused.Count == 1 && s.Batteries.Count == 1 ? unused[0] : null);
            if (samePosition is null || !sameLaptop)
            {
                continue;
            }

            unused.Remove(samePosition);
            if (IsRealSerial(b.SerialNumber) && IsRealSerial(samePosition.SerialNumber))
            {
                replaced.Add((b, samePosition));
            }
            else
            {
                pairs.Add((b, samePosition, false));
                notes.Add($"Battery {b.Index} has no serial number in one of the reports; matched by the laptop serial number.");
            }
        }

        var match = pairs.Count > 0 ? ComparisonMatch.SameBattery
            : replaced.Count > 0 ? ComparisonMatch.BatteryReplaced
            : ComparisonMatch.NoMatch;

        if (match == ComparisonMatch.SameBattery && laptopSerialsDiffer)
        {
            notes.Add("The laptop serial number is different: the battery has been moved to another laptop.");
        }

        if (match == ComparisonMatch.SameBattery && replaced.Count > 0)
        {
            notes.Add($"{replaced.Count} other {(replaced.Count == 1 ? "battery has" : "batteries have")} been replaced since.");
        }

        var changes = match switch
        {
            ComparisonMatch.SameBattery => pairs.OrderBy(p => p.Now.Index).Select(p => Change(p.Now, p.Before, p.BySerial, days)).ToList(),
            ComparisonMatch.BatteryReplaced => replaced.OrderBy(p => p.Now.Index).Select(p => Change(p.Now, p.Before, true, days) with
            {
                HealthChange = null,
                HealthChangePerMonth = null,
                FullChargeChange = null,
                CyclesChange = null,
            }).ToList(),
            _ => [],
        };

        return new ReportComparison
        {
            Match = match,
            PreviousGeneratedAt = previous.GeneratedAt,
            PreviousFileName = previous.FileName,
            PreviousGenerator = previous.Generator,
            Days = Math.Round(days, 1),
            Batteries = changes,
            VerdictBefore = previous.VerdictTitle,
            VerdictNow = current.Verdict.Title,
            TestWattsBefore = previous.TestWatts,
            TestWattsNow = current.Drain?.AverageWatts,
            Summary = Summarize(match, changes, previous, days, laptopSerialsDiffer),
            Notes = notes,
        };
    }

    private static BatteryChange Change(BatteryInfo now, PreviousBattery before, bool bySerial, double days)
    {
        var hBefore = before.Health.HealthPercent;
        var hNow = now.Health.HealthPercent;
        double? change = hBefore is { } a && hNow is { } b ? Math.Round(b - a, 1, MidpointRounding.AwayFromZero) : null;
        var sameUnits = before.IsRelativeCapacity == now.IsRelativeCapacity;
        return new BatteryChange
        {
            Index = now.Index,
            Name = now.Name ?? before.Name,
            MatchedBySerial = bySerial,
            HealthBefore = hBefore,
            HealthNow = hNow,
            HealthChange = change,
            HealthChangePerMonth = change is { } c && days >= MinimumDaysForRate ? Math.Round(c * 30 / days, 1, MidpointRounding.AwayFromZero) : null,
            FullChargeBefore = before.FullChargeCapacity,
            FullChargeNow = now.FullChargeCapacity,
            FullChargeChange = sameUnits && before.FullChargeCapacity is { } fb && now.FullChargeCapacity is { } fn ? fn - fb : null,
            CyclesBefore = before.CycleCount is > 0 ? before.CycleCount : null,
            CyclesNow = now.CycleCount is > 0 ? now.CycleCount : null,
            CyclesChange = before.CycleCount is int cb and > 0 && now.CycleCount is int cn and > 0 ? cn - cb : null,
        };
    }

    private static string Summarize(ComparisonMatch match, IReadOnlyList<BatteryChange> changes, PreviousReport previous, double days, bool laptopSerialsDiffer)
    {
        var when = $"{Date(previous.GeneratedAt)} ({Ago(days)})";
        switch (match)
        {
            case ComparisonMatch.NoMatch:
                return laptopSerialsDiffer
                    ? $"The report from {Date(previous.GeneratedAt)} is for a different laptop{(previous.LaptopModel is { } m ? $" ({m})" : "")}."
                    : $"The report from {Date(previous.GeneratedAt)} is for a different battery or laptop.";
            case ComparisonMatch.BatteryReplaced:
                var old = changes[0];
                return $"The battery has been replaced since {when}" +
                       (old.HealthBefore is { } hb ? $": the old one was at {Percent(hb, "0")} health" : "") +
                       (old.HealthNow is { } hn ? $", this one is at {Percent(hn, "0")}." : ".");
        }

        var main = changes.Count == 1 ? changes[0] : changes.FirstOrDefault(c => c.HealthChange is not null) ?? changes[0];
        var prefix = changes.Count > 1 ? $"Battery {main.Index}: " : "";
        if (main.HealthChange is not { } change)
        {
            return main.FullChargeChange is { } mwh
                ? $"Since {when}: {prefix}full charge {Signed(mwh)} mWh (health could not be measured)."
                : $"Since {when}: {prefix}health could not be measured in both reports.";
        }

        var trend = Math.Abs(change) < 0.5 ? "about the same" : change < 0 ? $"down {Points(-change)}" : $"up {Points(change)}";
        var rate = main.HealthChangePerMonth is { } perMonth && change <= -0.5 ? $", about {Points(-perMonth)} a month" : "";
        return $"Since {when}: {prefix}health {Percent(main.HealthBefore, "0")} -> {Percent(main.HealthNow, "0")} ({trend}{rate}).";
    }

    private static string Ago(double days) => days switch
    {
        < 1 => "earlier today",
        < 1.5 => "1 day ago",
        < 60 => $"{Math.Round(days).ToString("0", Inv)} days ago",
        _ => $"{Math.Round(days / 30.44).ToString("0", Inv)} months ago",
    };

    private static string Points(double value) => $"{value.ToString("0.0", Inv)} point{(Math.Abs(value - 1) < 0.05 ? "" : "s")}";

    public static string Signed(double value, string format = "0.0") => (value > 0 ? "+" : value < 0 ? "-" : "") + Math.Abs(value).ToString(format, Inv);

    public static string Signed(long value) => (value > 0 ? "+" : value < 0 ? "-" : "") + Number(Math.Abs(value));

    /// <summary>False for missing serials and OEM placeholders like "0", "000000" or "SerialNumber".</summary>
    public static bool IsRealSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return false;
        }

        var s = serial.Trim();
        return s.Distinct().Count() > 1 &&
               !s.Equals("SerialNumber", StringComparison.OrdinalIgnoreCase) &&
               !s.Equals("Default string", StringComparison.OrdinalIgnoreCase) &&
               !s.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) &&
               !s.Equals("123456789", StringComparison.Ordinal);
    }

    private static bool SerialsMatch(string? a, string? b) =>
        IsRealSerial(a) && IsRealSerial(b) && string.Equals(a!.Trim(), b!.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Looks for the most recent earlier report for this laptop among exported <c>unveil-*.json</c> files, so a returning
/// technician sees the change without having to pick a file.
/// </summary>
public static class PreviousReportFinder
{
    public const string FilePattern = "unveil-*.json";
    private const long MaxFileBytes = 50L * 1024 * 1024;
    private const int MaxFiles = 400;

    /// <param name="folders">Folders to look in, with how many levels of subfolders to search (0 = that folder only).</param>
    /// <param name="before">Only reports generated before this (so reports from this session are skipped).</param>
    public static (PreviousReport Report, ReportComparison Comparison)? FindLatest(
        IEnumerable<(string Folder, int Depth)> folders, ReportDocument current, DateTimeOffset before, CollectionLog? log = null)
    {
        (PreviousReport Report, ReportComparison Comparison)? best = null;
        foreach (var path in Candidates(folders))
        {
            PreviousReport report;
            try
            {
                report = PreviousReportReader.ReadFile(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                continue; // not an Unveil report, or unreadable: not worth a note
            }

            if (report.GeneratedAt >= before || (best is { } b && report.GeneratedAt <= b.Report.GeneratedAt))
            {
                continue;
            }

            var comparison = ReportComparer.Compare(current, report);
            if (comparison.Match != ComparisonMatch.NoMatch)
            {
                best = (report, comparison);
            }
        }

        if (best is { } found)
        {
            log?.Info("Comparison", $"Found an earlier report for this laptop: {found.Report.FileName} ({Date(found.Report.GeneratedAt)}).");
        }

        return best;
    }

    private static IEnumerable<string> Candidates(IEnumerable<(string Folder, int Depth)> folders)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var (folder, depth) in folders)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                continue;
            }

            var options = new System.IO.EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = depth > 0,
                MaxRecursionDepth = Math.Max(0, depth),
                AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
            };

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, FilePattern, options).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!seen.Add(Path.GetFullPath(file)))
                {
                    continue;
                }

                long size;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (size > MaxFileBytes)
                {
                    continue;
                }

                yield return file;
                if (++count >= MaxFiles)
                {
                    yield break;
                }
            }
        }
    }
}
