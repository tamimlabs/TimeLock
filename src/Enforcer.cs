using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace TimeLock
{
    /// <summary>
    /// Runs as LocalSystem on a schedule (logon, boot, and every minute).
    ///
    /// It is the authority: it owns the state file, decides when a lock is
    /// over, and guarantees a lock client is present in every interactive
    /// session.  If a client is killed -- or the machine reboots mid-lock --
    /// the client is respawned.  Crucially, the countdown is an absolute
    /// wall-clock end time, so the lock always terminates even if every
    /// client is gone and the enforcer is restarted from scratch.
    /// </summary>
    internal static class EnforcerProgram
    {
        private const int PollMs = 1500;
        private const int SpawnBackoffMs = 6000;

        private static Dictionary<int, DateTime> _lastSpawn = new Dictionary<int, DateTime>();

        public static int Main(string[] args)
        {
            if (HasArg(args, "--status") || HasArg(args, "/status"))
                return PrintStatus();

            Mutex mutex = null;
            bool ownsMutex = false;

            try
            {
                bool createdNew;
                mutex = new Mutex(true, "Global\\TimeLock.Enforcer", out createdNew);
                ownsMutex = createdNew;
                if (!createdNew)
                {
                    // Another enforcer is already watching. The 1-minute trigger
                    // spawns a fresh one shortly, so simply step aside.
                    return 0;
                }
            }
            catch (AbandonedMutexException)
            {
                // The previous holder died without releasing; the name is ours.
                ownsMutex = true;
            }
            catch
            {
                // Could not create the global mutex at all -- run anyway rather
                // than skip enforcement entirely.
            }

            try
            {
                RunWatchdog();
                return 0;
            }
            catch (Exception ex)
            {
                Log("fatal: " + ex);
                return 1;
            }
            finally
            {
                if (mutex != null)
                {
                    try { if (ownsMutex) mutex.ReleaseMutex(); } catch { }
                    try { mutex.Dispose(); } catch { }
                }
            }
        }

        private static void RunWatchdog()
        {
            Store.EnsureDir();

            while (true)
            {
                LockState state = Store.Load();

                if (!state.IsActive)
                {
                    if (state.Enabled)
                    {
                        // Finished but not tidied: persist a clean "off".
                        SafeSave(LockState.Disabled());
                    }
                    Proc.KillAllClients();
                    RestoreShell();
                    return;
                }

                Enforce(state);
                SuppressShell();

                Thread.Sleep(PollMs);
            }
        }

        // ---------------------------------------------------------------
        //  Shell suppression.
        //
        //  Win+Ctrl+D (new desktop) and Win+Ctrl+Left/Right (virtual desktop
        //  switching) are handled entirely by explorer.exe. A keyboard hook
        //  alone is not a trustworthy defence -- hooks are user-mode and can
        //  be missed -- so while a lock is active the shell itself is taken
        //  away. The lock window is an ordinary top-level window and does not
        //  need explorer to stay on screen.
        //
        //  Ownership of this lives here rather than in the lock client,
        //  because the enforcer keeps running even if every client is killed.
        // ---------------------------------------------------------------
        private static void SuppressShell()
        {
            // Stop Winlogon from resurrecting explorer behind our back.
            ShellPolicy.DisableAutoRestart();

            foreach (int sid in Native.GetLockableSessions())
            {
                if (Proc.KillSessionProcess(sid, "explorer") > 0)
                    Log("suppressed shell in session " + sid);
            }
        }

        private static void RestoreShell()
        {
            ShellPolicy.RestoreAutoRestart();

            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                          "explorer.exe");
            if (!File.Exists(explorer)) return;

            foreach (int sid in Native.GetActiveSessions())
            {
                if (Proc.SessionHasProcess(sid, "explorer")) continue;

                List<string> diag = new List<string>();
                string err;
                if (Native.LaunchInSession(sid, explorer, diag, out err))
                    Log("restored shell in session " + sid);
                else
                    Log("could not restore shell in session " + sid + ": " + err);
            }
        }

        private static void Enforce(LockState state)
        {
            List<int> sessions = Native.GetLockableSessions();
            Dictionary<int, int> running = Proc.ClientSessions();

            // Reap clients in sessions that no longer qualify.
            foreach (KeyValuePair<int, int> kv in running)
            {
                if (!sessions.Contains(kv.Key)) KillClientsInSession(kv.Key);
            }

            foreach (int sid in sessions)
            {
                int count;
                if (running.TryGetValue(sid, out count) && count > 0) continue;

                DateTime last;
                if (_lastSpawn.TryGetValue(sid, out last))
                {
                    if ((DateTime.UtcNow - last).TotalMilliseconds < SpawnBackoffMs) continue;
                }

                _lastSpawn[sid] = DateTime.UtcNow;

                string exe = Path.Combine(Paths.InstallDir, Paths.ClientExe);
                if (!File.Exists(exe))
                {
                    Log("client missing at " + exe);
                    return;
                }

                List<string> diag = new List<string>();
                string err;
                if (Native.LaunchInSession(sid, exe, diag, out err))
                {
                    Log("spawned client in session " + sid + " [" + string.Join(" ", diag.ToArray()) + "]");
                }
                else
                {
                    Log("spawn failed session " + sid + ": " + err);
                }
            }
        }

        private static void KillClientsInSession(int sessionId)
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName("TimeLock.Client"); }
            catch { return; }

            foreach (Process p in procs)
            {
                try
                {
                    if (p.SessionId == sessionId) p.Kill();
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
        }

        private static void SafeSave(LockState state)
        {
            try { Store.Save(state); }
            catch (Exception ex) { Log("save failed: " + ex.Message); }
        }

        private static void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(Paths.DataDir);
                File.AppendAllText(
                    Path.Combine(Paths.DataDir, "enforcer.log"),
                    DateTime.Now.ToString("s") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }

        private static int PrintStatus()
        {
            LockState s = Store.Load();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("TimeLock status");
            sb.AppendLine("  data dir   : " + Paths.DataDir);
            sb.AppendLine("  install dir: " + Paths.InstallDir);
            sb.AppendLine("  enabled    : " + s.Enabled);
            sb.AppendLine("  active     : " + s.IsActive);
            sb.AppendLine("  remaining  : " + (s.IsActive ? Fmt.Countdown(s.Remaining) : "-"));
            sb.AppendLine("  unlocks at : " +
                (s.EndUtcTicks > 0 ? new DateTime(s.EndUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "-"));
            sb.AppendLine("  message    : " + (string.IsNullOrEmpty(s.Message) ? "-" : s.Message));
            sb.AppendLine("  sessions   : " + string.Join(", ", Native.GetLockableSessions().ToArray()));
            sb.AppendLine("  clients    : " + Proc.ClientSessions().Count);
            Console.Write(sb.ToString());
            return 0;
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (string a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
