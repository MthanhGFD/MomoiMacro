using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BAMacro
{
    /// <summary>Marker drawn on the 16:9 preview canvas (1600 x 900 virtual units).</summary>
    public record KeyMarker(string Label, string Tip, double Left, double Top, double Width);

    public partial class MainWindow : Window
    {
        private Process? _ahkProcess;
        private DispatcherTimer? _syncTimer;
        private readonly string _iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "coords.ini");
        private readonly string _ahkScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "macro_core.ahk");

        private bool? _isRunning;
        private bool? _isCapture;
        private string _keysSignature = "";

        #region Win32

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool WritePrivateProfileString(string section, string key, string value, string filePath);

        private const byte VK_CONTROL = 0x11;
        private const byte VK_1 = 0x31;
        private const byte VK_2 = 0x32;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        #endregion

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureIniExists();
            LoadSettingsToUi();
            StartAhkEngine();
            SyncFromIni();

            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _syncTimer.Tick += (_, _) =>
            {
                SyncFromIni();
                CheckTargetGameProcess();
            };
            _syncTimer.Start();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) => StopAhkEngine();

        #region AutoHotkey Engine

        private void StartAhkEngine()
        {
            try
            {
                string args = $"\"{_iniPath}\" {Environment.ProcessId}";

                // 1) Preferred: compiled engine embedded in BAMacro.exe (no AutoHotkey install needed)
                string? engine = ExtractEmbeddedEngine();
                if (engine != null)
                {
                    _ahkProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = engine,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    return;
                }

                // 2) Fallback (dev): run macro_core.ahk with an installed AutoHotkey
                string[] possiblePaths =
                {
                    @"C:\Program Files\AutoHotkey\AutoHotkeyU64.exe",
                    @"C:\Program Files\AutoHotkey\AutoHotkey.exe",
                    @"C:\Program Files (x86)\AutoHotkey\AutoHotkey.exe"
                };

                string? ahkExe = possiblePaths.FirstOrDefault(File.Exists);

                if (ahkExe != null && File.Exists(_ahkScriptPath))
                {
                    _ahkProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = ahkExe,
                        Arguments = $"\"{_ahkScriptPath}\" {args}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                    });
                }
                else
                {
                    TxtGameConn.Text = "Macro engine not found";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting AHK: {ex.Message}");
                TxtGameConn.Text = "Cannot start macro engine (blocked by antivirus?)";
            }
        }

        /// <summary>Extracts BAMacroEngine.exe from the embedded resources to %LOCALAPPDATA%\BAMacro.</summary>
        private static string? ExtractEmbeddedEngine()
        {
            using var res = typeof(MainWindow).Assembly.GetManifestResourceStream("BAMacroEngine.exe");
            if (res == null) return null;

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BAMacro");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "BAMacroEngine.exe");

            // Close any engine left over from a previous run so the file can be replaced
            foreach (var p in Process.GetProcessesByName("BAMacroEngine"))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { /* ignore */ }
            }

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                res.CopyTo(fs);

            return path;
        }

        private void StopAhkEngine()
        {
            try
            {
                if (_ahkProcess != null && !_ahkProcess.HasExited)
                {
                    _ahkProcess.Kill();
                    _ahkProcess.Dispose();
                    _ahkProcess = null;
                }
            }
            catch
            {
                // Ignore cleanup
            }
        }

        #endregion

        #region Actions

        private void BtnToggle_Click(object sender, RoutedEventArgs e) => SendCtrlCombo(VK_1);

        private void BtnRecord_Click(object sender, RoutedEventArgs e) => SendCtrlCombo(VK_2);

        private static void SendCtrlCombo(byte vk)
        {
            keybd_event(VK_CONTROL, 0, 0, 0);
            keybd_event(vk, 0, 0, 0);
            System.Threading.Thread.Sleep(25);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
        }

        private void BtnOpenIni_Click(object sender, RoutedEventArgs e)
        {
            EnsureIniExists();
            Process.Start(new ProcessStartInfo { FileName = _iniPath, UseShellExecute = true });
        }

        private void Settings_LostFocus(object sender, RoutedEventArgs e)
        {
            string exe = TxtTargetExe.Text.Trim();
            if (!string.IsNullOrEmpty(exe))
                WritePrivateProfileString("Settings", "TargetExe", exe, _iniPath);

            if (int.TryParse(TxtDelayMs.Text.Trim(), out int delay) && delay >= 0)
                WritePrivateProfileString("Settings", "DelayMs", delay.ToString(), _iniPath);
            else
                TxtDelayMs.Text = "20";
            // AHK đọc lại Settings mỗi lần bật macro (Ctrl + 1)
        }

        #endregion

        #region Game detection

        private void CheckTargetGameProcess()
        {
            string targetName = TxtTargetExe.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetName)) return;

            string processClean = Path.GetFileNameWithoutExtension(targetName);
            var proc = Process.GetProcessesByName(processClean).FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

            if (proc == null)
            {
                SetGameStatus("#9ca3af", $"{targetName} not running");
                return;
            }

            IntPtr hwnd = proc.MainWindowHandle;
            GetWindowThreadProcessId(GetForegroundWindow(), out uint fgPid);

            if (IsIconic(hwnd))
                SetGameStatus("#f59e0b", $"{targetName} minimized - macro paused");
            else if (fgPid == (uint)proc.Id)
                SetGameStatus("#10b981", $"{targetName} active");
            else
                SetGameStatus("#f59e0b", $"{targetName} in background - macro paused");
        }

        private void SetGameStatus(string color, string text)
        {
            DotGameConn.Fill = Brush(color);
            TxtGameConn.Text = text;
        }

        #endregion

        #region INI Sync

        private void SyncFromIni()
        {
            if (!File.Exists(_iniPath)) return;

            Dictionary<string, Dictionary<string, string>> ini;
            try
            {
                ini = ParseIni(File.ReadAllLines(_iniPath));
            }
            catch
            {
                return; // File đang bị AHK ghi
            }

            if (ini.TryGetValue("status", out var status))
            {
                status.TryGetValue("state", out var state);
                status.TryGetValue("capture", out var cap);
                ApplyState(string.Equals(state, "RUNNING", StringComparison.OrdinalIgnoreCase), cap == "1");
            }

            var points = new List<(string Key, double X, double Y)>();
            if (ini.TryGetValue("keys", out var keySection))
            {
                foreach (var kv in keySection)
                {
                    if (TryParseCoord(kv.Value, out double x, out double y))
                        points.Add((PrettyKey(kv.Key), x, y));
                }
            }

            string signature = string.Join("|", points.Select(p => $"{p.Key}{p.X}{p.Y}"));
            if (signature != _keysSignature)
            {
                _keysSignature = signature;
                ListKeys.ItemsSource = BuildMarkers(points);
                TxtKeysHeader.Text = $"SAVED KEYS ({points.Count})";
                TxtNoKeys.Visibility = points.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private const double CanvasW = 1600, CanvasH = 900, MarkerH = 64;

        /// <summary>Groups keys that share (almost) the same position into one marker, e.g. "Z · X".</summary>
        private static List<KeyMarker> BuildMarkers(List<(string Key, double X, double Y)> points)
        {
            var markers = new List<KeyMarker>();
            var groups = points.GroupBy(p => (Math.Round(p.X, 2), Math.Round(p.Y, 2)));

            foreach (var g in groups)
            {
                var first = g.First();
                string label = string.Join(" · ", g.Select(p => p.Key));
                double width = Math.Max(MarkerH, label.Length * 20 + 36);

                double left = Math.Clamp(first.X * CanvasW - width / 2, 0, CanvasW - width);
                double top = Math.Clamp(first.Y * CanvasH - MarkerH / 2, 0, CanvasH - MarkerH);
                string tip = $"{label}: {first.X * 100:0.0}%, {first.Y * 100:0.0}%";

                markers.Add(new KeyMarker(label, tip, left, top, width));
            }
            return markers;
        }

        private static bool TryParseCoord(string value, out double x, out double y)
        {
            x = y = 0;
            var parts = value.Split(',');
            return parts.Length == 2 &&
                   double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out x) &&
                   double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out y);
        }

        private static Dictionary<string, Dictionary<string, string>> ParseIni(IEnumerable<string> lines)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string>? current = null;

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    var name = line[1..^1].Trim();
                    if (!result.TryGetValue(name, out current))
                    {
                        current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        result[name] = current;
                    }
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq > 0 && current != null)
                    current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
            return result;
        }

        private void ApplyState(bool running, bool capture)
        {
            if (_isRunning != running)
            {
                _isRunning = running;
                if (running)
                {
                    BorderBtn.Background = Brush("#e11d48");
                    BorderPill.Background = Brush("#9f1239");
                    TxtBtnIcon.Text = "⏹";
                    TxtBtnText.Text = "Stop";
                }
                else
                {
                    BorderBtn.Background = Brush("#cf6a87");
                    BorderPill.Background = Brush("#b95572");
                    TxtBtnIcon.Text = "▶";
                    TxtBtnText.Text = "Start";
                }
            }

            if (_isCapture != capture)
            {
                _isCapture = capture;
                if (capture)
                {
                    BorderRecord.Background = Brush("#fdf2f4");
                    BorderRecord.BorderBrush = Brush("#cf6a87");
                    DotRecord.Fill = Brush("#e11d48");
                    TxtRecord.Text = "Recording";
                    TxtRecord.Foreground = Brush("#cf6a87");
                    PillRecord.Background = Brush("#fce7f3");
                    TxtRecordPill.Foreground = Brush("#cf6a87");
                }
                else
                {
                    BorderRecord.Background = Brushes.White;
                    BorderRecord.BorderBrush = Brush("#d1d5db");
                    DotRecord.Fill = Brush("#4b5563");
                    TxtRecord.Text = "Record";
                    TxtRecord.Foreground = Brush("#4b5563");
                    PillRecord.Background = Brush("#e5e7eb");
                    TxtRecordPill.Foreground = Brush("#6b7280");
                }
            }

            // Header badge: RECORDING > RUNNING > READY
            if (capture)
                SetBadge("#fef3c7", "#fcd34d", "#f59e0b", "RECORDING", "#92400e");
            else if (running)
                SetBadge("#fce7f3", "#f472b6", "#e11d48", "RUNNING", "#9f1239");
            else
                SetBadge("#d1fae5", "#a7f3d0", "#10b981", "READY", "#047857");
        }

        private void SetBadge(string bg, string border, string dot, string text, string fg)
        {
            if (TxtStatus.Text == text) return;
            BadgeStatus.Background = Brush(bg);
            BadgeStatus.BorderBrush = Brush(border);
            DotStatus.Fill = Brush(dot);
            TxtStatus.Text = text;
            TxtStatus.Foreground = Brush(fg);
        }

        private static string PrettyKey(string key) => key.ToLowerInvariant() switch
        {
            "xbutton1" => "Mouse4",
            "xbutton2" => "Mouse5",
            "rbutton" => "RMB",
            var k => k.ToUpperInvariant()
        };

        private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

        private void LoadSettingsToUi()
        {
            try
            {
                var ini = ParseIni(File.ReadAllLines(_iniPath));
                if (ini.TryGetValue("settings", out var s))
                {
                    if (s.TryGetValue("targetexe", out var exe) && exe.Length > 0) TxtTargetExe.Text = exe;
                    if (s.TryGetValue("delayms", out var d) && d.Length > 0) TxtDelayMs.Text = d;
                }
            }
            catch
            {
                // Keep defaults
            }
        }

        private void EnsureIniExists()
        {
            if (File.Exists(_iniPath)) return;

            File.WriteAllText(_iniPath,
@"[Settings]
TargetExe=BlueArchive.exe
DelayMs=20

[Keys]

[Status]
State=STOPPED
Capture=0
");
        }

        #endregion
    }
}
