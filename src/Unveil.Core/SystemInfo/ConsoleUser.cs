using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace Unveil.Core.SystemInfo;

/// <summary>
/// The person signed in at the laptop's screen, found from the system account (N-sight runs Unveil as Local System):
/// their SID (to read their loaded registry hive) and profile folder. The name itself is never kept or reported.
/// </summary>
public static class ConsoleUser
{
    public sealed record Info(string Sid, string? ProfilePath);

    public static Info? Find()
    {
        var sid = FromConsoleSession() ?? OnlyLoadedUserHive();
        return sid is null ? null : new Info(sid, ProfilePath(sid));
    }

    /// <summary>The user of the active console session, translated to a SID.</summary>
    private static string? FromConsoleSession()
    {
        try
        {
            var session = WTSGetActiveConsoleSessionId();
            if (session == 0xFFFFFFFF)
            {
                return null;
            }

            var user = Query(session, WtsUserName);
            var domain = Query(session, WtsDomainName);
            if (string.IsNullOrEmpty(user))
            {
                return null;
            }

            var account = string.IsNullOrEmpty(domain) ? new NTAccount(user) : new NTAccount(domain, user);
            return ((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier))).Value;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            return null;
        }
    }

    /// <summary>Fallback: if exactly one person's registry hive is loaded, that is the signed-in user.</summary>
    private static string? OnlyLoadedUserHive()
    {
        try
        {
            var users = Registry.Users.GetSubKeyNames()
                .Where(n => (n.StartsWith("S-1-5-21-", StringComparison.Ordinal) || n.StartsWith("S-1-12-1-", StringComparison.Ordinal))
                            && !n.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return users.Count == 1 ? users[0] : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string? ProfilePath(string sid)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid);
            return key?.GetValue("ProfileImagePath") is string path ? Environment.ExpandEnvironmentVariables(path) : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private const int WtsUserName = 5;
    private const int WtsDomainName = 7;

    private static string? Query(uint session, int infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, session, infoClass, out var buffer, out _))
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WTSQuerySessionInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
