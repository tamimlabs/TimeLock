using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TimeLock
{
    // ------------------------------------------------------------------
    //  Filesystem locations
    // ------------------------------------------------------------------
    public static class Paths
    {
        public static string DataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "TimeLock");
            }
        }

        public static string StateFile { get { return Path.Combine(DataDir, "state.json"); } }
        public static string InstallDir { get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); } }

        public const string TaskName = "TimeLock.Enforce";
        public const string ClientExe = "TimeLock.Client.exe";
        public const string EnforcerExe = "TimeLock.Enforce.exe";
        public const string AppExe = "TimeLock.App.exe";
        public const string ProcessPrefix = "TimeLock.";
    }

    // ------------------------------------------------------------------
    //  Lock state.  The lock is anchored to an ABSOLUTE end time, never a
    //  countdown, so sleep / suspend / reboot / clock quirks can never make
    //  the lock drift or extend itself.
    // ------------------------------------------------------------------
    [DataContract]
    public sealed class LockState
    {
        [DataMember] public bool Enabled;
        [DataMember] public long EndUtcTicks;
        [DataMember] public long CreatedUtcTicks;
        [DataMember] public int DurationSeconds;
        [DataMember] public string Message;

        [IgnoreDataMember]
        public bool IsActive
        {
            get { return Enabled && EndUtcTicks > DateTime.UtcNow.Ticks; }
        }

        [IgnoreDataMember]
        public TimeSpan Remaining
        {
            get
            {
                long ticks = EndUtcTicks - DateTime.UtcNow.Ticks;
                return ticks > 0 ? TimeSpan.FromTicks(ticks) : TimeSpan.Zero;
            }
        }

        public static LockState Disabled()
        {
            LockState s = new LockState();
            s.Enabled = false;
            s.EndUtcTicks = DateTime.UtcNow.Ticks;
            s.CreatedUtcTicks = DateTime.UtcNow.Ticks;
            s.DurationSeconds = 0;
            s.Message = "";
            return s;
        }

        public static LockState Create(TimeSpan duration, string message)
        {
            DateTime now = DateTime.UtcNow;
            LockState s = new LockState();
            s.Enabled = true;
            s.DurationSeconds = (int)Math.Max(1, Math.Min(duration.TotalSeconds, 72 * 3600));
            s.EndUtcTicks = now.AddSeconds(s.DurationSeconds).Ticks;
            s.CreatedUtcTicks = now.Ticks;
            s.Message = message ?? "";
            return s;
        }
    }

    // ------------------------------------------------------------------
    //  Durable state read/write.  Every failure path degrades to "unlocked"
    //  so a corrupt file can never brick the machine.
    // ------------------------------------------------------------------
    public static class Store
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static void EnsureDir()
        {
            try
            {
                if (!Directory.Exists(Paths.DataDir))
                    Directory.CreateDirectory(Paths.DataDir);
            }
            catch { }
        }

        public static LockState Load()
        {
            try
            {
                string path = Paths.StateFile;
                if (!File.Exists(path)) return LockState.Disabled();

                LockState s;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    DataContractJsonSerializer ser =
                        new DataContractJsonSerializer(typeof(LockState));
                    s = (LockState)ser.ReadObject(fs);
                }

                if (s == null) return LockState.Disabled();
                if (s.Message == null) s.Message = "";

                // Deliberately NOT clearing Enabled here: the enforcer needs to
                // see "finished but not yet tidied up" so it can rewrite a
                // clean state. IsActive is the single source of truth.
                if (s.EndUtcTicks <= 0) s.Enabled = false;
                return s;
            }
            catch
            {
                // A corrupt or unreadable state must never mean "locked".
                return LockState.Disabled();
            }
        }

        public static void Save(LockState state)
        {
            EnsureDir();
            string path = Paths.StateFile;
            string tmp = path + ".tmp";

            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(LockState));
            byte[] bytes;
            using (MemoryStream ms = new MemoryStream())
            {
                ser.WriteObject(ms, state);
                bytes = ms.ToArray();
            }

            using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); }
                catch { CopyOver(path, tmp); }
            }
            else
            {
                try { File.Move(tmp, path); }
                catch { CopyOver(path, tmp); }
            }
        }

        private static void CopyOver(string path, string tmp)
        {
            try
            {
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            catch { }
        }


    }

    // ------------------------------------------------------------------
    //  Win32
    // ------------------------------------------------------------------
    public static class Native
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        public const int HC_ACTION = 0;

        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_XBUTTONDOWN = 0x020B;
        public const int WM_XBUTTONUP = 0x020C;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public int x;
            public int y;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSEnumerateSessions(IntPtr hServer, int Reserved, int Version,
            out IntPtr ppSessionInfo, out int pCount);

        [DllImport("wtsapi32.dll")]
        public static extern void WTSFreeMemory(IntPtr pMemory);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSQuerySessionInformation(IntPtr hServer, int SessionId,
            int InfoClass, out IntPtr ppBuffer, out int pBytesReturned);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSQueryUserToken(int SessionId, out IntPtr phToken);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess,
            IntPtr lpTokenAttributes, IntPtr lpSecurityAttributes,
            int dwTokenType, out IntPtr phNewToken);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool SetTokenInformation(IntPtr TokenHandle, int TokenInformationClass,
            ref TOKEN_SESSION_ID TokenInformation, int TokenInformationLength);

        [DllImport("userenv.dll", SetLastError = true)]
        public static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

        [DllImport("userenv.dll")]
        public static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        // NOTE: lpCommandLine is LPWSTR (the callee may modify it), so it must
        // be marshalled as a mutable StringBuilder. Passing a read-only string
        // here causes a hard access violation inside advapi32.
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcessAsUser(IntPtr hToken, string lpApplicationName,
            StringBuilder lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
            bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
            string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessWithTokenW")]
        public static extern bool CreateProcessWithTokenW(IntPtr hToken, int dwLogonFlags,
            string lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags,
            IntPtr lpEnvironment, string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);


        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseProcessHandle(IntPtr hObject);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessWithTokenW")]
        public static extern bool CreateProcessWithTokenW(IntPtr hToken, int dwLogonFlags,
            string lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags,
            IntPtr lpEnvironment, string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_SESSION_ID
        {
            public IntPtr SessionId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct STARTUPINFO
        {
            public int cb;
            public IntPtr lpReserved;
            public IntPtr lpDesktop;
            public IntPtr lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_SESSION_INFO
        {
            public int SessionId;
            public IntPtr pWinStationName;
            public int State;
        }

        public const int WTSActive = 0;
        public const int WTSDisconnected = 7;
        public const int WTSUserName = 5;

        public const uint TOKEN_ADJUST_DEFAULT = 0x00000020;
        public const uint TOKEN_ADJUST_SESSIONID = 0x00000008;
        public const uint TOKEN_QUERY = 0x00000008;
        public const uint TOKEN_DUPLICATE = 0x00000002;
        public const uint MAXIMUM_ALLOWED = 0x02000000;
        public const int SecurityImpersonation = 2;
        public const int TokenPrimary = 1;
        public const int TokenSessionId = 1;
        public const int LOGON_WITH_PROFILE = 0x00000001;

        public const uint CREATE_NEW_CONSOLE = 0x00000010;
        public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

        /// <summary>
        /// Sessions that should be showing the lock screen: real interactive
        /// user sessions that are connected or merely disconnected (so the lock
        /// is waiting for them the moment they reconnect).
        /// </summary>
        public static List<int> GetLockableSessions()
        {
            return EnumerateSessions(false);
        }

        /// <summary>Sessions where someone is actively looking at the screen.</summary>
        public static List<int> GetActiveSessions()
        {
            return EnumerateSessions(true);
        }

        private static List<int> EnumerateSessions(bool activeOnly)
        {
            List<int> result = new List<int>();
            IntPtr buf = IntPtr.Zero;
            int count = 0;

            if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out buf, out count))
                return result;

            try
            {
                int stride = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                for (int i = 0; i < count; i++)
                {
                    IntPtr slot = IntPtr.Add(buf, i * stride);
                    WTS_SESSION_INFO info =
                        (WTS_SESSION_INFO)Marshal.PtrToStructure(slot, typeof(WTS_SESSION_INFO));

                    if (info.SessionId == 0) continue;
                    if (activeOnly)
                    {
                        if (info.State != WTSActive) continue;
                    }
                    else if (info.State != WTSActive && info.State != WTSDisconnected) continue;

                    if (!SessionHasRealUser(info.SessionId)) continue;

                    result.Add(info.SessionId);
                }
            }
            catch { }
            finally
            {
                if (buf != IntPtr.Zero) WTSFreeMemory(buf);
            }

            return result;
        }

        private static bool SessionHasRealUser(int sessionId)
        {
            IntPtr buf = IntPtr.Zero;
            int bytes = 0;
            try
            {
                if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, WTSUserName, out buf, out bytes))
                    return false;
                if (buf == IntPtr.Zero) return false;

                string user = Marshal.PtrToStringUni(buf);
                if (string.IsNullOrEmpty(user)) return false;

                // Session 0 / service pseudo-sessions sometimes report a name.
                if (string.Equals(user, "SYSTEM", StringComparison.OrdinalIgnoreCase)) return false;
                if (user.IndexOf('#') >= 0 && user.IndexOf('\\') < 0) return false;

                return true;
            }
            catch { return false; }
            finally
            {
                if (buf != IntPtr.Zero) WTSFreeMemory(buf);
            }
        }

        /// <summary>
        /// Launch a process inside a specific interactive user session.
        /// Must be called as LocalSystem.
        ///
        /// Several strategies are tried in turn because none of them is
        /// reliable on every Windows configuration -- job objects, UAC
        /// filtering and session isolation each break a different one.
        /// Every attempt is recorded in <paramref name="diag"/>.
        /// </summary>
        public static bool LaunchInSession(int sessionId, string exePath, List<string> diag, out string error)
        {
            error = null;
            IntPtr userToken = IntPtr.Zero;
            IntPtr primaryToken = IntPtr.Zero;
            IntPtr envBlock = IntPtr.Zero;
            IntPtr desktop = IntPtr.Zero;
            PROCESS_INFORMATION pi = new PROCESS_INFORMATION();
            bool started = false;

            if (diag == null) diag = new List<string>();

            try
            {
                if (!WTSQueryUserToken(sessionId, out userToken) || userToken == IntPtr.Zero)
                {
                    int e = Marshal.GetLastWin32Error();
                    error = "WTSQueryUserToken failed (" + e + ")";
                    diag.Add("WTSQueryUserToken=" + e);
                    return false;
                }
                diag.Add("WTSQueryUserToken=ok");

                string dir = Path.GetDirectoryName(exePath);
                STARTUPINFO si = new STARTUPINFO();
                si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                desktop = Marshal.StringToHGlobalUni("WinSta0\\Default");
                si.lpDesktop = desktop;

                // CREATE_UNICODE_ENVIRONMENT only. CREATE_NEW_CONSOLE buys
                // nothing for a GUI app and is a common source of failures.
                const uint flags = CREATE_UNICODE_ENVIRONMENT;

                // ---- A: the session token straight into CreateProcessAsUser
                SetSessionId(userToken, sessionId);
                if (CreateEnvironmentBlock(out envBlock, userToken, false))
                    diag.Add("envblock=ok");
                else
                {
                    envBlock = IntPtr.Zero;
                    diag.Add("envblock=failed(" + Marshal.GetLastWin32Error() + ")");
                }

                PROCESS_INFORMATION a;
                started = CreateProcessAsUser(userToken, exePath, new StringBuilder("\"" + exePath + "\""),
                    IntPtr.Zero, IntPtr.Zero, false, flags, envBlock, dir, ref si, out a);
                if (started) { pi = a; diag.Add("A:CreateProcessAsUser=ok"); return true; }
                diag.Add("A:CreateProcessAsUser=" + Marshal.GetLastWin32Error());

                // ---- B: duplicated primary token
                IntPtr dup;
                if (DuplicateTokenEx(userToken, MAXIMUM_ALLOWED, IntPtr.Zero, IntPtr.Zero,
                                     SecurityImpersonation, out dup))
                {
                    primaryToken = dup;
                    SetSessionId(primaryToken, sessionId);

                    IntPtr env2;
                    if (CreateEnvironmentBlock(out env2, primaryToken, false))
                    {
                        if (envBlock != IntPtr.Zero) DestroyEnvironmentBlock(envBlock);
                        envBlock = env2;
                    }

                    PROCESS_INFORMATION b;
                    if (CreateProcessAsUser(primaryToken, exePath, new StringBuilder("\"" + exePath + "\""),
                            IntPtr.Zero, IntPtr.Zero, false, flags, envBlock, dir, ref si, out b))
                    {
                        pi = b; started = true; diag.Add("B:CreateProcessAsUser(dup)=ok"); return true;
                    }
                    diag.Add("B:CreateProcessAsUser(dup)=" + Marshal.GetLastWin32Error());
                }
                else
                {
                    diag.Add("B:DuplicateTokenEx=" + Marshal.GetLastWin32Error());
                }

                // ---- C: CreateProcessWithTokenW
                PROCESS_INFORMATION c;
                if (CreateProcessWithTokenW(userToken, LOGON_WITH_PROFILE, exePath,
                        new StringBuilder("\"" + exePath + "\""), IntPtr.Zero, IntPtr.Zero, false,
                        flags, IntPtr.Zero, dir, ref si, out c))
                {
                    pi = c; started = true; diag.Add("C:CreateProcessWithTokenW=ok"); return true;
                }
                diag.Add("C:CreateProcessWithTokenW=" + Marshal.GetLastWin32Error());

                error = "could not start in session " + sessionId + " [" + string.Join(" ", diag.ToArray()) + "]";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                diag.Add("exception=" + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
            finally
            {
                if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
                if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
                if (desktop != IntPtr.Zero) Marshal.FreeHGlobal(desktop);
                if (envBlock != IntPtr.Zero) DestroyEnvironmentBlock(envBlock);
                if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
                if (userToken != IntPtr.Zero) CloseHandle(userToken);
            }
        }

        private static void SetSessionId(IntPtr token, int sessionId)
        {
            try
            {
                TOKEN_SESSION_ID ts = new TOKEN_SESSION_ID();
                ts.SessionId = new IntPtr(sessionId);
                SetTokenInformation(token, TokenSessionId, ref ts, Marshal.SizeOf(typeof(TOKEN_SESSION_ID)));
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------
    public static class Fmt
    {
        public static string Countdown(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            long total = (long)t.TotalSeconds;

            int days = (int)(total / 86400);
            int hours = (int)((total % 86400) / 3600);
            int mins = (int)((total % 3600) / 60);
            int secs = (int)(total % 60);

            if (days > 0)
                return string.Format("{0}d {1:00}:{2:00}:{3:00}", days, hours, mins, secs);

            return string.Format("{0:00}:{1:00}:{2:00}", hours, mins, secs);
        }

        public static string Short(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            long total = (long)t.TotalSeconds;
            int hours = (int)(total / 3600);
            int mins = (int)((total % 3600) / 60);

            if (hours > 0) return string.Format("{0}h {1}m", hours, mins);
            if (mins > 0) return string.Format("{0}m {1}s", mins, (int)(total % 60));
            return string.Format("{0}s", (int)total);
        }

        public static string Trim(string s, int max)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length > max) s = s.Substring(0, max);
            return s;
        }
    }

    // ------------------------------------------------------------------
    //  Winlogon shell-restart policy.
    //
    //  Killing explorer.exe alone does not stick: Winlogon immediately
    //  restarts the shell, which brings back every shell-owned shortcut
    //  (Win+Ctrl+D new desktop, virtual desktop switching, taskbar menus).
    //  AutoRestartShell=0 turns that off so the lock owns the shell.
    //
    //  The original value is remembered so it can be put back, leaving the
    //  machine's normal behaviour untouched whenever no lock is active.
    // ------------------------------------------------------------------
    public static class ShellPolicy
    {
        private const string KeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
        private const string ValueName = "AutoRestartShell";
        private static int _saved = -2;   // -2 = nothing saved yet

        public static bool DisableAutoRestart()
        {
            try
            {
                if (_saved == -2)
                {
                    _saved = ReadAutoRestart() ? 1 : 0;
                }
                WriteAutoRestart(false);
                return true;
            }
            catch { return false; }
        }

        public static bool RestoreAutoRestart()
        {
            try
            {
                if (_saved == -2) _saved = 1;
                WriteAutoRestart(_saved == 1);
                _saved = -2;
                return true;
            }
            catch { return false; }
        }

        public static bool ReadAutoRestart()
        {
            try
            {
                object v = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(KeyPath, false).GetValue(ValueName, 1);
                return Convert.ToInt32(v) != 0;
            }
            catch { return true; }
        }

        public static void WriteAutoRestart(bool enabled)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k =
                       Microsoft.Win32.Registry.LocalMachine.OpenSubKey(KeyPath, true))
                {
                    if (k != null) k.SetValue(ValueName, enabled ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        /// <summary>Hard reset, used by the uninstaller.</summary>
        public static void ForceDefault()
        {
            try { WriteAutoRestart(true); } catch { }
        }
    }

    public static class Proc
    {
        public static bool SessionHasProcess(int sessionId, string name)
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch { return false; }

            foreach (Process p in procs)
            {
                try { if (p.SessionId == sessionId) return true; }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            return false;
        }

        public static int KillSessionProcess(int sessionId, string name)
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch { return 0; }

            int killed = 0;
            foreach (Process p in procs)
            {
                try
                {
                    if (p.SessionId == sessionId) { p.Kill(); killed++; }
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            return killed;
        }

        /// <summary>
        /// Map of Windows session id -> number of live lock clients in it.
        /// </summary>
        public static Dictionary<int, int> ClientSessions()
        {
            Dictionary<int, int> map = new Dictionary<int, int>();
            Process[] procs;
            try { procs = Process.GetProcessesByName("TimeLock.Client"); }
            catch { return map; }

            foreach (Process p in procs)
            {
                try
                {
                    int sid = p.SessionId;
                    if (map.ContainsKey(sid)) map[sid] = map[sid] + 1;
                    else map[sid] = 1;
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }

            return map;
        }

        public static void KillAllClients()
        {
            Kill("TimeLock.Client");
        }

        public static void Kill(string name)
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch { return; }

            foreach (Process p in procs)
            {
                try { p.Kill(); }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
        }
    }
}
