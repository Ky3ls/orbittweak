using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace OrbitTweak.Core;

/// <summary>Registry helper. Paths look like "HKLM\SOFTWARE\..." or "HKCU\...".</summary>
internal static class Reg
{
    private static RegistryKey Base(string path, out string sub)
    {
        var i = path.IndexOf('\\');
        var hive = path[..i].ToUpperInvariant();
        sub = path[(i + 1)..];
        var h = hive switch
        {
            "HKLM" => RegistryHive.LocalMachine,
            "HKCU" => RegistryHive.CurrentUser,
            _ => throw new ArgumentException("Unknown hive " + hive)
        };
        return RegistryKey.OpenBaseKey(h, RegistryView.Registry64);
    }

    public static object? Get(string path, string name)
    {
        try
        {
            using var b = Base(path, out var sub);
            using var k = b.OpenSubKey(sub);
            return k?.GetValue(name);
        }
        catch { return null; }
    }

    public static bool Is(string path, string name, object expected)
        => Get(path, name)?.ToString() == expected.ToString();

    public static void Set(string path, string name, object value, RegistryValueKind kind = RegistryValueKind.DWord)
    {
        using var b = Base(path, out var sub);
        using var k = b.CreateSubKey(sub, true);
        k.SetValue(name, value, kind);
    }

    public static void SetStr(string path, string name, string value) => Set(path, name, value, RegistryValueKind.String);

    public static void Del(string path, string name)
    {
        try
        {
            using var b = Base(path, out var sub);
            using var k = b.OpenSubKey(sub, true);
            k?.DeleteValue(name, false);
        }
        catch { }
    }

    public static string[] SubKeys(string path)
    {
        try
        {
            using var b = Base(path, out var sub);
            using var k = b.OpenSubKey(sub);
            return k?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }
}

internal static class Proc
{
    public static string Run(string file, string args, int timeoutMs = 90_000)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return "Timeout"; }
            return (o.Result + e.Result).Trim();
        }
        catch (Exception ex) { return ex.Message; }
    }

    public static void Open(string target, string? args = null)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = args ?? "" }); }
        catch { }
    }

    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

internal static class SysInfo
{
    public static string CpuName =>
        (Reg.Get(@"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") as string)?.Trim() ?? "Unbekannt";

    public static int Cores => Environment.ProcessorCount;

    public static string GpuName
    {
        get
        {
            const string cls = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            var names = new List<string>();
            foreach (var sk in Reg.SubKeys(cls))
            {
                if (!int.TryParse(sk, out _)) continue;
                var d = Reg.Get($@"{cls}\{sk}", "DriverDesc") as string;
                if (string.IsNullOrWhiteSpace(d) || d.Contains("Basic", StringComparison.OrdinalIgnoreCase)
                    || d.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || names.Contains(d)) continue;
                names.Add(d);
            }
            // Prefer dedicated GPUs
            var ded = names.FirstOrDefault(n => n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || n.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || n.Contains("Arc", StringComparison.OrdinalIgnoreCase));
            return ded ?? names.FirstOrDefault() ?? "Unbekannt";
        }
    }

    public static string OsName
    {
        get
        {
            const string k = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            var name = Reg.Get(k, "ProductName") as string ?? "Windows";
            var ver = Reg.Get(k, "DisplayVersion") as string ?? "";
            if (int.TryParse(Reg.Get(k, "CurrentBuildNumber") as string, out var build) && build >= 22000)
                name = name.Replace("Windows 10", "Windows 11");
            return $"{name} {ver}".Trim();
        }
    }

    public static string Bytes(double b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return i == 0 ? $"{b:0} {u[i]}" : $"{b:0.0} {u[i]}";
    }
}
