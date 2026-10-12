using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TimeLock
{
    internal static class ClientProgram
    {
        private static MutexHandle _single;

        public static int Main(string[] args)
        {
            // Session 0 means nobody is logged in yet -- nothing to lock.
            try
            {
                if (System.Diagnostics.Process.GetCurrentProcess().SessionId == 0) return 0;
            }
            catch { return 0; }

            _single = new MutexHandle("Local\\TimeLock.Client");
            if (!_single.Acquire())
            {
                // Another client already owns this session's desktop.
                return 0;
            }

            bool hooksInstalled = false;
            try
            {
                // Never flash a lock screen when there is nothing to lock:
                // give the state file a moment to appear, then bail out.
                if (!WaitForActiveLock()) return 0;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                LockForm form = new LockForm();
                hooksInstalled = form.InstallHooks();
                if (!hooksInstalled)
                {
                    // Without the keyboard hook the lock is cosmetic only.
                    return 2;
                }

                Application.Run(form);
                return 0;
            }
            catch (Exception ex)
            {
                TryReport(ex.Message);
                return 1;
            }
            finally
            {
                if (hooksInstalled) LockForm.RemoveHooks();
                if (_single != null) _single.Dispose();
            }
        }

        /// <summary>
        /// Polls the state file briefly before committing to a window. The
        /// enforcer can spawn us a moment before the UI writes the state, and
        /// a lock screen that blinks on and off would look broken.
        /// </summary>
        private static bool WaitForActiveLock()
        {
            for (int i = 0; i < 20; i++)
            {
                if (Store.Load().IsActive) return true;
                System.Threading.Thread.Sleep(250);
            }
            return false;
        }

        internal static void Log(string msg)
        {
            TryReport(msg);
        }

        private static void TryReport(string msg)
        {
            try
            {
                // The hardened data folder is read-only for standard users, so
                // the client logs into its own profile instead.
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeLock");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dir, "client.log"),
                    DateTime.Now.ToString("s") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    //  Named mutex helper (single instance per session)
    // ------------------------------------------------------------------
    internal sealed class MutexHandle : IDisposable
    {
        private readonly string _name;
        private bool _created;

        public MutexHandle(string name) { _name = name; }

        public bool Acquire()
        {
            try
            {
                bool createdNew;
                _mutex = new Mutex(true, _name, out createdNew);
                _created = createdNew;
                return createdNew;
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
            catch { return true; }
        }

        private Mutex _mutex;

        public void Dispose()
        {
            try
            {
                if (_mutex != null)
                {
                    if (_created) _mutex.ReleaseMutex();
                    _mutex.Dispose();
                }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    //  The lock screen
    // ------------------------------------------------------------------
    internal sealed class LockForm : Form
    {
        // ---- palette ---------------------------------------------------
        private static readonly Color BackTop = Color.FromArgb(24, 27, 38);
        private static readonly Color BackBottom = Color.FromArgb(9, 10, 14);
        private static readonly Color Accent = Color.FromArgb(94, 200, 255);
        private static readonly Color TextDim = Color.FromArgb(126, 134, 156);
        private static readonly Color TextBright = Color.FromArgb(238, 242, 250);
        private static readonly Color Panel = Color.FromArgb(255, 255, 255, 8);

        // ---- fonts -----------------------------------------------------
        private readonly Font _fLabel = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _fClock = new Font("Consolas", 96f, FontStyle.Bold, GraphicsUnit.Point);
        private readonly Font _fMessage = new Font("Segoe UI", 16f, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _fSmall = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _fHeadline = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Point);

        // ---- hooks -----------------------------------------------------
        private static Native.HookProc _kbdProc;
        private static Native.HookProc _mouseProc;
        private static IntPtr _kbdHook = IntPtr.Zero;
        private static IntPtr _mouseHook = IntPtr.Zero;

        // ---- state -----------------------------------------------------
        private LockState _state;
        private string _message = "";
        private string _shownCountdown = "";
        private System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private DateTime _endLocal;
        private bool _closing;
        private Font _clockFontScaled;
        private float _clockFontK = -1f;
        private bool _resourcesReleased;

        public LockForm()
        {
            // Same reason as MainForm: the Win32 icon resources are not the
            // same thing WinForms uses for a form's icon.
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            _state = Store.Load();
            _message = Fmt.Trim(_state.Message, 120);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            FitToVirtualScreen();
        }

        public bool InstallHooks()
        {
            IntPtr mod = Native.GetModuleHandle(null);

            // Kept in static fields: if these delegates get collected the
            // hooks die silently and the lock turns cosmetic.
            _kbdProc = KbdHook;
            _mouseProc = MouseHook;

            _kbdHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbdProc, mod, 0);
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, mod, 0);

            ClientProgram.Log("hooks: kbd=" + (_kbdHook != IntPtr.Zero) +
                              " mouse=" + (_mouseHook != IntPtr.Zero) +
                              " err=" + Marshal.GetLastWin32Error() +
                              " module=" + mod);

            return _kbdHook != IntPtr.Zero;
        }

        public static void RemoveHooks()
        {
            if (_kbdHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_kbdHook); _kbdHook = IntPtr.Zero; }
            if (_mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
            _kbdProc = null;
            _mouseProc = null;
        }

        // ==================================================================
        //  Input blocking
        // ==================================================================
        private static int _hookHits;

        private static IntPtr KbdHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // The lock screen has no interactive controls, so every keystroke is
            // refused.  This covers Win, Win+R, Win+X, Alt+Tab, Alt+F4,
            // Ctrl+Shift+Esc, PrintScreen and anything else user-mode.
            if (nCode == Native.HC_ACTION)
            {
                // Bounded trace so a silently-dead hook is diagnosable from the
                // log. Capped hard: a low-level hook must return fast or Windows
                // drops it, and disk I/O on every keystroke is not fast.
                if (System.Threading.Interlocked.Increment(ref _hookHits) <= 10)
                {
                    try
                    {
                        Native.KBDLLHOOKSTRUCT k =
                            (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                        ClientProgram.Log("kbdhook msg=" + wParam.ToInt32() + " vk=" + k.vkCode);
                    }
                    catch { }
                }
                return new IntPtr(1);
            }

            return Native.CallNextHookEx(_kbdHook, nCode, wParam, lParam);
        }

        private static IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == Native.HC_ACTION)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_RBUTTONDOWN || msg == Native.WM_RBUTTONUP ||
                    msg == Native.WM_MBUTTONDOWN || msg == Native.WM_MBUTTONUP ||
                    msg == Native.WM_XBUTTONDOWN || msg == Native.WM_XBUTTONUP)
                {
                    return new IntPtr(1);
                }
            }

            return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        // ==================================================================
        //  Window behaviour
        // ==================================================================
        private void FitToVirtualScreen()
        {
            try
            {
                Rectangle v = SystemInformation.VirtualScreen;
                if (v.Width < 800 || v.Height < 600)
                    v = new Rectangle(0, 0, Math.Max(1024, Screen.PrimaryScreen.Bounds.Width),
                                             Math.Max(768, Screen.PrimaryScreen.Bounds.Height));

                Bounds = v;
            }
            catch
            {
                WindowState = FormWindowState.Maximized;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            try { Native.SetForegroundWindow(Handle); }
            catch { }
        }

        protected override void SetVisibleCore(bool value)
        {
            // A locked session must not be able to hide this window.
            base.SetVisibleCore(value);
            if (value)
            {
                try
                {
                    Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                }
                catch { }
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int WM_SYSCOMMAND = 0x0112;
            const int WM_DISPLAYCHANGE = 0x007E;

            if (m.Msg == WM_SYSCOMMAND)
            {
                int sc = m.WParam.ToInt32() & 0xFFF0;
                if (sc == 0xF060 ||   // SC_CLOSE
                    sc == 0xF030 ||   // SC_MINIMIZE
                    sc == 0xF030 ||   // SC_MAXIMIZE
                    sc == 0xF120 ||   // SC_RESTORE
                    sc == 0xF010 ||   // SC_MOVE
                    sc == 0xF000 ||   // SC_SIZE
                    sc == 0xF100 ||   // SC_KEYMENU (Alt+Space)
                    sc == 0xF140)     // SC_TASKMODAL
                {
                    return;
                }
            }
            else if (m.Msg == WM_NCHITTEST)
            {
                // HTCLIENT everywhere: no dragging, no resize, no system menu.
                m.Result = new IntPtr(1);
                return;
            }
            else if (m.Msg == WM_DISPLAYCHANGE)
            {
                try { BeginInvoke(new MethodInvoker(FitToVirtualScreen)); }
                catch { }
            }

            base.WndProc(ref m);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        // ==================================================================
        //  Countdown
        // ==================================================================
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = ClientRectangle;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (LinearGradientBrush bg = new LinearGradientBrush(
                       r, BackTop, BackBottom, LinearGradientMode.Vertical))
            {
                g.FillRectangle(bg, r);
            }

            float scale = Math.Max(0.45f, Math.Min(1.0f, r.Height / 1000f));
            int cx = r.Width / 2;
            int mid = r.Height / 2;

            // ---- header ------------------------------------------------
            string header = "SESSION  LOCKED";
            SizeF hs = g.MeasureString(header, _fHeadline);
            float hy = mid - (int)(230 * scale) - hs.Height;
            DrawCentered(g, header, _fHeadline, Accent, cx, (int)hy);

            // ---- accent bar -------------------------------------------
            int barW = (int)(84 * scale);
            int barH = Math.Max(3, (int)(3 * scale));
            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, Accent)))
                g.FillRectangle(b, cx - barW / 2, (int)hy + hs.Height + (int)(12 * scale), barW, barH);

            // ---- clock -------------------------------------------------
            string clock = _shownCountdown;
            SizeF cs = g.MeasureString(clock, _fClock);

            float availW = r.Width * 0.86f;
            float availH = r.Height * 0.40f;
            float k = Math.Min(1f, Math.Min(availW / cs.Width, availH / cs.Height));
            if (k < 0.2f) k = 0.2f;

            float clockTop = mid - (int)(120 * scale) - (cs.Height * k) / 2f;

            Font clockFont = ScaledClockFont(k);
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(38, TextBright)))
            using (SolidBrush solid = new SolidBrush(TextBright))
            using (StringFormat sf = new StringFormat
                   {
                       Alignment = StringAlignment.Center,
                       LineAlignment = StringAlignment.Center
                   })
            {
                RectangleF box = new RectangleF(0, clockTop, r.Width, cs.Height * k);
                g.DrawString(clock, clockFont, glow, box, sf);
                g.DrawString(clock, clockFont, solid, box, sf);
            }

            // ---- message -----------------------------------------------
            if (_message.Length > 0)
            {
                DrawCentered(g, _message, _fMessage, TextBright, cx, (int)(clockTop + cs.Height + 46 * scale));
            }

            // ---- footer ------------------------------------------------
            string foot1 = "Unlocks at " + _endLocal.ToString("HH:mm:ss");
            string foot2 = "This lock is restored automatically after a restart.";
            DrawCentered(g, foot1, _fSmall, TextDim, cx, mid + (int)(190 * scale));
            DrawCentered(g, foot2, _fSmall, TextDim, cx, mid + (int)(190 * scale) + _fSmall.Height + 6);

            // ---- edges -------------------------------------------------
            using (Pen p = new Pen(Color.FromArgb(60, 255, 255, 255)))
            {
                g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
            }
        }

        private Font ScaledClockFont(float k)
        {
            if (_clockFontK > 0 && Math.Abs(_clockFontK - k) < 0.01f && _clockFontScaled != null)
                return _clockFontScaled;

            try { if (_clockFontScaled != null) _clockFontScaled.Dispose(); } catch { }
            _clockFontScaled = new Font("Consolas", 96f * k, FontStyle.Bold, GraphicsUnit.Point);
            _clockFontK = k;
            return _clockFontScaled;
        }

        private void DrawCentered(Graphics g, string text, Font font, Color color, int cx, int y)
        {
            if (string.IsNullOrEmpty(text)) return;
            SizeF s = g.MeasureString(text, font);
            using (SolidBrush b = new SolidBrush(color))
                g.DrawString(text, font, b, cx - s.Width / 2f, y);
        }

        private void StartTimer()
        {
            _timer.Interval = 500;
            _timer.Tick += OnTick;
            _timer.Start();
            OnTick(null, EventArgs.Empty);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            StartTimer();
        }

        private void OnTick(object sender, EventArgs e)
        {
            // A queued WM_TIMER can still land after the form is closing;
            // touching Handle then throws ObjectDisposedException.
            if (_closing || IsDisposed || Disposing || !IsHandleCreated) return;

            LockState s = Store.Load();

            // The single source of truth is the absolute end time in the state
            // file, which only SYSTEM/admins can write. The client polls it and
            // steps aside the moment it passes -- no local countdown to drift.
            if (!s.IsActive)
            {
                Finish();
                return;
            }

            _state = s;
            _endLocal = new DateTime(s.EndUtcTicks, DateTimeKind.Utc).ToLocalTime();
            string clock = Fmt.Countdown(s.Remaining);

            if (clock != _shownCountdown)
            {
                _shownCountdown = clock;
                Invalidate();
            }

            try
            {
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }
            catch { }
        }

        private void Finish()
        {
            if (_closing) return;
            _closing = true;

            try { _timer.Stop(); } catch { }

            try { Close(); } catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            RemoveHooks();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_resourcesReleased)
            {
                _resourcesReleased = true;
                try { if (_timer != null) { _timer.Stop(); _timer.Dispose(); } } catch { }
                try { if (_clockFontScaled != null) _clockFontScaled.Dispose(); } catch { }
                try { _fClock.Dispose(); } catch { }
                try { _fHeadline.Dispose(); } catch { }
                try { _fMessage.Dispose(); } catch { }
                try { _fSmall.Dispose(); } catch { }
                try { _fLabel.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
