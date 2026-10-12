using System;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TimeLock
{
    internal static class AppProgram
    {
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool dump = false;
            foreach (string a in args)
                if (string.Equals(a, "--dump", StringComparison.OrdinalIgnoreCase)) dump = true;

            MainForm f = new MainForm(dump);
            Application.Run(f);
        }
    }

    internal sealed class MainForm : Form
    {
        private static readonly Color Accent = Color.FromArgb(56, 142, 220);
        private static readonly Color TextDim = Color.FromArgb(110, 118, 138);
        private static readonly Color TextBright = Color.FromArgb(32, 36, 46);
        private static readonly Color Card = Color.FromArgb(248, 249, 251);
        private static readonly Color Stroke = Color.FromArgb(224, 227, 233);

        private readonly TextBox _hours = new TextBox();
        private readonly TextBox _minutes = new TextBox();        private readonly TextBox _message = new TextBox();
        private readonly Button _start = new Button();
        private readonly Button _stop = new Button();
        private readonly Label _status = new Label();
        private readonly Label _headline = new Label();
        private readonly Timer _timer = new Timer();
        private readonly Label _engineNote = new Label();
        private readonly bool _dump;

        public MainForm() : this(false) { }

        public MainForm(bool dump)
        {
            _dump = dump;

            // /win32icon: embeds the icon in the PE resources, but WinForms
            // takes its default form icon from the assembly manifest, which
            // csc.exe does not populate -- without this the title bar shows
            // the stock application icon.
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            Text = "TimeLock";
            ClientSize = new Size(468, 470);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9f);

            BuildUi();

            // Don't leave a random duration field pre-selected on open.
            ActiveControl = _start;

            _timer.Interval = 1000;
            _timer.Tick += (s, e) => RefreshStatus();
            _timer.Start();

            RefreshStatus();
            if (_dump) DumpLayout();
        }

        /// <summary>Diagnostic: write every control's real rectangle.</summary>
        private void DumpLayout()
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("form client=" + ClientSize + " location=" + Location);
                Dump(sb, this, 0);
                string dir = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "TimeLockDump.txt");
                System.IO.File.WriteAllText(dir, sb.ToString());
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText(
                        System.IO.Path.GetTempPath() + "TimeLockDumpError.txt", ex.ToString()); }
                catch { }
            }
        }

        private static void Dump(System.Text.StringBuilder sb, Control parent, int depth)
        {
            foreach (Control c in parent.Controls)
            {
                string text = "";
                try
                {
                    Label lb = c as Label;      if (lb != null) text = lb.Text;
                    Button bt = c as Button;   if (bt != null) text = bt.Text;
                    TextBox tb = c as TextBox; if (tb != null) text = tb.Text;
                }
                catch { }
                sb.AppendLine(new string(' ', depth * 2) +
                    c.GetType().Name +
                    " text='" + Fmt.Trim(Convert.ToString(text), 20) + "'" +
                    " loc=" + c.Location + " size=" + c.Size +
                    " bounds=" + c.Bounds +
                    " visible=" + c.Visible + " autoSize=" + c.AutoSize +
                    " back=" + c.BackColor.Name);
                Dump(sb, c, depth + 1);
            }
        }

        // ------------------------------------------------------------------
        private void BuildUi()
        {
            _headline.Text = "Lock this laptop for a set time";
            _headline.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            _headline.ForeColor = TextBright;
            _headline.AutoSize = false;
            _headline.Size = new Size(400, 60);
            _headline.Location = new Point(34, 26);
            Controls.Add(_headline);

            Label sub = MakeLabel("Enforced by a Windows service, so it survives restarts.", 10f, TextDim);
            sub.Size = new Size(400, 34);
            sub.Location = new Point(34, 80);
            Controls.Add(sub);

            Panel card = new Panel();
            card.BackColor = Card;
            card.Size = new Size(400, 176);
            card.Location = new Point(34, 130);
            card.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Stroke))
                    e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
            };
            Controls.Add(card);

            Label durLabel = MakeLabel("DURATION", 9f, TextDim);
            durLabel.Location = new Point(20, 18);
            card.Controls.Add(durLabel);

            // Plain text fields rather than NumericUpDown: the spin buttons
            // overlap the value at large font sizes, which makes the number
            // unreadable. The presets cover the common cases.
            _hours.Text = "2";
            StyleDurationBox(_hours, 20);
            _hours.KeyPress += (s, e) => AllowDigitsOnly(e);
            card.Controls.Add(_hours);

            Label hLbl = MakeLabel("hours", 10f, TextDim);
            hLbl.Size = new Size(58, 18);
            hLbl.Location = new Point(138, 53);
            card.Controls.Add(hLbl);

            _minutes.Text = "0";
            StyleDurationBox(_minutes, 200);
            _minutes.KeyPress += (s, e) => AllowDigitsOnly(e);
            card.Controls.Add(_minutes);

            Label mLbl = MakeLabel("minutes", 10f, TextDim);
            mLbl.Size = new Size(80, 18);
            mLbl.Location = new Point(318, 53);
            card.Controls.Add(mLbl);

            Label msgLabel = MakeLabel("MESSAGE ON THE LOCK SCREEN (OPTIONAL)", 9f, TextDim);
            msgLabel.Size = new Size(360, 18);
            msgLabel.Location = new Point(20, 96);
            card.Controls.Add(msgLabel);

            _message.Location = new Point(20, 116);
            _message.Size = new Size(360, 28);
            _message.MaxLength = 120;
            _message.BorderStyle = BorderStyle.FixedSingle;
            card.Controls.Add(_message);

            FlowLayoutPanel presets = new FlowLayoutPanel();
            presets.Location = new Point(34, 322);
            presets.Size = new Size(400, 38);
            presets.FlowDirection = FlowDirection.LeftToRight;
            presets.WrapContents = false;
            foreach (int[] p in new[] { new[] { 0, 15 }, new[] { 0, 30 }, new[] { 1, 0 }, new[] { 2, 0 }, new[] { 4, 0 }, new[] { 8, 0 } })
            {
                int hh = p[0], mm = p[1];
                Button b = new Button();
                b.Text = hh > 0 ? hh + "h" : mm + "m";
                b.Tag = new int[] { hh, mm };
                b.Size = new Size(58, 30);
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Stroke;
                b.BackColor = Color.White;
                b.Click += (s, e) => { _hours.Text = hh.ToString(); _minutes.Text = mm.ToString(); };
                presets.Controls.Add(b);
            }
            Controls.Add(presets);

            _start.Text = "Start lock";
            _start.Location = new Point(34, 372);
            _start.Size = new Size(260, 46);
            _start.FlatStyle = FlatStyle.Flat;
            _start.FlatAppearance.BorderSize = 0;
            _start.BackColor = Accent;
            _start.ForeColor = Color.White;
            _start.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            _start.Cursor = Cursors.Hand;
            _start.Click += OnStart;
            Controls.Add(_start);

            _stop.Text = "Stop now";
            _stop.Location = new Point(304, 372);
            _stop.Size = new Size(130, 46);
            _stop.FlatStyle = FlatStyle.Flat;
            _stop.FlatAppearance.BorderColor = Stroke;
            _stop.BackColor = Color.White;
            _stop.ForeColor = Color.FromArgb(70, 76, 92);
            _stop.Cursor = Cursors.Hand;
            _stop.Click += OnStop;
            Controls.Add(_stop);

            _status.Text = "";
            _status.Font = new Font("Segoe UI", 10f);
            _status.ForeColor = TextDim;
            _status.BackColor = Color.Transparent;
            _status.AutoSize = false;
            _status.Size = new Size(400, 34);
            _status.Location = new Point(34, 428);
            _status.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_status);

            _engineNote.Text = "";
            _engineNote.Font = new Font("Segoe UI", 9f);
            _engineNote.ForeColor = Color.FromArgb(190, 70, 70);
            _engineNote.BackColor = Color.Transparent;
            _engineNote.AutoSize = false;
            _engineNote.Size = new Size(400, 20);
            _engineNote.Location = new Point(34, 452);
            Controls.Add(_engineNote);
        }

        private static void StyleDurationBox(TextBox box, int x)
        {
            box.Font = new Font("Consolas", 17f, FontStyle.Bold);
            box.AutoSize = false;                 // otherwise the height is
            box.BorderStyle = BorderStyle.FixedSingle;   // recomputed from the font
            box.Size = new Size(112, 42);
            box.Location = new Point(x, 39);
            box.TextAlign = HorizontalAlignment.Center;
            box.MaxLength = 2;
        }

        private static void AllowDigitsOnly(KeyPressEventArgs e)
        {
            if (!char.IsDigit(e.KeyChar) && e.KeyChar != (char)Keys.Back)
            {
                e.Handled = true;
                System.Media.SystemSounds.Beep.Play();
            }
        }

        private static int ReadBox(TextBox box, int max)
        {
            int v;
            if (!int.TryParse(box.Text, out v)) return 0;
            if (v < 0) return 0;
            if (v > max) return max;
            return v;
        }

        private static Label MakeLabel(string text, float size, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = new Font("Segoe UI", size);
            l.ForeColor = color;
            // Label.BackColor is opaque white by default. On the tinted card
            // that paints a solid rectangle over whatever sits underneath, so
            // every label must be explicitly transparent.
            l.BackColor = Color.Transparent;
            l.AutoSize = false;
            l.Size = new Size(200, 18);
            return l;
        }

        private void SyncPresetHighlight() { }

        // ------------------------------------------------------------------
        private void RefreshStatus()
        {
            LockState s = Store.Load();

            if (s.IsActive)
            {
                _status.ForeColor = Accent;
                _status.BackColor = Color.Transparent;
                _status.Text = "Locked  -  " + Fmt.Countdown(s.Remaining) + " remaining  (" +
                               new DateTime(s.EndUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm") + " unlock)";
                _start.Enabled = false;
                _stop.Enabled = true;
            }
            else
            {
                _status.ForeColor = TextDim;
            _status.BackColor = Color.Transparent;
                _status.Text = "Not locked.";
                _start.Enabled = true;
                _stop.Enabled = false;
            }

            bool installed = File_Exists(System.IO.Path.Combine(Paths.InstallDir, Paths.EnforcerExe));
            _engineNote.Text = installed ? "" : "Enforcement service not found - run Install.ps1 first.";
        }

        private static bool File_Exists(string p)
        {
            try { return System.IO.File.Exists(p); }
            catch { return false; }
        }

        // ------------------------------------------------------------------
        private void OnStart(object sender, EventArgs e)
        {
            int totalMinutes = (ReadBox(_hours, 72) * 60) + ReadBox(_minutes, 59);
            if (totalMinutes <= 0)
            {
                MessageBox.Show(this, "Choose a duration first.", "TimeLock",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                LockState s = LockState.Create(TimeSpan.FromMinutes(totalMinutes), _message.Text);
                Store.Save(s);
                KickEnforcer();

                _start.Enabled = false;
                _status.ForeColor = Accent;
                _status.BackColor = Color.Transparent;
                _status.Text = "Locked  -  " + Fmt.Countdown(s.Remaining) + " remaining";

                System.Threading.Thread.Sleep(400);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not start the lock:\n\n" + ex.Message, "TimeLock",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnStop(object sender, EventArgs e)
        {
            DialogResult r = MessageBox.Show(this,
                "End the lock immediately and unlock this laptop?",
                "TimeLock", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            try
            {
                LockState s = Store.Load();
                s.Enabled = false;
                Store.Save(s);
                Proc.Kill("TimeLock.Client");
                RefreshStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not stop the lock:\n\n" + ex.Message, "TimeLock",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void KickEnforcer()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe",
                    "/run /tn \"" + Paths.TaskName + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process p = Process.Start(psi);
                if (p != null) p.Dispose();
            }
            catch { }
        }
    }
}
