using System.IO;
using System.Text.Json;
using OrbitTweak.Core;

namespace OrbitTweak;

/// <summary>Einstellungs-Backup (2.31, Settings › Sicherheit): FiveM's key bindings, graphics and CitizenFX.ini as a zip
/// under %AppData%\OrbitTweak\backups, written back on request. The work is in Core/CfgBackup.cs, the rules in
/// Core/CfgBackupLogic.cs. Every file operation runs on the thread pool.</summary>
public partial class MainWindow
{
    private bool? _bkFmBefore;   // FiveM seen open on the probe before (null: not looked yet)
    private int _bkAutoBusy;

    private async Task<object?> CfgBackupCmd(string cmd, JsonElement a)
    {
        try
        {
            switch (cmd)
            {
                // -> { auto, lastAuto, keepAuto, dir, running, sources: [{ key, name, what, path, exists, size, modified, note }],
                //      items: [{ name, created, kind, kindLabel, size, files: [{ key, name, what, size }], ok, error }] }
                case "bk.state":
                    return await Task.Run(() =>
                    {
                        var roots = CfgBackup.Roots();
                        var st = CfgBackup.LoadState();
                        return (object)new
                        {
                            auto = st.Auto, lastAuto = st.LastAuto, keepAuto = CfgBackupLogic.KeepAuto,
                            dir = CfgBackupLogic.ToPlaceholder(CfgBackup.Dir, roots) ?? @"OrbitTweak\backups",
                            running = FiveM.IsRunning,
                            sources = CfgBackup.Sources(roots),
                            items = CfgBackup.List(),
                        };
                    });

                // -> { item, skipped: [{ name, why }] }
                case "bk.create":
                {
                    var (item, skipped) = await Task.Run(() => CfgBackup.Create(CfgBackupLogic.Manual));
                    Log($"🗂 FiveM-Einstellungen gesichert ({item.Files.Count} {(item.Files.Count == 1 ? "Datei" : "Dateien")})");
                    return new { item, skipped };
                }

                // { name, keys?: [] } -> { restored: [], unchanged: [], skipped: [{ name, why }], failed: [{ name, why }], safety, safetyNew,
                //                          undo: [keys "Rückgängig" writes back from safety], created: [names that were not there before] }
                // (FiveM running, a broken backup or one that cannot be checked: a German error, nothing written)
                case "bk.restore":
                {
                    var name = Str(a, "name");
                    var keys = new List<string>();
                    if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("keys", out var k) && k.ValueKind == JsonValueKind.Array)
                        foreach (var x in k.EnumerateArray()) if (x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 and < 40 } s) keys.Add(s);
                    var r = await Task.Run(() => CfgBackup.Restore(name, keys));
                    if (r.Restored.Count > 0) Log($"🗂 FiveM-Einstellungen wiederhergestellt: {string.Join(", ", r.Restored)}");
                    return r;
                }

                // { name } -> { ok }
                case "bk.delete":
                {
                    var name = Str(a, "name");
                    await Task.Run(() => CfgBackup.Delete(name));
                    return new { ok = true };
                }

                // { on } -> { auto }
                case "bk.auto":
                {
                    bool on = Bool(a, "on");
                    var now = await Task.Run(() => CfgBackup.SetAuto(on));
                    return new { auto = now };
                }

                // shows the backup folder in Explorer (made first, so it opens even before the first backup)
                case "bk.folder":
                {
                    var dir = CfgBackup.Dir;
                    await Task.Run(() => Directory.CreateDirectory(dir));
                    Proc.Open(dir);
                    return new { ok = true };
                }
            }
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) { throw new InvalidOperationException(CfgBackup.Reason(ex)); }
        throw new InvalidOperationException("Unbekannter Befehl: " + cmd);
    }

    /// <summary>Called after every FiveM probe (UI thread). FiveM just started: the automatic backup, at most once a
    /// day and only when something changed (CfgBackup.AutoRun), on the thread pool.</summary>
    private void CfgBackupTick(bool fmOpen)
    {
        var before = _bkFmBefore;
        _bkFmBefore = fmOpen;
        if (!_authed || !CfgBackupLogic.Started(before, fmOpen)) return;
        if (Interlocked.CompareExchange(ref _bkAutoBusy, 1, 0) != 0) return;
        _ = Task.Run(() =>
        {
            try
            {
                var item = CfgBackup.AutoRun();
                if (item != null)
                {
                    Log($"🗂 FiveM-Einstellungen automatisch gesichert ({item.Files.Count} {(item.Files.Count == 1 ? "Datei" : "Dateien")})");
                    Emit("bkauto", new { name = item.Name });
                }
            }
            catch { }   // a backup must never disturb the start of the game
            finally { Interlocked.Exchange(ref _bkAutoBusy, 0); }
        });
    }
}
