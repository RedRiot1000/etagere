using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Etagere
{
    // Un jeu ou une application rangé dans l'Étagère
    public class Item : INotifyPropertyChanged
    {
        string name, category;
        bool favorite;
        ImageSource icon, poster;

        public string Key { get; set; }            // "steam:123" / "epic:xxx" pour les jeux détectés
        public string Path { get; set; }           // fichier (.lnk/.url/.exe) ou lien (steam://...)
        public string IconSource { get; set; }
        public string PosterPath { get; set; }
        public int LaunchCount { get; set; }
        long lastLaunch;
        public long LastLaunch { get { return lastLaunch; } set { lastLaunch = value; NotifyPlay(); } }
        public long Added { get; set; }
        public bool FromDesktop { get; set; }      // raccourci déplacé depuis le bureau
        public string OriginalFolder { get; set; }
        public string GameDir { get; set; }        // dossier surveillé pour mesurer le temps de jeu

        long lastSession, totalPlayed, lastPlayed;
        public long LastSessionSeconds { get { return lastSession; } set { lastSession = value; NotifyPlay(); } }
        public long TotalSeconds { get { return totalPlayed; } set { totalPlayed = value; NotifyPlay(); } }
        public long LastPlayed { get { return lastPlayed; } set { lastPlayed = value; NotifyPlay(); } }   // fin de la dernière session

        void NotifyPlay() { Notify("PlayInfo"); Notify("PlayVisibility"); Notify("TooltipText"); }

        [ScriptIgnore] public string PlayInfo
        {
            get
            {
                if (lastSession > 0) return Fmt.Duration(lastSession) + " · " + Fmt.When(lastPlayed);
                return LastLaunch > 0 ? "Lancé " + Fmt.When(LastLaunch) : "";
            }
        }
        [ScriptIgnore] public Visibility PlayVisibility { get { return PlayInfo == "" ? Visibility.Collapsed : Visibility.Visible; } }
        [ScriptIgnore] public string TooltipText
        {
            get
            {
                if (lastSession <= 0) return name;
                return name + "\nDernière session : " + Fmt.Duration(lastSession) + ", " + Fmt.When(lastPlayed)
                    + "\nTemps total : " + Fmt.Duration(totalPlayed);
            }
        }

        public string Name { get { return name; } set { name = value; Notify("Name"); Notify("Initial"); Notify("TooltipText"); } }
        public string Category { get { return category; } set { category = value; Notify("Category"); Notify("TileBrush"); } }
        public bool Favorite { get { return favorite; } set { favorite = value; Notify("Favorite"); Notify("StarVisibility"); } }

        bool selected;
        [ScriptIgnore] public bool ArtLoaded { get; set; }
        [ScriptIgnore] public string Source { get; set; }      // d'où vient l'élément trouvé par le scan
        [ScriptIgnore] public string MatchPath { get; set; }   // exe réel, pour éviter les doublons au scan
        [ScriptIgnore] public bool Selected { get { return selected; } set { selected = value; Notify("Selected"); } }
        [ScriptIgnore] public ImageSource Icon { get { return icon; } set { icon = value; Notify("Icon"); Notify("InitialVisibility"); } }
        [ScriptIgnore] public ImageSource Poster { get { return poster; } set { poster = value; Notify("Poster"); Notify("IconVisibility"); } }
        [ScriptIgnore] public Visibility StarVisibility { get { return favorite ? Visibility.Visible : Visibility.Collapsed; } }
        [ScriptIgnore] public Visibility IconVisibility { get { return poster == null ? Visibility.Visible : Visibility.Collapsed; } }
        [ScriptIgnore] public Visibility InitialVisibility { get { return icon == null ? Visibility.Visible : Visibility.Collapsed; } }
        [ScriptIgnore] public string Initial { get { return string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant(); } }
        [ScriptIgnore] public Brush TileBrush { get { return Tiles.For(category); } }

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string p) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(p)); }
    }

    // Durées et dates lisibles : « 1 h 23 », « hier à 21:05 »
    public static class Fmt
    {
        public static string Duration(long seconds)
        {
            if (seconds < 60) return "moins d'une minute";
            long min = seconds / 60;
            if (min < 60) return min + " min";
            return (min / 60) + " h " + (min % 60).ToString("00");
        }

        public static string When(long ticks)
        {
            if (ticks <= 0) return "";
            var d = new DateTime(ticks);
            var days = (DateTime.Today - d.Date).Days;
            var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            if (days == 0) return "aujourd'hui à " + d.ToString("HH:mm");
            if (days == 1) return "hier à " + d.ToString("HH:mm");
            if (days < 7) return d.ToString("dddd", fr);
            return "le " + d.ToString(d.Year == DateTime.Today.Year ? "d MMM" : "d MMM yyyy", fr);
        }
    }

    public static class Tiles
    {
        public const string Games = "Jeux", Apps = "Applications", Papers = "Papiers importants", Other = "Autres";
        public static readonly string[] All = { Games, Apps, Papers, Other };
        static readonly Brush games = Make("#3A1F6E", "#171330"), apps = Make("#0F3B52", "#111A2C"),
            papers = Make("#5A1D3A", "#1E1420"), other = Make("#4A3214", "#1E1915");

        static Brush Make(string a, string b)
        {
            var g = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a), (Color)ColorConverter.ConvertFromString(b), new Point(0, 0), new Point(0.6, 1));
            g.Freeze();
            return g;
        }

        public static Brush For(string cat) { return cat == Games ? games : cat == Papers ? papers : cat == Other ? other : apps; }
    }

    public class LibraryData
    {
        public List<Item> Items { get; set; }
        public List<string> Ignored { get; set; }   // jeux détectés que l'utilisateur a retirés
        public bool MinimizeOnLaunch { get; set; }
        public double Zoom { get; set; }
        public string Sort { get; set; }

        public LibraryData()
        {
            Items = new List<Item>();
            Ignored = new List<string>();
            MinimizeOnLaunch = true;
            Zoom = 1;
            Sort = "name";
        }
    }

    public static class Store
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Etagere");
        public static readonly string ShortcutDir = Path.Combine(Dir, "Raccourcis");
        public static readonly string PapersDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Papiers importants");
        static readonly string FilePath = Path.Combine(Dir, "etagere.json");

        public static bool IsFirstRun { get { return !File.Exists(FilePath); } }

        static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }; }

        public static LibraryData Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = Serializer().Deserialize<LibraryData>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (d != null)
                    {
                        if (d.Items == null) d.Items = new List<Item>();
                        if (d.Ignored == null) d.Ignored = new List<string>();
                        if (d.Zoom < 0.6 || d.Zoom > 1.6) d.Zoom = 1;
                        d.Items.RemoveAll(i => i == null || string.IsNullOrEmpty(i.Path));
                        return d;
                    }
                }
            }
            catch { }
            return new LibraryData();
        }

        public static void Save(LibraryData d)
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, Serializer().Serialize(d), Encoding.UTF8);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }

        public static string UniqueIn(string dir, string fileName)
        {
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, fileName);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            for (int n = 2; File.Exists(dest); n++)
                dest = Path.Combine(dir, stem + " (" + n + ")" + ext);
            return dest;
        }
    }

    public static class ShortcutInfo
    {
        public static readonly string[] Extensions = { ".lnk", ".url", ".appref-ms" };

        public static bool IsShortcut(string path)
        {
            return Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        }

        // Cible d'un raccourci (.lnk : exe + arguments, .url : l'adresse)
        public static string Target(string path)
        {
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".url")
                {
                    foreach (var line in File.ReadAllLines(path))
                        if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) return line.Substring(4).Trim();
                }
                else if (ext == ".lnk")
                {
                    var t = Type.GetTypeFromProgID("WScript.Shell");
                    object sh = Activator.CreateInstance(t);
                    object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { path });
                    var lt = lnk.GetType();
                    var target = (string)lt.InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null);
                    var args = (string)lt.InvokeMember("Arguments", BindingFlags.GetProperty, null, lnk, null);
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(lnk);
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sh);
                    return (target + " " + args).Trim();
                }
            }
            catch { }
            return "";
        }

        public static readonly string[] DocExtensions =
        {
            ".pdf", ".doc", ".docx", ".odt", ".rtf", ".txt", ".xls", ".xlsx", ".ods", ".csv", ".ppt", ".pptx", ".odp",
            ".jpg", ".jpeg", ".png", ".heic", ".webp", ".bmp", ".gif", ".tif", ".tiff"
        };
        static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff" };

        public static bool IsDocument(string path) { return DocExtensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant()); }
        public static bool IsImage(string path) { return ImageExtensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant()); }

        // Chemin de l'exécutable au début d'une cible « exe + arguments »
        public static string ExeOf(string target)
        {
            var m = Regex.Match(target ?? "", @"^""?(.+?\.(exe|bat|cmd|com))""?(\s|$)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        public static string SteamAppId(string target)
        {
            var m = Regex.Match(target ?? "", @"steam://(?:rungameid|run|launch)/(\d+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }
    }

    // Devine si c'est un jeu, une appli, un document ou autre chose (lien web…)
    public static class Classifier
    {
        static readonly string[] GameHints =
        {
            "steam://", "com.epicgames.launcher", "uplay://", "ubisoft", "origin://", "origin2://", "battlenet://",
            "goggalaxy://", "\\steamapps\\", "\\steam\\", "steam.exe", "\\epic games\\", "epicgameslauncher",
            "\\riot games\\", "riot client", "\\gog games\\", "gog galaxy", "\\ea games\\", "\\ea desktop",
            "electronic arts", "\\rockstar games\\", "rockstar games launcher", "battle.net", "blizzard", "\\games\\",
            "xboxgames", "gamingapp", "minecraft", "roblox", "\\itch\\", "heroic", "playnite", "\\amazon games\\",
            "league of legends", "valorant", "fortnite", "curseforge", "\\gog.com\\", "\\jeux\\", "solitaire",
            "thunderstore", "r2modman", "beyond-all-reason"
        };

        // Logiciels connus, même s'ils traînent dans un dossier de jeux ou sur Steam
        static readonly string[] AppHints =
        {
            "wallpaper engine", "xboxgamingoverlay", "game bar", "obs studio", "obs64", "discord", "spotify", "chrome",
            "firefox", "brave", "opera", "msedge", "microsoft edge", "teamspeak", "nvidia", "amd software", "radeon",
            "afterburner", "msi center", "steelseries", "razer", "logitech", "corsair", "medal.tv", "overwolf.client",
            "filmora", "premiere", "photoshop", "gimp", "blender", "vlc", "audacity", "7-zip", "winrar", "visual studio",
            "python", "node.js", "openiv", "soundpad", "voicemod"
        };

        public static string Guess(string name, string target)
        {
            if (ShortcutInfo.IsDocument(target)) return Tiles.Papers;
            var t = (target ?? "").ToLowerInvariant();
            var s = t + " " + (name ?? "").ToLowerInvariant();
            if (AppHints.Any(h => s.Contains(h))) return Tiles.Apps;
            if (GameHints.Any(h => s.Contains(h))) return Tiles.Games;
            if (GameDetector.LooksLikeGame(ShortcutInfo.ExeOf(target))) return Tiles.Games;
            if (t.StartsWith("http://") || t.StartsWith("https://")) return Tiles.Other;
            return Tiles.Apps;
        }
    }

    // Reconnaît un jeu à ce qu'il y a à côté de son .exe : moteurs (Unity, Unreal, Godot…) et bibliothèques de jeux
    public static class GameDetector
    {
        static readonly string[] Markers =
        {
            "unityplayer.dll", "gameassembly.dll", "unitycrashhandler64.exe", "unitycrashhandler32.exe",
            "steam_api.dll", "steam_api64.dll", "eossdk-win64-shipping.dll", "eossdk-win32-shipping.dll",
            "galaxy.dll", "galaxy64.dll", "data.win", "fmod.dll", "fmod64.dll", "fmodstudio.dll", "fmodstudio64.dll",
            "bink2w64.dll", "binkw64.dll", "binkw32.dll", "physx3_x64.dll", "physx_64.dll", "physxcore.dll",
            "xinput1_3.dll", "d3dx9_43.dll", "openal32.dll", "sdl2.dll", "love.dll"
        };
        static readonly string[] Strong = { "unityplayer.dll", "gameassembly.dll", "steam_api.dll", "steam_api64.dll",
            "eossdk-win64-shipping.dll", "eossdk-win32-shipping.dll", "galaxy.dll", "galaxy64.dll", "data.win" };

        public static bool LooksLikeGame(string exe)
        {
            try
            {
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return false;
                var name = Path.GetFileNameWithoutExtension(exe);
                if (name.EndsWith("-Win64-Shipping", StringComparison.OrdinalIgnoreCase)) return true;
                var dir = Path.GetDirectoryName(exe);
                if (Directory.Exists(Path.Combine(dir, name + "_Data"))) return true;      // Unity
                if (File.Exists(Path.Combine(dir, name + ".pck"))) return true;            // Godot
                if (dir.IndexOf("\\Binaries\\Win", StringComparison.OrdinalIgnoreCase) >= 0)  // Unreal
                {
                    var up = Directory.GetParent(dir);
                    for (int i = 0; i < 4 && up != null; i++, up = up.Parent)
                        if (Directory.Exists(Path.Combine(up.FullName, "Engine"))) return true;
                }
                var files = Directory.GetFiles(dir).Select(f => Path.GetFileName(f).ToLowerInvariant()).ToList();
                return files.Any(f => Strong.Contains(f)) || files.Count(f => Markers.Contains(f)) >= 2;
            }
            catch { return false; }
        }
    }

    public static class Importer
    {
        public static Item FromShortcut(string storedPath, string name)
        {
            var target = ShortcutInfo.Target(storedPath);
            var it = New(name, storedPath, Classifier.Guess(name, target));
            var appid = ShortcutInfo.SteamAppId(target);
            if (appid != null)
            {
                it.Key = "steam:" + appid;
                it.PosterPath = Steam.FindPoster(appid);
            }
            return it;
        }

        public static Item FromFile(string path)
        {
            if (Directory.Exists(path)) return New(Path.GetFileName(path.TrimEnd('\\')), path, Tiles.Other);
            if (ShortcutInfo.IsDocument(path)) return FromDocument(path);
            var name = Path.GetFileNameWithoutExtension(path);
            if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var pn = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductName;
                    if (!string.IsNullOrWhiteSpace(pn) && pn.Trim().Length <= 40) name = pn.Trim();
                }
                catch { }
            }
            return New(name, path, Classifier.Guess(name, path));
        }

        public static Item FromDocument(string path)
        {
            var it = New(Path.GetFileNameWithoutExtension(path), path, Tiles.Papers);
            if (ShortcutInfo.IsImage(path)) it.PosterPath = path;   // l'image elle-même sert d'aperçu
            return it;
        }

        public static Item New(string name, string path, string category)
        {
            return new Item { Name = name, Path = path, Category = category, Added = DateTime.Now.Ticks };
        }
    }

    public static class Steam
    {
        static string root;
        static bool rootSearched;

        public static string Root
        {
            get
            {
                if (rootSearched) return root;
                rootSearched = true;
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    {
                        var p = k == null ? null : k.GetValue("SteamPath") as string;
                        if (!string.IsNullOrEmpty(p)) { p = p.Replace('/', '\\'); if (Directory.Exists(p)) return root = p; }
                    }
                }
                catch { }
                var def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
                return root = Directory.Exists(def) ? def : null;
            }
        }

        static readonly string[] Tools = { "steamworks common", "redistributable", "proton", "steam linux runtime", "steamvr" };

        static List<string> Libraries()
        {
            var r = Root;
            var libs = new List<string>();
            if (r == null) return libs;
            libs.Add(r);
            try
            {
                var vdf = Path.Combine(r, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch { }
            return libs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Dossier d'installation d'un jeu Steam (pour mesurer le temps de jeu)
        public static string InstallDir(string appid)
        {
            foreach (var lib in Libraries())
            {
                try
                {
                    var acf = Path.Combine(lib, "steamapps", "appmanifest_" + appid + ".acf");
                    if (!File.Exists(acf)) continue;
                    var dir = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
                    if (dir != "") return Path.Combine(lib, "steamapps", "common", dir);
                }
                catch { }
            }
            return null;
        }

        public static List<Item> Scan()
        {
            var found = new List<Item>();
            var r = Root;
            if (r == null) return found;
            foreach (var lib in Libraries())
            {
                var apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                string[] manifests;
                try { manifests = Directory.GetFiles(apps, "appmanifest_*.acf"); } catch { continue; }
                foreach (var f in manifests)
                {
                    try
                    {
                        var txt = File.ReadAllText(f);
                        var id = Regex.Match(txt, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                        var name = Regex.Match(txt, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                        if (id == "" || name == "" || id == "228980") continue;
                        var low = name.ToLowerInvariant();
                        if (Tools.Any(t => low.Contains(t))) continue;
                        if (found.Any(i => i.Key == "steam:" + id)) continue;
                        var url = "steam://rungameid/" + id;
                        var it = Importer.New(name, url, Classifier.Guess(name, url));
                        it.Key = "steam:" + id;
                        it.PosterPath = FindPoster(id);
                        it.IconSource = Path.Combine(r, "steam.exe");
                        found.Add(it);
                    }
                    catch { }
                }
            }
            return found;
        }

        // Jaquette verticale que Steam garde en cache (format ancien et nouveau)
        public static string FindPoster(string appid)
        {
            var r = Root;
            if (r == null) return null;
            try
            {
                var cache = Path.Combine(r, "appcache", "librarycache");
                var old = Path.Combine(cache, appid + "_library_600x900.jpg");
                if (File.Exists(old)) return old;
                var dir = Path.Combine(cache, appid);
                if (Directory.Exists(dir))
                {
                    var files = Directory.GetFiles(dir, "library_600x900*.jpg", SearchOption.AllDirectories);
                    if (files.Length > 0) return files.OrderBy(x => x.Length).First();
                    files = Directory.GetFiles(dir, "library_capsule*.jpg", SearchOption.AllDirectories);
                    if (files.Length > 0) return files[0];
                }
            }
            catch { }
            return null;
        }
    }

    public static class Epic
    {
        public static List<Item> Scan()
        {
            var found = new List<Item>();
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(dir)) return found;
            var ser = new JavaScriptSerializer();
            foreach (var f in Directory.GetFiles(dir, "*.item"))
            {
                try
                {
                    var m = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                    Func<string, string> get = k => m.ContainsKey(k) && m[k] != null ? m[k].ToString() : "";
                    if (get("bIsIncompleteInstall").Equals("True", StringComparison.OrdinalIgnoreCase)) continue;
                    var app = get("AppName");
                    if (app == "" || app.StartsWith("UE_") || get("LaunchExecutable") == "") continue;
                    var exe = Path.Combine(get("InstallLocation"), get("LaunchExecutable"));
                    if (!File.Exists(exe)) continue;
                    var url = "com.epicgames.launcher://apps/" + get("CatalogNamespace") + "%3A" + get("CatalogItemId") + "%3A" + app + "?action=launch&silent=true";
                    var it = Importer.New(get("DisplayName") == "" ? app : get("DisplayName"), url, Tiles.Games);
                    it.Key = "epic:" + app;
                    it.IconSource = exe;
                    found.Add(it);
                }
                catch { }
            }
            return found;
        }
    }

    public class TidyResult { public int Moved, Added, Locked; }

    // Rangement du bureau : les raccourcis partent dans %APPDATA%\Etagere\Raccourcis (réversible)
    public static class Desk
    {
        public static readonly string User = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        public static readonly string Common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        public static bool IsDesktopFolder(string dir)
        {
            dir = (dir ?? "").TrimEnd('\\');
            return dir.Equals(User.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                || (Common != "" && dir.Equals(Common.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        }

        // Raccourcis + documents (PDF, Word, images…) posés sur le bureau
        public static List<string> FindClutter()
        {
            var list = new List<string>();
            foreach (var d in new[] { User, Common })
            {
                if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) continue;
                try
                {
                    list.AddRange(Directory.GetFiles(d).Where(f =>
                        (File.GetAttributes(f) & (FileAttributes.Hidden | FileAttributes.System)) == 0
                        && (ShortcutInfo.IsShortcut(f) || ShortcutInfo.IsDocument(f))));
                }
                catch { }
            }
            return list;
        }

        public static TidyResult Tidy(LibraryData data, IEnumerable<string> files)
        {
            var r = new TidyResult();
            foreach (var f in files) TidyOne(data, f, r);
            return r;
        }

        public static void TidyOne(LibraryData data, string file, TidyResult r)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var dir = Path.GetDirectoryName(file);
            var existing = data.Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));

            // Déjà rangé (copie faite sans droits admin lors d'un passage précédent) : on enlève juste l'original
            if (existing != null && existing.FromDesktop && File.Exists(existing.Path)
                && string.Equals(existing.OriginalFolder, dir, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(file); r.Moved++; } catch { r.Locked++; }
                return;
            }

            bool isDoc = ShortcutInfo.IsDocument(file);
            // Les documents vont dans un dossier visible (Documents\Papiers importants), pas caché dans AppData
            var dest = Store.UniqueIn(isDoc ? Store.PapersDir : Store.ShortcutDir, Path.GetFileName(file));
            bool removed = true;
            try { File.Move(file, dest); }
            catch
            {
                try { File.Copy(file, dest); } catch { return; }
                removed = false;
                r.Locked++;
            }
            if (removed) r.Moved++;

            if (!isDoc && existing != null && !existing.FromDesktop && !File.Exists(existing.Path))
            {
                existing.Path = dest;
                existing.FromDesktop = true;
                existing.OriginalFolder = dir;
                return;
            }
            var it = isDoc ? Importer.FromDocument(dest) : Importer.FromShortcut(dest, name);
            it.FromDesktop = true;
            it.OriginalFolder = dir;
            data.Items.Add(it);
            r.Added++;
        }

        public static bool Restore(Item it)
        {
            if (!it.FromDesktop || !File.Exists(it.Path)) return false;
            var dir = string.IsNullOrEmpty(it.OriginalFolder) || !Directory.Exists(it.OriginalFolder) ? User : it.OriginalFolder;
            var fileName = Path.GetFileName(it.Path);
            string dest;
            try { dest = Store.UniqueIn(dir, fileName); File.Move(it.Path, dest); }
            catch
            {
                try { dest = Store.UniqueIn(User, fileName); File.Move(it.Path, dest); }
                catch { return false; }
            }
            it.Path = dest;
            it.FromDesktop = false;
            return true;
        }
    }
}
