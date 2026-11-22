using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrbitTweak.Core;

namespace OrbitTweak;

/// <summary>Absturz-Analyse (2.31) on the Diagnose page: FiveM's last crashes with their likely cause, the fixes that
/// apply on this PC and a short text for support. Reading is in Core/CrashScan.cs, the rules in Core/CrashScanLogic.cs.</summary>
public partial class MainWindow
{
    private static readonly Regex CrashFileName = new(@"^[\w\-. ]{1,120}\.(?:dmp|log)$", RegexOptions.IgnoreCase);

    private async Task<object?> CrashCmd(string cmd, JsonElement a)
    {
        switch (cmd)
        {
            // -> { found, items: [{ id, time (UTC, ISO), build, server, title, signature, module, code, text, legacyHash,
            //      reportId, stack[], dump, log, cause: { key, title, why, steps[], sure, icon }, fixes: [{ id, label, kind, arg }],
            //      support }], pattern: { key, title, count, of } | null, logs, dumps } or { error }
            case "crash.list":
                try { return await Task.Run(CrashList); }
                catch (Exception)
                {
                    return new { error = "FiveMs Absturz-Daten ließen sich nicht lesen. Versuch es gleich noch einmal." };
                }

            // { fix: "driver:nvidia|amd|intel" | "plugins" } -> { ok, error? }
            case "crash.fix":
            {
                var fix = Str(a, "fix");
                if (fix.StartsWith("driver:", StringComparison.Ordinal))
                {
                    var url = CrashScanLogic.DriverUrl(fix[7..]);
                    if (url == null) return new { ok = false, error = "Diesen Grafikkarten-Hersteller kennt OrbitTweak nicht." };
                    Proc.Open(url);
                    return new { ok = true };
                }
                if (fix == "plugins")
                {
                    var dir = CrashScan.PluginDir;
                    if (dir == null || !Directory.Exists(dir)) return new { ok = false, error = "In FiveM.app gibt es keinen Plugins-Ordner." };
                    Proc.Open(dir);
                    return new { ok = true };
                }
                return new { ok = false, error = "Diese Aktion gibt es nicht." };
            }

            // { dump } or { log }: a plain file name in FiveM.app\crashes or \logs -> { ok, error? }; shows it in Explorer
            case "crash.reveal":
            {
                var name = Str(a, "dump");
                var dir = CrashScan.CrashDir;
                if (name.Length == 0) { name = Str(a, "log"); dir = CrashScan.LogDir; }
                if (dir == null || !CrashFileName.IsMatch(name) || name.Contains("..")) return new { ok = false, error = "Diese Datei gibt es nicht." };
                var file = Path.Combine(dir, name);
                if (!File.Exists(file)) return new { ok = false, error = "Die Datei ist nicht mehr da – FiveM behält nur die letzten zehn Dumps." };
                try { Process.Start("explorer.exe", $"/select,\"{file}\"")?.Dispose(); }
                catch (System.ComponentModel.Win32Exception) { return new { ok = false, error = "Der Explorer ließ sich nicht öffnen." }; }
                return new { ok = true };
            }
        }
        throw new InvalidOperationException("Unbekannter Befehl: " + cmd);
    }

    private object CrashList()
    {
        var pc = new CrashPc(
            GpuVendor: CrashScanLogic.VendorOf(SysInfo.GpuName),
            PagefileOk: _tweaks.FirstOrDefault(t => t.Id == "pagefile") is not { } pf || pf.SafeCheck(),
            HagsOn: _tweaks.FirstOrDefault(t => t.Id == "hags") is { } hg && hg.SafeCheck(),
            TexRaised: TexRaised(),
            HasPlugins: CrashScan.PluginDir is { } pd && Directory.Exists(pd),
            NowUtc: DateTime.UtcNow);
        var res = CrashScan.Read();
        string user = Environment.UserName, profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var rows = res.Crashes.Select(r => (r, c: CrashScanLogic.Analyze(r, pc))).ToList();
        var items = rows.Select(x => new
        {
            id = CrashScanLogic.IdOf(x.r),
            time = x.r.TimeUtc?.ToString("o"),
            build = x.r.Build,
            server = x.r.Server,
            title = x.r.Title,
            signature = x.r.Signature,
            module = x.r.Module,
            code = x.r.Code,
            text = x.r.Text,
            legacyHash = x.r.LegacyHash,
            reportId = x.r.ReportId,
            stack = x.r.Stack,
            dump = x.r.Dump,
            log = x.r.Log,
            cause = new { key = x.c.Key, title = x.c.Title, why = x.c.Why, steps = x.c.Steps, sure = x.c.Sure, icon = x.c.Icon },
            fixes = x.c.Fixes.Select(f => new { id = f.Id, label = f.Label, kind = f.Kind, arg = f.Arg }),
            support = CrashScanLogic.SupportText(x.r, x.c, x.r.TimeUtc?.ToLocalTime(), user, profile),
        }).ToList();
        var pat = CrashScanLogic.Pattern(rows.Select(x => (x.r.TimeUtc, x.c)).ToList(), DateTime.UtcNow);
        return new
        {
            found = res.Found,
            items,
            pattern = pat is { } p ? new { key = p.key, title = p.title, count = p.count, of = p.of } : null,
            logs = res.Logs,
            dumps = res.Dumps,
        };
    }

    /// <summary>Whether FiveM's texture budget (vid_budgetScale in fivem.cfg) is above its default 0.</summary>
    private static bool TexRaised()
    {
        try
        {
            var f = TexBudget.CfgPath;
            return File.Exists(f) && TexBudgetLogic.Read(Encoding.Latin1.GetString(File.ReadAllBytes(f))).value > 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
