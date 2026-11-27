using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Unveil.Core.Updates;

/// <summary>The newest release on GitHub, and the file for this PC in it.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string AssetName, string AssetUrl, string SumsUrl);

/// <summary>
/// "Check for updates": asks GitHub for the latest release, and on request downloads the exe for this PC, checks it
/// against the release's SHA256SUMS.txt, and puts it in place of the running Unveil.exe (Windows lets a running exe
/// be renamed, not overwritten: the old one becomes Unveil.exe.old and is deleted on the next start).
/// Nothing about the PC is sent: only a request for the release list, with Unveil's version as the user agent.
/// </summary>
public static class AppUpdate
{
    public const string Repo = "Z3R0ZIGMA/Unveil";

    public static string LatestReleaseApi => $"https://api.github.com/repos/{Repo}/releases/latest";

    /// <summary>The release file for this PC: the ARM64 build on Windows on ARM, else the x64 one.</summary>
    public static string AssetName(Architecture architecture) => architecture == Architecture.Arm64 ? "Unveil-arm64.exe" : "Unveil.exe";

    public static string AssetName() => AssetName(RuntimeInformation.OSArchitecture);

    /// <summary>
    /// Reads GitHub's "latest release" JSON. Null when it has no usable version or lacks the exe or the checksum file
    /// (a release must have both to be installed).
    /// </summary>
    public static ReleaseInfo? ParseLatest(string json, string assetName)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (tag is null || !Version.TryParse(tag.TrimStart('v', 'V'), out var version))
        {
            return null;
        }

        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean() || root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
        {
            return null;
        }

        string? Url(string name) => root.TryGetProperty("assets", out var assets)
            ? assets.EnumerateArray()
                .Where(a => string.Equals(a.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                .Select(a => a.GetProperty("browser_download_url").GetString())
                .FirstOrDefault()
            : null;

        var asset = Url(assetName);
        var sums = Url("SHA256SUMS.txt");
        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;
        return asset is null || sums is null || !IsGitHubDownload(asset) || !IsGitHubDownload(sums)
            ? null
            : new ReleaseInfo(version, tag, page ?? $"https://github.com/{Repo}/releases/latest", assetName, asset, sums);
    }

    /// <summary>Only files from this project's own releases are ever downloaded.</summary>
    private static bool IsGitHubDownload(string url) =>
        url.StartsWith($"https://github.com/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Compares major.minor.build (the exe's version also has a revision of 0).</summary>
    public static bool IsNewer(Version latest, Version current) =>
        new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build)) > new Version(current.Major, current.Minor, Math.Max(0, current.Build));

    /// <summary>The SHA-256 for <paramref name="assetName"/> in a SHA256SUMS.txt ("hash  name" per line), lower case.</summary>
    public static string? ExpectedHash(string sums, string assetName) =>
        sums.Split('\n')
            .Select(line => line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit)
                            && string.Equals(parts[1].TrimStart('*').Trim(), assetName, StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[0].ToLowerInvariant())
            .FirstOrDefault();

    public static HttpClient CreateClient(Version current)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Unveil/{current.ToString(3)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public static async Task<ReleaseInfo?> GetLatestAsync(HttpClient http, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await http.SendAsync(request, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        return ParseLatest(json, AssetName());
    }

    /// <summary>
    /// Downloads the release's exe to <paramref name="target"/> and checks it against SHA256SUMS.txt. The file is
    /// deleted, and an <see cref="InvalidDataException"/> thrown, when it does not match.
    /// </summary>
    public static async Task DownloadAsync(HttpClient http, ReleaseInfo release, string target, IProgress<double>? progress, CancellationToken ct)
    {
        var sums = await http.GetStringAsync(release.SumsUrl, ct).ConfigureAwait(false);
        var expected = ExpectedHash(sums, release.AssetName)
            ?? throw new InvalidDataException($"{release.AssetName} is not listed in the release's SHA256SUMS.txt.");

        using (var response = await http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total is > 0)
                {
                    progress?.Report(100.0 * done / total.Value);
                }
            }
        }

        string actual;
        await using (var check = File.OpenRead(target))
        {
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(check, ct).ConfigureAwait(false));
        }

        if (actual != expected)
        {
            File.Delete(target);
            throw new InvalidDataException("The download does not match the release's SHA256SUMS.txt, so it was not used.");
        }
    }

    /// <summary>
    /// Puts <paramref name="newExe"/> in place of the running exe: the running one is renamed to "*.old" (Windows
    /// allows that for a running exe) and the new one moved to its name. Undone if the move fails.
    /// </summary>
    public static void Install(string newExe, string exePath)
    {
        var old = exePath + ".old";
        if (File.Exists(old))
        {
            File.Delete(old);
        }

        File.Move(exePath, old);
        try
        {
            File.Move(newExe, exePath);
        }
        catch
        {
            File.Move(old, exePath);
            throw;
        }
    }

    /// <summary>Removes the previous version left by an update (it was still running then). Best effort.</summary>
    public static void CleanUpOldVersion(string? exePath)
    {
        if (exePath is null)
        {
            return;
        }

        foreach (var leftover in new[] { exePath + ".old", exePath + ".new" })
        {
            try
            {
                if (File.Exists(leftover))
                {
                    File.Delete(leftover);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still in use (the old version has not quite exited): next start.
            }
        }
    }
}
