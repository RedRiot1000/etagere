using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace Etagere
{
    // Mesure une session de jeu : on attend qu'un processus démarre dans le dossier du jeu, puis qu'il n'en reste plus.
    // Ça marche même quand le jeu passe par Steam, Epic ou un launcher (seul le dossier compte).
    public static class PlayTracker
    {
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        static readonly HashSet<Item> tracking = new HashSet<Item>();

        public static string ResolveDir(Item it)
        {
            if (!string.IsNullOrEmpty(it.GameDir) && Directory.Exists(it.GameDir)) return it.GameDir;
            string dir = null;
            if (it.Key != null && it.Key.StartsWith("steam:")) dir = Steam.InstallDir(it.Key.Substring(6));
            if (dir == null)
            {
                string exe = null;
                if (it.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe = it.Path;
                else if (ShortcutInfo.IsShortcut(it.Path))
                {
                    var t = ShortcutInfo.Target(it.Path);
                    var appid = ShortcutInfo.SteamAppId(t);
                    if (appid != null) dir = Steam.InstallDir(appid);
                    else exe = ShortcutInfo.ExeOf(t);
                }
                if (exe == null && it.IconSource != null && it.IconSource.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe = it.IconSource;
                if (dir == null && exe != null && File.Exists(exe)) dir = Path.GetDirectoryName(exe);
            }
            // Un dossier trop général (racine d'un disque, Program Files, Steam lui-même…) fausserait la mesure
            if (dir == null || !Directory.Exists(dir)) return null;
            var full = Path.GetFullPath(dir).TrimEnd('\\');
            if (full.Count(c => c == '\\') < 1) return null;
            var generic = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), Steam.Root ?? "?" };
            if (generic.Any(g => string.Equals(g.TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase))) return null;
            it.GameDir = full;
            return full;
        }

        public static void Start(Item it, Dispatcher ui, Action<Item, long> onEnd)
        {
            var dir = ResolveDir(it);
            if (dir == null) return;
            lock (tracking) { if (!tracking.Add(it)) return; }
            var prefix = dir + "\\";
            var t = new Thread(() =>
            {
                try
                {
                    DateTime? first = null;
                    var waitUntil = DateTime.Now.AddMinutes(4);   // les launchers peuvent être lents
                    while (first == null && DateTime.Now < waitUntil)
                    {
                        if (AnyRunningIn(prefix)) first = DateTime.Now;
                        else Thread.Sleep(2000);
                    }
                    if (first == null) return;

                    var last = DateTime.Now;
                    int misses = 0;
                    while (misses < 3)   // tolère le passage launcher → jeu
                    {
                        Thread.Sleep(3000);
                        if (AnyRunningIn(prefix)) { last = DateTime.Now; misses = 0; }
                        else misses++;
                    }
                    var seconds = (long)(last - first.Value).TotalSeconds;
                    if (seconds >= 15) ui.BeginInvoke(new Action(() => onEnd(it, seconds)));
                }
                catch { }
                finally { lock (tracking) tracking.Remove(it); }
            });
            t.IsBackground = true;
            t.Start();
        }

        static bool AnyRunningIn(string prefix)
        {
            var me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == me || p.Id <= 4) continue;
                    var path = ImagePath(p.Id);
                    if (path != null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                }
                finally { p.Dispose(); }
            }
            return false;
        }

        static string ImagePath(int pid)
        {
            var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }
    }
}
