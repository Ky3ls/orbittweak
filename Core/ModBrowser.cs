using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace OrbitTweak.Core;

/// <summary>
/// Built-in mod browser: a normal browser window (gta5-mods.com, Cfx.re forum, GitHub …).
/// When a mod archive (.rpf/.zip/.rar/.7z) is downloaded, OrbitTweak saves it and installs it
/// into FiveM's mods folder right away.
/// </summary>
internal sealed class ModBrowser : Window
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    public static readonly (string id, string name, string url)[] Sources =
    {
        ("gta5mods", "GTA5-Mods", "https://www.gta5-mods.com/"),
        ("cfx", "Cfx.re Forum", "https://forum.cfx.re/search?q=client%20mod%20rpf"),
        ("github", "GitHub", "https://github.com/search?q=fivem+rpf&type=repositories"),
    };
    public static readonly (string id, string name)[] Cats =
    {
        ("minimap", "Minimap"), ("kill", "Kill-Effekte"), ("gfx", "Grafik-Packs"), ("sound", "Sounds"), ("other", "Sonstiges"),
    };

    private static readonly SolidColorBrush Bg = new(Color.FromRgb(0x13, 0x13, 0x15)), Fg = new(Color.FromRgb(0xF4, 0xF4, 0xF5)),
        Sub = new(Color.FromRgb(0xA1, 0xA1, 0xAA)), Line = new(Color.FromRgb(0x2A, 0x2A, 0x30)), Btn = new(Color.FromRgb(0x1E, 0x1E, 0x23));

    private readonly WebView2 _wv = new();
    private readonly TextBox _url = new();
    private readonly TextBlock _status = new();
    private readonly ComboBox _cat = new();
    private readonly Settings _s;
    private readonly Action<string> _log;
    private readonly Action<object> _emit;
    private readonly SolidColorBrush _accent;

    public ModBrowser(CoreWebView2Environment env, string url, string cat, string accent, Settings s, Action<string> log, Action<object> emit)
    {
        _s = s; _log = log; _emit = emit;
        _accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent));
        Title = "OrbitTweak · Mod-Browser"; Width = 1240; Height = 820; Background = Bg; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SourceInitialized += (_, _) => { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, 4); };

        // ---- toolbar ----
        var bar = new DockPanel { Background = Bg, LastChildFill = true, Height = 54 };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        left.Children.Add(Button("◀", () => { if (_wv.CanGoBack) _wv.GoBack(); }));
        left.Children.Add(Button("▶", () => { if (_wv.CanGoForward) _wv.GoForward(); }));
        left.Children.Add(Button("⟳", () => _wv.Reload()));
        foreach (var src in Sources) { var u = src.url; left.Children.Add(Button(src.name, () => Go(u), true)); }
        DockPanel.SetDock(left, Dock.Left);
        bar.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        right.Children.Add(new TextBlock { Text = "Installieren als", Foreground = Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) });
        foreach (var c in Cats) _cat.Items.Add(new ComboBoxItem { Content = c.name, Tag = c.id });
        _cat.SelectedIndex = Math.Max(0, Array.FindIndex(Cats, c => c.id == cat));
        _cat.Width = 140; _cat.Height = 30; _cat.VerticalContentAlignment = VerticalAlignment.Center;
        right.Children.Add(_cat);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);

        _url.Margin = new Thickness(10, 12, 10, 12); _url.Background = Btn; _url.Foreground = Fg; _url.BorderBrush = Line; _url.Padding = new Thickness(10, 4, 10, 4);
        _url.VerticalContentAlignment = VerticalAlignment.Center; _url.CaretBrush = Fg;
        _url.KeyDown += (_, e) => { if (e.Key == Key.Enter) Go(_url.Text); };
        bar.Children.Add(_url);

        // ---- status line ----
        var foot = new Border { Background = Bg, BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 9, 14, 9) };
        _status.Foreground = Sub; _status.FontSize = 12.5; _status.TextWrapping = TextWrapping.Wrap;
        _status.Text = "Mod suchen und auf „Download“ klicken – OrbitTweak installiert .rpf / .zip / .rar / .7z automatisch in FiveM. Achtung: Viele Server verbieten Mods (Pure-Mode).";
        foot.Child = _status;

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top); DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(bar); root.Children.Add(foot);
        root.Children.Add(new Border { BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = _wv });
        Content = root;

        Loaded += async (_, _) =>
        {
            await _wv.EnsureCoreWebView2Async(env);
            var c = _wv.CoreWebView2;
            c.NewWindowRequested += (_, e) => { e.Handled = true; c.Navigate(e.Uri); };
            c.SourceChanged += (_, _) => _url.Text = c.Source;
            c.DownloadStarting += OnDownload;
            Go(url);
        };
    }

    private Border Button(string text, Action click, bool src = false)
    {
        var b = new Border
        {
            Background = Btn, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(src ? 12 : 10, 6, src ? 12 : 10, 6), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand,
            Child = new TextBlock { Text = text, Foreground = src ? _accent : Fg, FontWeight = FontWeights.SemiBold, FontSize = 12.5 }
        };
        b.MouseEnter += (_, _) => b.BorderBrush = _accent;
        b.MouseLeave += (_, _) => b.BorderBrush = Line;
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    private void Go(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url.Trim();
        try { _wv.CoreWebView2?.Navigate(url); } catch { }
    }

    private void Status(string text, bool good = false)
    {
        Dispatcher.Invoke(() => { _status.Text = text; _status.Foreground = good ? new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x9A)) : Sub; });
    }

    private void OnDownload(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var name = Path.GetFileName(e.ResultFilePath);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext is not (".rpf" or ".zip" or ".rar" or ".7z")) return; // anything else: normal download
        var dir = Path.Combine(Path.GetTempPath(), "OrbitTweak-dl");
        Directory.CreateDirectory(dir);
        e.ResultFilePath = Path.Combine(dir, name);
        e.Handled = true;
        var cat = (_cat.SelectedItem as ComboBoxItem)?.Tag as string ?? "other";
        var op = e.DownloadOperation;
        Status($"Lade {name} …");
        op.BytesReceivedChanged += (_, _) =>
        {
            if (op.TotalBytesToReceive is ulong t && t > 0) Status($"Lade {name} … {(ulong)op.BytesReceived * 100 / t}%");
        };
        op.StateChanged += async (_, _) =>
        {
            if (op.State == CoreWebView2DownloadState.Interrupted) { Status($"Download abgebrochen: {op.InterruptReason}"); return; }
            if (op.State != CoreWebView2DownloadState.Completed) return;
            Status($"Installiere {name} …");
            var path = op.ResultFilePath;
            var (count, error) = await Task.Run(() =>
            {
                var (files, temp, err) = ModDownloads.PrepareFile(path);
                try
                {
                    if (err != null) return (0, err);
                    lock (_s) return Mods.Install(files, cat, _s.ModCats);
                }
                finally { if (temp != null) try { Directory.Delete(temp, true); } catch { } }
            });
            if (error != null) Status($"✖ {name}: {error}");
            else
            {
                _s.Save();
                Status($"✔ {name} installiert ({count} Datei(en)) – aktiv beim nächsten FiveM-Start. Im Mod-Manager an-/ausschaltbar.", true);
                _log($"🧩 Mod installiert: {name}");
                _emit(new { name, count, cat });
            }
        };
    }
}
