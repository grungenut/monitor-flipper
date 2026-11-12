// Monitor Flipper — mirror a monitor or a single program window onto one or more monitors
// (or onto the same monitor, or into a window), optionally flipped horizontally and/or
// vertically. This file is the control window, tray icon and output windows; the GPU engine
// is in Engine.cs.
//
// Written for the C# 5 compiler that ships with Windows (.NET Framework 4), so it builds
// with no extra installs. See ../build.cmd.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorFlipper
{
    // Settings remembered between runs, saved as plain key=value lines in %APPDATA%.
    class Settings
    {
        public string Source = "";          // monitor device name, or "window:<title>"
        public string Targets = "";         // device names and/or "window", separated by |
        public bool FlipH = true;
        public bool FlipV = false;
        public bool KeepAspect = true;
        public bool ShowCursor = true;
        public bool AutoMirror = false;     // start mirroring as soon as the program opens

        static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorFlipper");
                return Path.Combine(dir, "settings.txt");
            }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    if (k == "Source") s.Source = v;
                    else if (k == "Targets" || k == "Target") s.Targets = v;
                    else if (k == "FlipH") s.FlipH = v == "1";
                    else if (k == "FlipV") s.FlipV = v == "1";
                    else if (k == "KeepAspect") s.KeepAspect = v == "1";
                    else if (k == "ShowCursor") s.ShowCursor = v == "1";
                    else if (k == "AutoMirror") s.AutoMirror = v == "1";
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new string[] {
                    "Source=" + Source, "Targets=" + Targets,
                    "FlipH=" + (FlipH ? "1" : "0"), "FlipV=" + (FlipV ? "1" : "0"),
                    "KeepAspect=" + (KeepAspect ? "1" : "0"), "ShowCursor=" + (ShowCursor ? "1" : "0"),
                    "AutoMirror=" + (AutoMirror ? "1" : "0") });
            }
            catch { }
        }
    }

    // "Start with Windows" = an entry in the current user's Run list.
    static class AutoStart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "MonitorFlipper";

        public static bool Enabled
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                    return k != null && k.GetValue(Name) != null;
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
                {
                    if (value) k.SetValue(Name, "\"" + Application.ExecutablePath + "\" --tray");
                    else if (k.GetValue(Name) != null) k.DeleteValue(Name);
                }
            }
        }
    }

    // One entry in the "Mirror from" list: a monitor or a program window.
    class SourceItem
    {
        public string Device;      // monitor
        public IntPtr Hwnd;        // window
        public string Title;
        public string Label;
        public override string ToString() { return Label; }
    }

    // The window that shows the mirrored picture. The GPU engine draws straight into it.
    class OutputForm : Form
    {
        readonly bool clickThrough;
        public volatile int ClientW, ClientH;

        public OutputForm(Rectangle? monitorBounds, bool clickThrough, int index)
        {
            this.clickThrough = clickThrough;
            Text = "Monitor Flipper - Output";
            BackColor = Color.Black;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            if (monitorBounds.HasValue)
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                Bounds = monitorBounds.Value;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                Size = new Size(960, 540);
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.X + 60 + index * 40, wa.Y + 60 + index * 40);
            }
            ClientW = ClientSize.Width; ClientH = ClientSize.Height;
            Resize += delegate { ClientW = ClientSize.Width; ClientH = ClientSize.Height; };
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (clickThrough)
                    cp.ExStyle |= 0x80000 /*LAYERED*/ | 0x20 /*TRANSPARENT*/ | 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return clickThrough; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (clickThrough) Native.SetLayeredWindowAttributes(Handle, 0, 255, 2 /*LWA_ALPHA*/);
            // Keep this window out of screen capture so the mirror never copies itself.
            try { Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE); } catch { }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { /* the GPU paints everything */ }
    }

    // The small control window: pick source and outputs, flips, start/stop. Lives in the tray.
    class MainForm : Form
    {
        const int HOTKEY_ID = 1;
        const string WindowChoice = "A window (for testing)";

        readonly Settings settings = Settings.Load();
        readonly bool startHidden;
        readonly ComboBox sourceBox = new ComboBox();
        readonly CheckedListBox targetList = new CheckedListBox();
        readonly CheckBox flipH = new CheckBox();
        readonly CheckBox flipV = new CheckBox();
        readonly CheckBox keepAspect = new CheckBox();
        readonly CheckBox showCursor = new CheckBox();
        readonly CheckBox startWithWindows = new CheckBox();
        readonly CheckBox autoMirror = new CheckBox();
        readonly Button startButton = new Button();
        readonly Label status = new Label();
        readonly Timer statusTimer = new Timer();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ToolStripMenuItem trayStart = new ToolStripMenuItem("Start mirroring");
        readonly ToolStripMenuItem trayFlipH = new ToolStripMenuItem("Flip horizontally");
        readonly ToolStripMenuItem trayFlipV = new ToolStripMenuItem("Flip vertically");

        Screen[] screens;
        GpuMirror mirror;
        readonly List<OutputForm> outputs = new List<OutputForm>();
        bool filling, exiting;

        public MainForm(bool startHidden)
        {
            this.startHidden = startHidden;
            Text = "Monitor Flipper";
            Icon = MakeIcon();
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            // Labels on the left, fields on the right, tight rows.
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.ColumnCount = 2;
            grid.AutoSize = true;
            grid.Padding = new Padding(10, 8, 10, 8);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            sourceBox.DropDownStyle = ComboBoxStyle.DropDownList; sourceBox.Width = 320;
            sourceBox.DropDownWidth = 480;
            targetList.CheckOnClick = true; targetList.Width = 320; targetList.IntegralHeight = false;
            targetList.BorderStyle = BorderStyle.FixedSingle;

            flipH.Text = "Horizontally (left/right)"; flipH.AutoSize = true; flipH.Margin = new Padding(0, 2, 12, 0);
            flipV.Text = "Vertically (upside down)"; flipV.AutoSize = true; flipV.Margin = new Padding(0, 2, 0, 0);
            FlowLayoutPanel flips = new FlowLayoutPanel();
            flips.AutoSize = true; flips.WrapContents = false; flips.Margin = new Padding(3, 0, 3, 0);
            flips.Controls.Add(flipH); flips.Controls.Add(flipV);

            keepAspect.Text = "Keep proportions (black bars if shapes differ)"; keepAspect.AutoSize = true;
            showCursor.Text = "Show the mouse pointer"; showCursor.AutoSize = true;
            startWithWindows.Text = "Start Monitor Flipper with Windows (in the tray)"; startWithWindows.AutoSize = true;
            autoMirror.Text = "Start mirroring as soon as it opens"; autoMirror.AutoSize = true;

            startButton.Text = "Start"; startButton.Width = 100; startButton.Height = 28;
            status.AutoSize = true; status.ForeColor = SystemColors.GrayText; status.MaximumSize = new Size(340, 0);
            status.Text = "Hotkey: Ctrl+Alt+F starts and stops.";

            AddRow(grid, "Mirror from", sourceBox);
            AddRow(grid, "Show on", targetList);
            AddRow(grid, "Flip", flips);
            AddRow(grid, "", keepAspect);
            AddRow(grid, "", showCursor);
            AddRow(grid, "Startup", startWithWindows);
            AddRow(grid, "", autoMirror);
            AddRow(grid, "", startButton);
            AddRow(grid, "", status);
            Controls.Add(grid);

            FillLists();
            flipH.Checked = settings.FlipH;
            flipV.Checked = settings.FlipV;
            keepAspect.Checked = settings.KeepAspect;
            showCursor.Checked = settings.ShowCursor;
            autoMirror.Checked = settings.AutoMirror;
            try { startWithWindows.Checked = AutoStart.Enabled; } catch { }

            // Flip / proportion / pointer changes apply live while running.
            flipH.CheckedChanged += delegate { trayFlipH.Checked = flipH.Checked; PushLive(); };
            flipV.CheckedChanged += delegate { trayFlipV.Checked = flipV.Checked; PushLive(); };
            keepAspect.CheckedChanged += delegate { PushLive(); };
            showCursor.CheckedChanged += delegate { PushLive(); };
            autoMirror.CheckedChanged += delegate { SaveSettings(); };
            startWithWindows.CheckedChanged += delegate
            {
                try { AutoStart.Enabled = startWithWindows.Checked; }
                catch (Exception ex) { status.Text = "Couldn't change startup setting: " + ex.Message; }
            };
            // Changing the source or outputs restarts the mirror.
            sourceBox.DropDown += delegate { RefreshWindows(); };
            sourceBox.SelectedIndexChanged += delegate { if (!filling) { SaveSettings(); Restart(); } };
            targetList.ItemCheck += delegate { if (!filling) BeginInvoke((MethodInvoker)delegate { SaveSettings(); Restart(); }); };
            startButton.Click += delegate { Toggle(); };

            statusTimer.Interval = 500;
            statusTimer.Tick += delegate
            {
                if (mirror == null) return;
                if (mirror.Finished) { string s = mirror.Status; StopMirror(); status.Text = s; return; }
                status.Text = mirror.Status + "   (Ctrl+Alt+F to stop)";
            };
            statusTimer.Start();

            SetUpTray();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        }

        // ---------------------------------------------------------------- tray
        void SetUpTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem show = new ToolStripMenuItem("Show Monitor Flipper");
            show.Font = new Font(show.Font, FontStyle.Bold);
            show.Click += delegate { ShowFromTray(); };
            trayStart.Click += delegate { Toggle(); };
            trayFlipH.Checked = settings.FlipH; trayFlipV.Checked = settings.FlipV;
            trayFlipH.Click += delegate { flipH.Checked = !flipH.Checked; };
            trayFlipV.Click += delegate { flipV.Checked = !flipV.Checked; };
            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { exiting = true; Close(); };
            menu.Items.AddRange(new ToolStripItem[] { show, trayStart, new ToolStripSeparator(), trayFlipH, trayFlipV, new ToolStripSeparator(), exit });
            tray.ContextMenuStrip = menu;
            tray.Icon = Icon;
            tray.Text = "Monitor Flipper";
            tray.Visible = true;
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowFromTray(); };
        }

        void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) Hide();   // minimize = go to the tray
        }

        protected override void SetVisibleCore(bool value)
        {
            // Started with --tray: create the window (for the hotkey) but keep it hidden.
            if (startHidden && !IsHandleCreated) { CreateHandle(); value = false; }
            base.SetVisibleCore(value);
        }

        static Icon MakeIcon()
        {
            // The icon built into the exe (assets/MonitorFlipper.ico); drawn by hand as a fallback.
            try { Icon own = Icon.ExtractAssociatedIcon(Application.ExecutablePath); if (own != null) return own; } catch { }
            using (Bitmap b = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Brush frame = new SolidBrush(Color.FromArgb(40, 44, 52)))
                    g.FillRectangle(frame, 2, 4, 28, 20);
                g.FillRectangle(Brushes.White, 4, 6, 24, 16);
                g.FillRectangle(new SolidBrush(Color.FromArgb(40, 44, 52)), 13, 24, 6, 4);
                g.FillRectangle(new SolidBrush(Color.FromArgb(40, 44, 52)), 9, 27, 14, 2);
                using (Brush left = new SolidBrush(Color.FromArgb(0, 120, 215)))
                using (Brush right = new SolidBrush(Color.FromArgb(255, 140, 0)))
                {
                    g.FillPolygon(left, new Point[] { new Point(15, 8), new Point(15, 20), new Point(6, 14) });
                    g.FillPolygon(right, new Point[] { new Point(17, 8), new Point(17, 20), new Point(26, 14) });
                }
                return Icon.FromHandle(b.GetHicon());
            }
        }

        // ---------------------------------------------------------------- lists
        static void AddRow(TableLayoutPanel grid, string label, Control field)
        {
            Label l = new Label();
            l.Text = label; l.AutoSize = true; l.Anchor = AnchorStyles.Left | AnchorStyles.Top; l.Margin = new Padding(0, 5, 8, 3);
            field.Anchor = AnchorStyles.Left;
            if (field.Margin.Top > 2) field.Margin = new Padding(3, 2, 3, 2);
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(l);
            grid.Controls.Add(field);
        }

        static string Describe(Screen s, int index)
        {
            return string.Format("Monitor {0}{1} - {2} x {3}", index + 1, s.Primary ? " (main)" : "", s.Bounds.Width, s.Bounds.Height);
        }

        // Visible, titled, top-level program windows (what Alt+Tab would show).
        static List<SourceItem> ListWindows()
        {
            List<SourceItem> list = new List<SourceItem>();
            int myPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h) || Native.GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return true;
                if ((Native.GetWindowLong(h, -20 /*EXSTYLE*/) & 0x80 /*TOOLWINDOW*/) != 0) return true;
                int cloaked; if (Native.DwmGetWindowAttribute(h, 14 /*CLOAKED*/, out cloaked, 4) == 0 && cloaked != 0) return true;
                int pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == myPid) return true;
                int len = Native.GetWindowTextLength(h);
                if (len == 0) return true;
                StringBuilder sb = new StringBuilder(len + 1);
                Native.GetWindowText(h, sb, sb.Capacity);
                string t = sb.ToString();
                if (t == "Program Manager") return true;
                SourceItem it = new SourceItem();
                it.Hwnd = h; it.Title = t;
                it.Label = "Window: " + (t.Length > 70 ? t.Substring(0, 67) + "..." : t);
                list.Add(it);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        void FillLists()
        {
            filling = true;
            screens = Screen.AllScreens;
            sourceBox.Items.Clear(); targetList.Items.Clear();
            int srcIdx = -1;
            for (int i = 0; i < screens.Length; i++)
            {
                SourceItem it = new SourceItem(); it.Device = screens[i].DeviceName; it.Label = Describe(screens[i], i);
                sourceBox.Items.Add(it);
                targetList.Items.Add(Describe(screens[i], i));
                if (screens[i].DeviceName == settings.Source) srcIdx = i;
            }
            if (WindowCapture.Supported)
                foreach (SourceItem w in ListWindows())
                {
                    int idx = sourceBox.Items.Add(w);
                    if (srcIdx < 0 && settings.Source == "window:" + w.Title) srcIdx = idx;
                }
            targetList.Items.Add(WindowChoice);
            sourceBox.SelectedIndex = Math.Max(0, srcIdx);

            // Outputs: saved choice, else the first monitor that isn't the source, else a window.
            List<string> saved = new List<string>(settings.Targets.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
            bool any = false;
            for (int i = 0; i < screens.Length; i++)
                if (saved.Contains(screens[i].DeviceName)) { targetList.SetItemChecked(i, true); any = true; }
            if (saved.Contains("window")) { targetList.SetItemChecked(screens.Length, true); any = true; }
            if (!any)
            {
                int pick = screens.Length;
                for (int i = 0; i < screens.Length; i++) if (screens[i].DeviceName != SourceMonitorDevice()) { pick = i; break; }
                targetList.SetItemChecked(pick, true);
            }
            targetList.Height = targetList.ItemHeight * targetList.Items.Count + 4;
            filling = false;
        }

        // Re-list program windows when the dropdown opens, keeping the current choice.
        void RefreshWindows()
        {
            if (!WindowCapture.Supported) return;
            filling = true;
            SourceItem current = sourceBox.SelectedItem as SourceItem;
            for (int i = sourceBox.Items.Count - 1; i >= screens.Length; i--) sourceBox.Items.RemoveAt(i);
            foreach (SourceItem w in ListWindows())
            {
                int idx = sourceBox.Items.Add(w);
                if (current != null && current.Hwnd != IntPtr.Zero && w.Hwnd == current.Hwnd) sourceBox.SelectedIndex = idx;
            }
            if (sourceBox.SelectedIndex < 0) sourceBox.SelectedIndex = 0;
            filling = false;
        }

        string SourceMonitorDevice()
        {
            SourceItem s = sourceBox.SelectedItem as SourceItem;
            if (s == null) return "";
            if (s.Device != null) return s.Device;
            return Native.IsWindow(s.Hwnd) ? Screen.FromHandle(s.Hwnd).DeviceName : "";
        }

        void OnDisplaysChanged(object sender, EventArgs e)
        {
            BeginInvoke((MethodInvoker)delegate
            {
                bool wasRunning = mirror != null;
                StopMirror();
                SaveSettings();
                FillLists();
                if (wasRunning) StartMirror();
            });
        }

        void SaveSettings()
        {
            if (screens == null || filling) return;
            SourceItem s = sourceBox.SelectedItem as SourceItem;
            if (s != null) settings.Source = s.Device ?? ("window:" + s.Title);
            List<string> t = new List<string>();
            for (int i = 0; i < targetList.Items.Count; i++)
                if (targetList.GetItemChecked(i)) t.Add(i < screens.Length ? screens[i].DeviceName : "window");
            settings.Targets = string.Join("|", t.ToArray());
            settings.FlipH = flipH.Checked;
            settings.FlipV = flipV.Checked;
            settings.KeepAspect = keepAspect.Checked;
            settings.ShowCursor = showCursor.Checked;
            settings.AutoMirror = autoMirror.Checked;
            settings.Save();
        }

        // ---------------------------------------------------------------- mirroring
        void PushLive()
        {
            if (mirror != null)
            {
                mirror.FlipH = flipH.Checked;
                mirror.FlipV = flipV.Checked;
                mirror.KeepAspect = keepAspect.Checked;
                mirror.ShowCursor = showCursor.Checked;
            }
            SaveSettings();
        }

        void Toggle()
        {
            if (mirror != null) StopMirror(); else StartMirror();
        }

        void Restart()
        {
            if (mirror != null) { StopMirror(); StartMirror(); }
        }

        void StartMirror()
        {
            SourceItem src = sourceBox.SelectedItem as SourceItem;
            if (screens == null || src == null) return;
            if (src.Hwnd != IntPtr.Zero && !Native.IsWindow(src.Hwnd)) { status.Text = "That window is gone - pick it again."; return; }
            string srcMonitor = SourceMonitorDevice();

            bool sameScreen = false;
            for (int i = 0; i < targetList.Items.Count; i++)
            {
                if (!targetList.GetItemChecked(i)) continue;
                bool inWindow = i >= screens.Length;
                // Output on the source's own monitor: it covers it, so let clicks pass through.
                bool same = !inWindow && screens[i].DeviceName == srcMonitor;
                sameScreen |= same;
                OutputForm o = new OutputForm(inWindow ? (Rectangle?)null : screens[i].Bounds, same, outputs.Count);
                o.FormClosed += delegate { if (outputs.Count > 0 && !exiting) BeginInvoke((MethodInvoker)StopMirror); };
                o.Show();
                outputs.Add(o);
            }
            if (outputs.Count == 0) { status.Text = "Tick at least one place to show it on."; return; }

            mirror = new GpuMirror(src.Device, src.Hwnd, outputs);
            PushLive();
            mirror.Start();
            startButton.Text = "Stop"; trayStart.Text = "Stop mirroring";
            status.Text = sameScreen ? "Same monitor: clicks pass through. Ctrl+Alt+F stops." : "Starting...";
        }

        void StopMirror()
        {
            if (mirror != null) { mirror.Stop(); mirror = null; }
            OutputForm[] os = outputs.ToArray();
            outputs.Clear();
            foreach (OutputForm o in os) if (!o.IsDisposed) o.Close();
            startButton.Text = "Start"; trayStart.Text = "Start mirroring";
            status.Text = "Stopped. Hotkey: Ctrl+Alt+F starts and stops.";
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!Native.RegisterHotKey(Handle, HOTKEY_ID, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)Keys.F))
                status.Text = "Ctrl+Alt+F is taken by another program; use the button.";
            if (settings.AutoMirror) BeginInvoke((MethodInvoker)StartMirror);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && (int)m.WParam == HOTKEY_ID) Toggle();
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            exiting = true;
            SaveSettings();
            StopMirror();
            Native.UnregisterHotKey(Handle, HOTKEY_ID);
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosing(e);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Work in real pixels so monitor sizes and positions are exact at any Windows scaling.
            // (Desktop Duplication also requires this.)
            try { if (!Native.SetProcessDpiAwarenessContext(new IntPtr(-4))) Native.SetProcessDPIAware(); }
            catch { try { Native.SetProcessDPIAware(); } catch { } }

            // MonitorFlipper.exe --selftest <result file> : GPU flip/rotation accuracy, no window.
            if (args.Length >= 2 && args[0] == "--selftest")
            {
                File.WriteAllText(args[1], GpuMirror.SelfTest(Screen.PrimaryScreen.DeviceName));
                return;
            }
            // MonitorFlipper.exe --selftest-window <hwnd> <result file> : window-capture check.
            if (args.Length >= 3 && args[0] == "--selftest-window")
            {
                File.WriteAllText(args[2], GpuMirror.WindowSelfTest(new IntPtr(long.Parse(args[1]))));
                return;
            }

            bool tray = args.Length > 0 && args[0] == "--tray";
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(tray));
        }
    }
}
