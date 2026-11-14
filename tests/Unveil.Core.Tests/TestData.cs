using Unveil.Core.Battery;
using Unveil.Core.SystemInfo;

namespace Unveil.Core.Tests;

/// <summary>Builders for snapshots used by verdict and report tests.</summary>
internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

    public static BatteryInfo MakeBattery(
        long? design = 50000,
        long? full = 45000,
        int? cycles = 200,
        int index = 1,
        string? name = "TEST-BATTERY",
        bool relative = false) => new()
        {
            Index = index,
            Sources = BatteryDataSources.Wmi | BatteryDataSources.BatteryReport,
            Name = name,
            Manufacturer = "Acme",
            ChemistryCode = "LION",
            DesignCapacity = design,
            FullChargeCapacity = full,
            CycleCount = cycles,
            IsRelativeCapacity = relative,
            RemainingCapacity = full / 2,
            ChargePercent = 50,
            PowerOnline = true,
            Charging = false,
            Discharging = false,
        };

    /// <summary>Battery whose health is exactly <paramref name="healthPercent"/>.</summary>
    public static BatteryInfo WithHealth(double healthPercent, int? cycles = 200, int index = 1) =>
        MakeBattery(design: 100000, full: (long)Math.Round(healthPercent * 1000), cycles: cycles, index: index);

    public static BatterySnapshot Snapshot(
        IReadOnlyList<BatteryInfo> batteries,
        BatteryReport? report = null,
        IReadOnlyList<string>? windowsStatus = null,
        int pcSystemType = 2) => new()
        {
            CollectedAt = Now,
            System = new SystemIdentity
            {
                Manufacturer = "Acme",
                Model = "Laptop 14",
                SerialNumber = "FAKE-SN-1",
                PcSystemType = pcSystemType,
            },
            Batteries = batteries,
            Combined = BatteryMerger.Combine(batteries),
            Report = report,
            WindowsBatteryStatus = windowsStatus ?? [],
        };

    public static BatterySnapshot Snapshot(params BatteryInfo[] batteries) => Snapshot((IReadOnlyList<BatteryInfo>)batteries);

    /// <summary>History with one entry per day, oldest first, ending the day before <see cref="Now"/>.</summary>
    public static IReadOnlyList<CapacityHistoryEntry> DailyHistory(params int[] fullChargeCapacities) =>
        fullChargeCapacities
            .Select((fcc, i) => new CapacityHistoryEntry
            {
                Start = Now.AddDays(i - fullChargeCapacities.Length),
                End = Now.AddDays(i - fullChargeCapacities.Length + 1).AddSeconds(-1),
                DesignCapacity = 50000,
                FullChargeCapacity = fcc,
                CycleCount = 100 + i,
            })
            .ToList();

    public static BatteryReport ReportWithHistory(IReadOnlyList<CapacityHistoryEntry> history) =>
        new() { CapacityHistory = history };

    public static BatteryReport AsusReport() =>
        BatteryReportParser.Load(TestFiles.Sample("battery-report", "asus-expertbook-b1403cva.xml"));
}
