using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace Etagere
{
    // Recherche sur tout le PC : launchers (Steam, Epic, GOG, Ubisoft), menu Démarrer (y compris les applis du
    // Microsoft Store et les jeux Xbox) et dossiers de jeux sur tous les disques.
    public static class Scanner
    {
        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        static readonly string[] JunkNames =
        {
            "uninstall", "désinstall", "desinstall", "unins", "help", "aide", "manual", "manuel", "documentation", "docs",
            "website", "site web", "homepage", "faq", "release notes", "nouveautés", "readme", "lisez", "à propos", "a propos",
            "bug report", "support center", "license", "licence", "what's new", "quelles sont", "command prompt",
            "invite de commandes", "configurer", "install additional", "observateur", "moniteur de ressources",
            "analyseur de performances", "planificateur", "gestion de l'ordinateur", "gestionnaire des tâches",
            "rechercher les mises", "outils windows", "panneau de configuration", "exécuter", "odbc", "iscsi", "pare-feu",
            "registre", "services", "diagnostic", "défragmenter", "nettoyage de disque", "informations système",
            "configuration du système", "lecteur de récupération", "clavier visuel", "narrateur", "loupe", "accès vocal",
            "sous-titres", "table des caractères", "enregistreur d'actions", "hub de commentaires", "prise en main",
            "actions par clic", "sauvegarde windows", "ordinateur", "connexion bureau à distance", "powershell ise", "crash", "redeem"
        };
        static readonly string[] JunkExe = { "unins", "setup", "crash", "report", "redist", "vcredist", "vc_redist", "dxsetup",
            "dotnet", "helper", "updater", "update", "cefprocess", "cefsharp", "webhelper", "install", "prereq", "easyanticheat",
            "battleye", "be_service", "cleanup", "config", "settings", "server", "dedicated", "benchmark", "touchup", "unitycrashhandler" };
        static readonly string[] SkipExt = { ".chm", ".txt", ".html", ".htm", ".pdf", ".msc", ".url", ".hlp", ".rtf", ".md" };

        public static List<Item> Run(LibraryData data, Action<string> status)
        {
            List<Item> result = null;
            Exception error = null;
            // Le menu Démarrer passe par un objet COM du shell, qui veut un thread STA
            var t = new Thread(() =>
            {
                try { result = RunCore(data, status); }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            t.Join();
            if (error != null) throw error;
            return result;
        }

        static List<Item> RunCore(LibraryData data, Action<string> status)
        {
            var known = new Known(data);
            var found = new List<Item>();
            Action<Item, string> add = (it, src) =>
            {
                if (it == null || known.Contains(it)) return;
                it.Source = src;
                it.Selected = it.Category == Tiles.Games;
                known.Add(it);
                found.Add(it);
            };

            status("Steam et Epic Games…");
            foreach (var it in Safe(Steam.Scan)) add(it, "Steam");
            foreach (var it in Safe(Epic.Scan)) add(it, "Epic Games");
            status("GOG et Ubisoft Connect…");
            foreach (var it in Safe(Gog)) add(it, "GOG");
            foreach (var it in Safe(Ubisoft)) add(it, "Ubisoft Connect");
            status("Menu Démarrer et Microsoft Store…");
            var xbox = XboxIdentities();
            foreach (var it in Safe(() => StartMenu(xbox))) add(it, it.Source);
            status("Dossiers de jeux sur tous les disques…");
            foreach (var it in Safe(() => GameFolders(status))) add(it, "Dossier de jeux");
            return found;
        }

        static List<Item> Safe(Func<List<Item>> f)
        {
            try { return f(); } catch { return new List<Item>(); }
        }

        // ───────────── Launchers ─────────────

        static List<Item> Gog()
        {
            var list = new List<Item>();
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games"))
            {
                if (k == null) return list;
                foreach (var id in k.GetSubKeyNames())
                {
                    using (var g = k.OpenSubKey(id))
                    {
                        var name = g.GetValue("gameName") as string;
                        var exe = g.GetValue("exe") as string;
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(exe) || !File.Exists(exe)) continue;
                        var it = Importer.New(name, exe, Tiles.Games);
                        it.Key = "gog:" + id;
                        it.MatchPath = exe;
                        list.Add(it);
                    }
                }
            }
            return list;
        }

        static List<Item> Ubisoft()
        {
            var list = new List<Item>();
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs"))
            {
                if (k == null) return list;
                foreach (var id in k.GetSubKeyNames())
                {
                    using (var g = k.OpenSubKey(id))
                    {
                        var dir = (g.GetValue("InstallDir") as string ?? "").Replace('/', '\\').TrimEnd('\\');
                        if (dir == "" || !Directory.Exists(dir)) continue;
                        var it = Importer.New(Path.GetFileName(dir), "uplay://launch/" + id + "/0", Tiles.Games);
                        it.Key = "ubi:" + id;
                        var exe = MainExe(dir);
                        it.IconSource = exe;
                        it.MatchPath = exe;
                        list.Add(it);
                    }
                }
            }
            return list;
        }

        // ───────────── Menu Démarrer (shell:AppsFolder) ─────────────

        static List<string> XboxIdentities()
        {
            var ids = new List<string>();
            foreach (var d in FixedDrives())
            {
                var root = Path.Combine(d, "XboxGames");
                if (!Directory.Exists(root)) continue;
                foreach (var g in Directory.GetDirectories(root))
                {
                    try
                    {
                        var cfg = Path.Combine(g, "Content", "MicrosoftGame.config");
                        if (!File.Exists(cfg)) continue;
                        var m = Regex.Match(File.ReadAllText(cfg), "<Identity[^>]*\\sName=\"([^\"]+)\"");
                        if (m.Success) ids.Add(m.Groups[1].Value);
                    }
                    catch { }
                }
            }
            return ids;
        }

        static List<Item> StartMenu(List<string> xbox)
        {
            var list = new List<Item>();
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            object shell = Activator.CreateInstance(shellType);
            object folder = shellType.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { "shell:AppsFolder" });
            object items = folder.GetType().InvokeMember("Items", BindingFlags.InvokeMethod, null, folder, null);
            int count = (int)items.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, items, null);
            var sys = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();

            for (int i = 0; i < count; i++)
            {
                try
                {
                    object fi = items.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, items, new object[] { i });
                    var t = fi.GetType();
                    var name = ((string)t.InvokeMember("Name", BindingFlags.GetProperty, null, fi, null) ?? "").Trim();
                    var p = (string)t.InvokeMember("Path", BindingFlags.GetProperty, null, fi, null) ?? "";
                    var low = name.ToLowerInvariant();
                    if (name == "" || p == "" || JunkNames.Any(j => low.Contains(j))) continue;

                    if (p.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || p.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.Contains("://"))
                    {
                        if (!p.StartsWith("steam://", StringComparison.OrdinalIgnoreCase)) continue;
                        var steam = Importer.New(name, p, Classifier.Guess(name, p));
                        var appid = ShortcutInfo.SteamAppId(p);
                        if (appid != null) { steam.Key = "steam:" + appid; steam.PosterPath = Steam.FindPoster(appid); }
                        steam.IconSource = Steam.Root == null ? null : Path.Combine(Steam.Root, "steam.exe");
                        steam.Source = "Steam";
                        list.Add(steam);
                        continue;
                    }

                    var file = ResolveKnownFolder(p);
                    var it = Importer.New(name, "shell:AppsFolder\\" + p, Tiles.Apps);
                    it.Source = "Menu Démarrer";
                    if (file != null)
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        var fname = Path.GetFileName(file).ToLowerInvariant();
                        if (SkipExt.Contains(ext) || fname.StartsWith("unins")) continue;
                        if (file.ToLowerInvariant().StartsWith(sys + "\\")) continue;   // outils système de Windows
                        it.IconSource = file;
                        it.MatchPath = file;
                        it.Category = Classifier.Guess(name, file);
                    }
                    else
                    {
                        it.IconSource = it.Path;
                        if (p.StartsWith("Microsoft.Windows.") || p.StartsWith("MicrosoftWindows.") || p.StartsWith("windows.")) continue;
                        bool isXbox = xbox.Any(x => p.StartsWith(x + "_", StringComparison.OrdinalIgnoreCase));
                        it.Category = isXbox ? Tiles.Games : Classifier.Guess(name, p);
                        if (isXbox) it.Source = "Xbox";
                        else if (p.Contains("!")) it.Source = "Microsoft Store";
                    }
                    list.Add(it);
                }
                catch { }
            }
            return list;
        }

        // « {GUID}\dossier\appli.exe » → « C:\Program Files\dossier\appli.exe »
        static string ResolveKnownFolder(string p)
        {
            if (Regex.IsMatch(p, @"^[A-Za-z]:\\")) return p;
            var m = Regex.Match(p, @"^\{([0-9A-Fa-f\-]{36})\}\\(.+)$");
            if (!m.Success) return null;
            IntPtr ptr;
            if (SHGetKnownFolderPath(new Guid(m.Groups[1].Value), 0, IntPtr.Zero, out ptr) != 0) return null;
            try { return Path.Combine(Marshal.PtrToStringUni(ptr), m.Groups[2].Value); }
            finally { Marshal.FreeCoTaskMem(ptr); }
        }

        // ───────────── Dossiers de jeux sur les disques ─────────────

        static IEnumerable<string> FixedDrives()
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                bool ok;
                try { ok = d.DriveType == DriveType.Fixed && d.IsReady; } catch { ok = false; }
                if (ok) yield return d.RootDirectory.FullName;
            }
        }

        static readonly string[] GameRoots = { "Games", "Jeux", "Game", "Jeux vidéo", "GOG Games", "Riot Games", "EA Games",
            "Rockstar Games", "Ubisoft Games", "Amazon Games\\Library", "Battle.net Games" };
        static readonly string[] VendorRoots = { "Rockstar Games", "EA Games", "Electronic Arts", "Riot Games", "GOG Galaxy\\Games",
            "Ubisoft\\Ubisoft Game Launcher\\games", "Battle.net", "Blizzard Entertainment" };

        static List<Item> GameFolders(Action<string> status)
        {
            var list = new List<Item>();
            foreach (var drive in FixedDrives())
            {
                status("Dossiers de jeux sur " + drive.TrimEnd('\\') + "…");
                // Dossiers dédiés aux jeux : tout ce qui contient un .exe est un jeu
                foreach (var r in GameRoots)
                    foreach (var g in SubDirs(Path.Combine(drive, r)))
                        AddFolder(list, g, false);

                foreach (var pf in new[] { "Program Files", "Program Files (x86)" })
                {
                    var pfDir = Path.Combine(drive, pf);
                    foreach (var v in VendorRoots)
                        foreach (var g in SubDirs(Path.Combine(pfDir, v)))
                            AddFolder(list, g, false);
                    // Ailleurs dans Program Files : seulement si le moteur de jeu est reconnu
                    foreach (var vendor in SubDirs(pfDir))
                    {
                        AddFolder(list, vendor, true);
                        foreach (var g in SubDirs(vendor)) AddFolder(list, g, true);
                    }
                }
            }
            return list;
        }

        static IEnumerable<string> SubDirs(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : new string[0]; }
            catch { return new string[0]; }
        }

        static void AddFolder(List<Item> list, string dir, bool needEngine)
        {
            var low = dir.ToLowerInvariant();
            if (low.Contains("\\steamapps") || low.Contains("\\windowsapps") || low.Contains("\\common files")
                || low.Contains("\\microsoft") || low.Contains("\\windows ")) return;
            // Les launchers eux-mêmes (déjà trouvés via le menu Démarrer) et les dossiers techniques
            var leaf = Path.GetFileName(dir).ToLowerInvariant();
            if (new[] { "launcher", "redist", "directx", "support", "common", "tools", "_commonredist", "installer" }.Any(j => leaf.Contains(j))) return;
            var exe = MainExe(dir);
            if (exe == null || list.Any(i => string.Equals(i.MatchPath, exe, StringComparison.OrdinalIgnoreCase))) return;
            if (needEngine && !(Directory.Exists(Path.Combine(dir, "Engine")) || GameDetector.LooksLikeGame(exe))) return;
            var it = Importer.New(Path.GetFileName(dir), exe, Tiles.Games);
            it.MatchPath = exe;
            list.Add(it);
        }

        // L'exe principal d'un dossier de jeu : celui qui ressemble à un jeu, sinon le plus gros
        public static string MainExe(string dir)
        {
            var places = new[] { dir, Path.Combine(dir, "Binaries", "Win64"), Path.Combine(dir, "bin"), Path.Combine(dir, "bin", "x64"),
                Path.Combine(dir, "bin64"), Path.Combine(dir, "x64"), Path.Combine(dir, "Content") };
            var exes = new List<string>();
            foreach (var p in places)
            {
                try { if (Directory.Exists(p)) exes.AddRange(Directory.GetFiles(p, "*.exe")); } catch { }
            }
            exes = exes.Where(e =>
            {
                var n = Path.GetFileNameWithoutExtension(e).ToLowerInvariant();
                return !JunkExe.Any(j => n.Contains(j));
            }).ToList();
            if (exes.Count == 0) return null;
            var game = exes.FirstOrDefault(GameDetector.LooksLikeGame);
            if (game != null)
            {
                // Unreal : l'exe à la racine lance le « -Shipping » caché dans Binaries
                var top = exes.FirstOrDefault(e => Path.GetDirectoryName(e).Equals(dir, StringComparison.OrdinalIgnoreCase));
                return game.EndsWith("-Shipping.exe", StringComparison.OrdinalIgnoreCase) && top != null ? top : game;
            }
            return exes.OrderByDescending(e => { try { return new FileInfo(e).Length; } catch { return 0L; } }).First();
        }

        // ───────────── Doublons ─────────────

        public static string Norm(string s)
        {
            return new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        class Known
        {
            readonly HashSet<string> keys = new HashSet<string>(), names = new HashSet<string>();
            readonly HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public Known(LibraryData data)
            {
                foreach (var i in data.Items)
                {
                    Add(i);
                    paths.Add(i.Path ?? "");
                    if (ShortcutInfo.IsShortcut(i.Path ?? ""))
                    {
                        var exe = ShortcutInfo.ExeOf(ShortcutInfo.Target(i.Path));
                        if (exe != null) paths.Add(exe);
                    }
                }
            }

            public void Add(Item i)
            {
                if (i.Key != null) keys.Add(i.Key);
                names.Add(Norm(i.Name));
                if (i.MatchPath != null) paths.Add(i.MatchPath);
                paths.Add(i.Path ?? "");
            }

            public bool Contains(Item i)
            {
                return (i.Key != null && keys.Contains(i.Key)) || names.Contains(Norm(i.Name))
                    || (i.MatchPath != null && paths.Contains(i.MatchPath)) || paths.Contains(i.Path ?? "");
            }
        }
    }
}
