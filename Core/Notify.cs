using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OrbitTweak.Core;

/// <summary>
/// 2.31: a small note on the desktop, bottom right of the main screen's work area, in the app's dark look: kicker,
/// title, one line, a main button and "Ignorieren". It never takes the focus (shown without activation,
/// WS_EX_NOACTIVATE), has no taskbar button (WS_EX_TOOLWINDOW, owned by a hidden window so it is never taken for the
/// app's main window), plays the short system sound, hides itself after 20 s (not while the mouse is on it) and makes
/// the app's taskbar button blink. Call everything on the UI thread. Up to three notes stack; a fourth closes the oldest.
/// </summary>
internal static class Notify
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] private static extern bool MessageBeep(uint type);
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO fi);

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
    private const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
    private const uint MB_ICONASTERISK = 0x40, FLASHW_TRAY = 2;
    private const int Visible = 20;

    private static readonly List<Window> Open = new();
    private static HwndSource? _owner;

    /// <summary>Shows a note. <paramref name="action"/> null: only "OK".</summary>
    public static void Show(string kicker, string title, string line, string? action, Action? onAction, string accentHex, bool sound = true)
    {
        try
        {
            var root = (Border)XamlReader.Parse(Xaml);
            void Text(string name, string v) { if (root.FindName(name) is TextBlock t) t.Text = v; }
            Text("Kicker", kicker); Text("Title", title); Text("Line", line);
            var accent = Accent(accentHex);
            if (root.FindName("Dot") is System.Windows.Shapes.Ellipse dot) dot.Fill = accent;
            var act = (Button)root.FindName("Act");
            var skip = (Button)root.FindName("Skip");
            var close = (Button)root.FindName("Close");

            var w = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
                SizeToContent = SizeToContent.Height, Width = 392, Content = root, Title = "OrbitTweak: " + title,
                Opacity = 0, Focusable = false, Left = -10000, Top = -10000,
            };
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
            new WindowInteropHelper(w).Owner = Owner();

            if (action != null && onAction != null)
            {
                act.Content = action;
                act.Background = accent;
                act.Click += (_, _) => { Close(w); try { onAction(); } catch { } };
                skip.Content = "Ignorieren";
            }
            else
            {
                act.Visibility = Visibility.Collapsed;
                skip.Content = "OK";
            }
            skip.Click += (_, _) => Close(w);
            close.Click += (_, _) => Close(w);

            w.SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(w).Handle;
                var ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
                SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST));
                // a click on the note must not take the focus from the game or the program in front
                HwndSource.FromHwnd(h)?.AddHook((IntPtr hw, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
                {
                    if (msg != WM_MOUSEACTIVATE) return IntPtr.Zero;
                    handled = true;
                    return (IntPtr)MA_NOACTIVATE;
                });
            };
            w.Loaded += (_, _) =>
            {
                Restack();
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                w.BeginAnimation(UIElement.OpacityProperty, fade);
            };
            w.SizeChanged += (_, _) => Restack();

            // hides itself after 20 s; the mouse on the note holds it
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Visible) };
            timer.Tick += (_, _) => { if (w.IsMouseOver) return; timer.Stop(); Close(w); };
            w.Closed += (_, _) => { timer.Stop(); Open.Remove(w); Restack(); };

            while (Open.Count >= 3) Close(Open[0]);
            Open.Add(w);
            w.Show();
            timer.Start();
            if (sound) MessageBeep(MB_ICONASTERISK);
        }
        catch { }   // a note that cannot be shown must never break the alarm itself (the page still gets the event)
    }

    /// <summary>Makes the taskbar button blink three times (it then stays marked until the window is used).</summary>
    public static void Flash(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var fi = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = hwnd, dwFlags = FLASHW_TRAY, uCount = 3, dwTimeout = 0 };
            FlashWindowEx(ref fi);
        }
        catch { }
    }

    private static void Close(Window w)
    {
        if (!Open.Contains(w)) return;
        Open.Remove(w);
        try { w.Close(); } catch { }
    }

    /// <summary>Newest at the bottom, older ones above it, 16 px from the work area's edge (the window has 14 px of room
    /// for its shadow).</summary>
    private static void Restack()
    {
        var wa = SystemParameters.WorkArea;
        double bottom = wa.Bottom + 2;
        for (int i = Open.Count - 1; i >= 0; i--)
        {
            var x = Open[i];
            if (x.ActualHeight <= 0) continue;
            x.Left = wa.Right - x.ActualWidth + 2;
            x.Top = bottom - x.ActualHeight;
            bottom = x.Top + 22;
        }
    }

    private static IntPtr Owner()
    {
        // a hidden window of its own as the owner: no taskbar button, and Process.MainWindowHandle (used by a second
        // start to find the app) never lands on a note. Not the main window: owned windows hide when it is minimized.
        _owner ??= new HwndSource(new HwndSourceParameters("OrbitTweak.NotifyOwner") { WindowStyle = 0, ExtendedWindowStyle = (int)WS_EX_TOOLWINDOW, Width = 0, Height = 0 });
        return _owner.Handle;
    }

    private static Brush Accent(string hex)
    {
        try { if (ColorConverter.ConvertFromString(hex) is Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; } } catch { }
        return new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x3D));
    }

    // The look of the app's cards (Web/uhq.css): blue-black surface lighter at the top, a hairline, 18 px corners,
    // a soft shadow, Geist-like sizes in Segoe UI. Text is set from code after parsing (never put into this string).
    private const string Xaml = """
<Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Margin="14" CornerRadius="18" BorderThickness="1" BorderBrush="#26C4D2FF" SnapsToDevicePixels="True" UseLayoutRounding="True">
  <Border.Resources>
    <Style x:Key="B" TargetType="Button">
      <Setter Property="Foreground" Value="#FFECEFF5"/>
      <Setter Property="Background" Value="#17FFFFFF"/>
      <Setter Property="FontSize" Value="12.5"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Height" Value="32"/>
      <Setter Property="Padding" Value="14,0"/>
      <Setter Property="Margin" Value="0,0,8,0"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Focusable" Value="False"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="Bd" CornerRadius="10" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Bd" Property="Opacity" Value="0.86"/></Trigger>
              <Trigger Property="IsPressed" Value="True"><Setter TargetName="Bd" Property="Opacity" Value="0.72"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="X" TargetType="Button">
      <Setter Property="Width" Value="26"/>
      <Setter Property="Height" Value="26"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Focusable" Value="False"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="Bd" CornerRadius="8" Background="Transparent">
              <Path Data="M0,0 L8,8 M8,0 L0,8" Stroke="#FF7E879A" StrokeThickness="1.5" StrokeStartLineCap="Round" StrokeEndLineCap="Round" HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Bd" Property="Background" Value="#14FFFFFF"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </Border.Resources>
  <Border.Background>
    <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
      <GradientStop Color="#FF181D28" Offset="0"/>
      <GradientStop Color="#FF0F131B" Offset="1"/>
    </LinearGradientBrush>
  </Border.Background>
  <Border.Effect>
    <DropShadowEffect BlurRadius="26" ShadowDepth="6" Direction="270" Opacity="0.55" Color="#FF000000"/>
  </Border.Effect>
  <Grid Margin="18,14,12,16">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>
    <DockPanel Grid.Row="0" LastChildFill="True">
      <Button x:Name="Close" DockPanel.Dock="Right" Style="{StaticResource X}" ToolTip="Schließen"/>
      <Ellipse x:Name="Dot" Width="7" Height="7" Fill="#FFFF7A3D" VerticalAlignment="Center" Margin="0,0,8,0"/>
      <TextBlock x:Name="Kicker" Foreground="#FF7E879A" FontSize="11.5" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
    </DockPanel>
    <TextBlock x:Name="Title" Grid.Row="1" Margin="0,6,10,0" Foreground="#FFECEFF5" FontSize="15" FontWeight="SemiBold" TextWrapping="Wrap"/>
    <TextBlock x:Name="Line" Grid.Row="2" Margin="0,4,10,0" Foreground="#FF9AA2B3" FontSize="12.5" TextWrapping="Wrap" LineHeight="18"/>
    <StackPanel Grid.Row="3" Orientation="Horizontal" Margin="0,14,0,0">
      <Button x:Name="Act" Style="{StaticResource B}" Foreground="#FF16110D"/>
      <Button x:Name="Skip" Style="{StaticResource B}"/>
    </StackPanel>
  </Grid>
</Border>
""";
}
