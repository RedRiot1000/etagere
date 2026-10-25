using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Etagere
{
    public class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly FrameworkElement root;
        readonly LibraryData data;
        string view = "all";
        List<Item> shown = new List<Item>();
        Action<string> dlgOk;
        readonly DispatcherTimer toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        bool loading = true;

        ItemsControl Cards;
        TextBox SearchBox, DlgInput;
        TextBlock SearchHint, TitleText, SubtitleText, NoResult, DlgTitle, DlgText, ToastText;
        TextBlock CountAll, CountFav, CountGames, CountApps, CountOther;
        FrameworkElement Overlay, EmptyState, Toast, DlgInputBorder;
        Border SearchBorder;
        Button DlgCancel, DlgOk, BtnMax;
        Slider ZoomSlider;
        ScrollViewer Scroller;
        CheckBox ChkMinimize, ChkStartup;
        Grid Shell;

        T F<T>(string name) where T : class { return (T)root.FindName(name); }

        public MainWindow()
        {
            Title = "Étagère";
            Width = 1240; Height = 790; MinWidth = 900; MinHeight = 580;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(14, 15, 26));
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 40,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });

            root = (FrameworkElement)Program.LoadXaml("MainView.xaml");
            Content = root;

            Cards = F<ItemsControl>("Cards"); SearchBox = F<TextBox>("SearchBox"); DlgInput = F<TextBox>("DlgInput");
            SearchHint = F<TextBlock>("SearchHint"); TitleText = F<TextBlock>("TitleText"); SubtitleText = F<TextBlock>("SubtitleText");
            NoResult = F<TextBlock>("NoResult"); DlgTitle = F<TextBlock>("DlgTitle"); DlgText = F<TextBlock>("DlgText");
            ToastText = F<TextBlock>("ToastText");
            CountAll = F<TextBlock>("CountAll"); CountFav = F<TextBlock>("CountFav"); CountGames = F<TextBlock>("CountGames");
            CountApps = F<TextBlock>("CountApps"); CountOther = F<TextBlock>("CountOther");
            Overlay = F<FrameworkElement>("Overlay"); EmptyState = F<FrameworkElement>("EmptyState"); Toast = F<FrameworkElement>("Toast");
            DlgInputBorder = F<FrameworkElement>("DlgInputBorder"); SearchBorder = F<Border>("SearchBorder");
            DlgCancel = F<Button>("DlgCancel"); DlgOk = F<Button>("DlgOk"); BtnMax = F<Button>("BtnMax");
            ZoomSlider = F<Slider>("ZoomSlider"); Scroller = F<ScrollViewer>("Scroller");
            ChkMinimize = F<CheckBox>("ChkMinimize"); ChkStartup = F<CheckBox>("ChkStartup"); Shell = F<Grid>("Shell");

            bool firstRun = Store.IsFirstRun;
            data = Store.Load();
            Directory.CreateDirectory(Store.ShortcutDir);
            int newGames = ImportGames();
            if (newGames > 0 || firstRun) Save();

            Wire();

            F<TextBlock>("VersionText").ToolTip = "Version " + AppInfo.Version;
            ChkMinimize.IsChecked = data.MinimizeOnLaunch;
            ChkStartup.IsChecked = StartupEnabled();
            ZoomSlider.Value = data.Zoom;
            ApplyZoom();
            var sort = data.Sort == "recent" ? "SortRecent" : data.Sort == "popular" ? "SortPopular" : "SortName";
            F<RadioButton>(sort).IsChecked = true;
            loading = false;
            Refresh();

            Loaded += async (s, e) =>
            {
                Updater.CleanupOld();
                var updated = await Updater.CheckAndInstallAsync();
                if (updated != null) OnUpdated(updated);
            };
            Loaded += (s, e) =>
            {
                if (newGames > 0) ShowToast(Plural(newGames, "jeu trouvé", "jeux trouvés") + " sur ton PC");
                if (firstRun)
                {
                    var n = Desk.FindShortcuts().Count;
                    if (n > 0)
                        ShowDialog("Bienvenue dans l'Étagère !",
                            "J'ai trouvé " + Plural(n, "raccourci", "raccourcis") + " sur ton bureau. Je peux les ranger ici pour que ton bureau soit tout propre.\n\nTu pourras les remettre en un clic quand tu veux.",
                            "Ranger mon bureau", "Plus tard", null, _ => TidyDesktop(false));
                }
            };
        }

        void OnUpdated(string version)
        {
            if (Overlay.Visibility == Visibility.Visible)
            {
                ShowToast("Mise à jour v" + version + " installée : active au prochain lancement");
                return;
            }
            ShowDialog("Nouvelle version installée ✨",
                "Étagère v" + version + " vient d'être téléchargée. Redémarrer maintenant pour en profiter ?",
                "Redémarrer", "Plus tard", null, _ =>
                {
                    try { Process.Start(Updater.ExePath); Close(); } catch { }
                });
        }

        // ───────────────────────── Câblage ─────────────────────────

        void Wire()
        {
            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                int on = 1, round = 2;
                DwmSetWindowAttribute(h, 20, ref on, 4);     // barre sombre
                DwmSetWindowAttribute(h, 33, ref round, 4);  // coins arrondis (Windows 11)
            };
            StateChanged += (s, e) =>
            {
                root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
                BtnMax.Content = WindowState == WindowState.Maximized ? "" : "";
            };
            Closing += (s, e) => { try { Save(); } catch { } };

            F<Button>("BtnMin").Click += (s, e) => WindowState = WindowState.Minimized;
            BtnMax.Click += (s, e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            F<Button>("BtnClose").Click += (s, e) => Close();

            foreach (var n in new[] { "NavAll", "NavFav", "NavRecent", "NavGames", "NavApps", "NavOther" })
            {
                var rb = F<RadioButton>(n);
                rb.Checked += (s, e) => { view = (string)((RadioButton)s).Tag; Scroller.ScrollToTop(); Refresh(); };
            }
            foreach (var n in new[] { "SortName", "SortRecent", "SortPopular" })
            {
                F<RadioButton>(n).Checked += (s, e) =>
                {
                    data.Sort = (string)((RadioButton)s).Tag;
                    if (!loading) { Save(); Refresh(); }
                };
            }

            F<Button>("BtnTidy").Click += (s, e) => TidyDesktop(true);
            F<Button>("BtnEmptyTidy").Click += (s, e) => TidyDesktop(true);
            F<Button>("BtnFindGames").Click += (s, e) => FindGames();
            F<Button>("BtnEmptyGames").Click += (s, e) => FindGames();
            F<Button>("BtnAdd").Click += (s, e) => BrowseAdd();
            F<Button>("BtnRestore").Click += (s, e) => RestoreAll();

            ChkMinimize.Checked += (s, e) => { data.MinimizeOnLaunch = true; if (!loading) Save(); };
            ChkMinimize.Unchecked += (s, e) => { data.MinimizeOnLaunch = false; if (!loading) Save(); };
            ChkStartup.Checked += (s, e) => { if (!loading) SetStartup(true); };
            ChkStartup.Unchecked += (s, e) => { if (!loading) SetStartup(false); };

            ZoomSlider.ValueChanged += (s, e) => { data.Zoom = Math.Round(ZoomSlider.Value, 2); ApplyZoom(); };
            Scroller.PreviewMouseWheel += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                ZoomSlider.Value += e.Delta > 0 ? 0.05 : -0.05;
                e.Handled = true;
            };

            SearchBox.TextChanged += (s, e) =>
            {
                SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                Refresh();
            };
            SearchBox.GotKeyboardFocus += (s, e) => SearchBorder.BorderBrush = (Brush)FindResource("Accent");
            SearchBox.LostKeyboardFocus += (s, e) => SearchBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255));

            Cards.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var it = ItemOf(e.OriginalSource);
                if (it != null) Launch(it);
            }));
            Cards.PreviewMouseRightButtonUp += (s, e) =>
            {
                var it = ItemOf(e.OriginalSource);
                if (it == null) return;
                var menu = BuildMenu(it);
                menu.PlacementTarget = (UIElement)e.OriginalSource;
                menu.Placement = PlacementMode.MousePoint;
                menu.IsOpen = true;
                e.Handled = true;
            };

            DlgOk.Click += (s, e) => CloseDialog(true);
            DlgCancel.Click += (s, e) => CloseDialog(false);
            DlgInput.KeyDown += (s, e) => { if (e.Key == Key.Enter) CloseDialog(true); };

            PreviewKeyDown += OnKey;
            TextInput += (s, e) =>
            {
                if (Overlay.Visibility == Visibility.Visible || Keyboard.FocusedElement is TextBox) return;
                if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]) || e.Text == " ") return;
                SearchBox.Focus();
                SearchBox.Text += e.Text;
                SearchBox.CaretIndex = SearchBox.Text.Length;
                e.Handled = true;
            };

            AllowDrop = true;
            Drop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null) AddFiles(files);
            };

            toastTimer.Tick += (s, e) =>
            {
                toastTimer.Stop();
                Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
            };
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (Overlay.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape) { CloseDialog(false); e.Handled = true; }
                else if (e.Key == Key.Enter && !DlgInput.IsKeyboardFocused) { CloseDialog(true); e.Handled = true; }
                return;
            }
            if (e.Key == Key.Escape && SearchBox.Text.Length > 0) { SearchBox.Text = ""; Keyboard.ClearFocus(); e.Handled = true; }
            else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Enter && SearchBox.IsKeyboardFocused && shown.Count > 0) { Launch(shown[0]); e.Handled = true; }
        }

        static Item ItemOf(object source)
        {
            var fe = source as FrameworkElement;
            if (fe != null) return fe.DataContext as Item;
            var fce = source as FrameworkContentElement;
            return fce != null ? fce.DataContext as Item : null;
        }

        void ApplyZoom()
        {
            var st = (ScaleTransform)Cards.LayoutTransform;
            st.ScaleX = st.ScaleY = data.Zoom;
        }

        // ───────────────────────── Affichage ─────────────────────────

        void Refresh()
        {
            if (loading) return;
            IEnumerable<Item> q = data.Items;
            string title;
            switch (view)
            {
                case "fav": q = q.Where(i => i.Favorite); title = "Favoris"; break;
                case "recent": q = q.Where(i => i.LaunchCount > 0); title = "Récents"; break;
                case "all": title = "Tout"; break;
                default: q = q.Where(i => i.Category == view); title = view; break;
            }

            var search = SearchBox.Text.Trim();
            if (search.Length > 0)
            {
                var ci = CultureInfo.CurrentCulture.CompareInfo;
                q = q.Where(i => ci.IndexOf(i.Name ?? "", search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
            }

            var byName = StringComparer.Create(CultureInfo.CurrentCulture, true);
            if (view == "recent" || data.Sort == "recent")
                q = q.OrderByDescending(i => i.LastLaunch).ThenBy(i => i.Name, byName);
            else if (data.Sort == "popular")
                q = q.OrderByDescending(i => i.LaunchCount).ThenBy(i => i.Name, byName);
            else
                q = q.OrderBy(i => i.Name, byName);
            if (view == "recent") q = q.Take(40);

            shown = q.ToList();
            Cards.ItemsSource = shown;

            TitleText.Text = title;
            SubtitleText.Text = shown.Count == 0 ? "Rien ici pour l'instant"
                : view == "Jeux" ? Plural(shown.Count, "jeu", "jeux")
                : Plural(shown.Count, "élément", "éléments");
            CountAll.Text = data.Items.Count.ToString();
            CountFav.Text = data.Items.Count(i => i.Favorite).ToString();
            CountGames.Text = data.Items.Count(i => i.Category == Tiles.Games).ToString();
            CountApps.Text = data.Items.Count(i => i.Category == Tiles.Apps).ToString();
            CountOther.Text = data.Items.Count(i => i.Category == Tiles.Other).ToString();

            bool empty = data.Items.Count == 0;
            EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            foreach (var n in new[] { "TitleText", "SearchBorder" })
                ((FrameworkElement)F<FrameworkElement>(n)).Visibility = empty ? Visibility.Hidden : Visibility.Visible;
            SubtitleText.Visibility = empty ? Visibility.Hidden : Visibility.Visible;
            Scroller.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

            if (!empty && shown.Count == 0)
            {
                NoResult.Text = search.Length > 0 ? "Aucun résultat pour « " + search + " »"
                    : view == "fav" ? "Pas encore de favoris.\nFais un clic droit sur une tuile ▸ Ajouter aux favoris."
                    : view == "recent" ? "Lance un jeu ou une appli : il apparaîtra ici."
                    : "Rien dans cette catégorie.\nClic droit sur une tuile ▸ Ranger dans…";
                NoResult.Visibility = Visibility.Visible;
            }
            else NoResult.Visibility = Visibility.Collapsed;

            LoadArt(shown);
        }

        // Icônes et jaquettes chargées petit à petit, sans bloquer la fenêtre
        void LoadArt(IEnumerable<Item> items)
        {
            foreach (var it in items.Where(i => !i.ArtLoaded).ToList())
            {
                it.ArtLoaded = true;
                var item = it;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    var poster = ShellIcons.LoadImage(item.PosterPath, 300);
                    if (poster != null) { item.Poster = poster; return; }
                    item.Icon = ShellIcons.Load(item.IconSource ?? item.Path) ?? ShellIcons.Load(item.Path);
                }));
            }
        }

        void ReloadArt(Item it)
        {
            it.ArtLoaded = false;
            it.Poster = null;
            it.Icon = null;
        }

        // ───────────────────────── Actions ─────────────────────────

        static bool IsUri(string p) { return Regex.IsMatch(p ?? "", @"^[a-zA-Z][a-zA-Z0-9+.\-]+://"); }

        void Launch(Item it)
        {
            var p = it.Path;
            try
            {
                if (IsUri(p))
                    Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
                else if (File.Exists(p) || Directory.Exists(p))
                {
                    var psi = new ProcessStartInfo(p) { UseShellExecute = true };
                    if (Path.GetExtension(p).Equals(".exe", StringComparison.OrdinalIgnoreCase)) psi.WorkingDirectory = Path.GetDirectoryName(p);
                    Process.Start(psi);
                }
                else
                {
                    ShowDialog("Introuvable",
                        "« " + it.Name + " » n'existe plus à cet endroit. Il a peut-être été désinstallé ou déplacé.\n\nLe retirer de l'Étagère ?",
                        "Retirer", "Garder", null, _ => RemoveItem(it));
                    return;
                }
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223) // 1223 = l'utilisateur a refusé la demande d'administrateur
                    ShowDialog("Impossible de lancer « " + it.Name + " »", ex.Message, "OK", null, null, null);
                return;
            }

            it.LaunchCount++;
            it.LastLaunch = DateTime.Now.Ticks;
            Save();
            ShowToast("Lancement de " + it.Name + "…");
            if (data.MinimizeOnLaunch)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                t.Tick += (s, e) => { t.Stop(); WindowState = WindowState.Minimized; };
                t.Start();
            }
            if (view == "recent" || data.Sort != "name") Refresh();
        }

        ContextMenu BuildMenu(Item it)
        {
            var m = new ContextMenu();
            Func<string, string, Action, MenuItem> add = (glyph, header, act) =>
            {
                var mi = new MenuItem
                {
                    Header = header,
                    Icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Glyph"), FontSize = 14, Foreground = (Brush)FindResource("SubText") }
                };
                mi.Click += (s, e) => act();
                m.Items.Add(mi);
                return mi;
            };
            Action sep = () => m.Items.Add(new Separator { Style = (Style)FindResource("MenuSep") });

            add("", it.Category == Tiles.Games ? "Jouer" : "Ouvrir", () => Launch(it)).FontWeight = FontWeights.SemiBold;
            add(it.Favorite ? "" : "", it.Favorite ? "Retirer des favoris" : "Ajouter aux favoris", () =>
            {
                it.Favorite = !it.Favorite;
                Save(); Refresh();
                ShowToast(it.Favorite ? it.Name + " ajouté aux favoris" : it.Name + " retiré des favoris");
            });
            sep();
            foreach (var cat in new[] { Tiles.Games, Tiles.Apps, Tiles.Other })
            {
                var c = cat;
                var glyph = c == it.Category ? "" : c == Tiles.Games ? "" : c == Tiles.Apps ? "" : "";
                var mi = add(glyph, "Ranger dans « " + c + " »", () =>
                {
                    it.Category = c;
                    Save(); Refresh();
                    ShowToast(it.Name + " rangé dans " + c);
                });
                if (c == it.Category) mi.IsEnabled = false;
            }
            sep();
            add("", "Renommer", () => ShowDialog("Renommer", null, "Renommer", "Annuler", it.Name, s =>
            {
                if (string.IsNullOrWhiteSpace(s)) return;
                it.Name = s.Trim();
                Save(); Refresh();
            }));
            if (!IsUri(it.Path)) add("", "Ouvrir l'emplacement du fichier", () => RevealInExplorer(it));
            if (it.FromDesktop) add("", "Remettre sur le bureau", () =>
            {
                if (Desk.Restore(it)) { Save(); ShowToast(it.Name + " est de retour sur le bureau"); }
            });
            add("", "Retirer de l'Étagère", () => ShowDialog("Retirer « " + it.Name + " » ?",
                it.FromDesktop ? "Le raccourci part à la corbeille. Le programme lui-même n'est pas désinstallé."
                               : "Il ne sera plus affiché ici. Le programme lui-même n'est pas désinstallé.",
                "Retirer", "Annuler", null, _ => RemoveItem(it)));
            return m;
        }

        void RevealInExplorer(Item it)
        {
            var target = it.Path;
            if (ShortcutInfo.IsShortcut(target))
            {
                var t = ShortcutInfo.Target(target);
                var exe = Regex.Match(t, @"^(.+?\.(exe|bat|cmd|com))", RegexOptions.IgnoreCase);
                if (exe.Success && File.Exists(exe.Groups[1].Value)) target = exe.Groups[1].Value;
            }
            try { Process.Start("explorer.exe", "/select,\"" + target + "\""); } catch { }
        }

        void RemoveItem(Item it)
        {
            if (it.FromDesktop && File.Exists(it.Path) && it.Path.StartsWith(Store.ShortcutDir, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(it.Path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                catch { }
            }
            if (it.Key != null && !data.Ignored.Contains(it.Key)) data.Ignored.Add(it.Key);
            data.Items.Remove(it);
            Save(); Refresh();
            ShowToast(it.Name + " retiré");
        }

        void TidyDesktop(bool confirm)
        {
            var files = Desk.FindShortcuts();
            if (files.Count == 0) { ShowToast("Ton bureau est déjà tout propre ✨"); return; }
            if (confirm)
            {
                ShowDialog("Ranger le bureau ?",
                    "J'ai trouvé " + Plural(files.Count, "raccourci", "raccourcis") + " sur ton bureau. Je les range dans l'Étagère.\n\nRien n'est supprimé : tu pourras tout remettre en un clic.",
                    "Ranger", "Annuler", null, _ => TidyDesktop(false));
                return;
            }

            var r = Desk.Tidy(data, files);
            Save();
            NavTo("all");
            Refresh();
            ShowToast(Plural(r.Moved, "raccourci rangé", "raccourcis rangés") + " ✨");

            if (r.Locked > 0)
                ShowDialog("Presque fini !",
                    Plural(r.Locked, "raccourci est partagé", "raccourcis sont partagés") + " entre tous les comptes du PC. Windows demande les droits administrateur pour les enlever du bureau (ils sont déjà dans l'Étagère).\n\nRelancer l'Étagère en administrateur pour finir le rangement ?",
                    "Relancer en admin", "Laisser comme ça", null, _ => RestartAsAdmin());
        }

        void RestartAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location) { UseShellExecute = true, Verb = "runas" });
                Close();
            }
            catch { }
        }

        void RestoreAll()
        {
            var items = data.Items.Where(i => i.FromDesktop).ToList();
            if (items.Count == 0) { ShowToast("Aucun raccourci à remettre sur le bureau"); return; }
            ShowDialog("Tout remettre sur le bureau ?",
                Plural(items.Count, "raccourci retournera", "raccourcis retourneront") + " sur ton bureau. Ils resteront aussi visibles dans l'Étagère.",
                "Remettre", "Annuler", null, _ =>
                {
                    int n = items.Count(Desk.Restore);
                    Save(); Refresh();
                    ShowToast(Plural(n, "raccourci remis", "raccourcis remis") + " sur le bureau");
                });
        }

        int ImportGames()
        {
            int added = 0;
            List<Item> found;
            try { found = Steam.Scan().Concat(Epic.Scan()).ToList(); } catch { return 0; }
            foreach (var g in found)
            {
                if (data.Ignored.Contains(g.Key) || data.Items.Any(i => i.Key == g.Key)) continue;
                var same = data.Items.FirstOrDefault(i => string.Equals(i.Name, g.Name, StringComparison.OrdinalIgnoreCase));
                if (same != null)
                {
                    same.Key = g.Key;
                    if (same.PosterPath == null) same.PosterPath = g.PosterPath;
                    same.Category = Tiles.Games;
                    continue;
                }
                data.Items.Add(g);
                added++;
            }
            // Steam télécharge parfois les jaquettes après coup
            foreach (var it in data.Items.Where(i => i.Key != null && i.Key.StartsWith("steam:")))
            {
                if (it.PosterPath != null && File.Exists(it.PosterPath)) continue;
                it.PosterPath = Steam.FindPoster(it.Key.Substring(6));
                if (it.PosterPath != null) ReloadArt(it);
            }
            return added;
        }

        void FindGames()
        {
            int n = ImportGames();
            Save();
            if (n > 0) NavTo("Jeux");
            Refresh();
            ShowToast(n > 0 ? Plural(n, "nouveau jeu ajouté", "nouveaux jeux ajoutés") : "Aucun nouveau jeu trouvé (Steam, Epic Games)");
        }

        void BrowseAdd()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Ajouter des programmes à l'Étagère",
                Filter = "Programmes et raccourcis|*.exe;*.lnk;*.url;*.appref-ms;*.bat|Tous les fichiers|*.*",
                Multiselect = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };
            if (dlg.ShowDialog(this) == true) AddFiles(dlg.FileNames);
        }

        void AddFiles(IEnumerable<string> paths)
        {
            int added = 0, moved = 0;
            var tidy = new TidyResult();
            foreach (var p in paths)
            {
                try
                {
                    if (ShortcutInfo.IsShortcut(p) && Desk.IsDesktopFolder(Path.GetDirectoryName(p)))
                    {
                        int before = tidy.Moved;
                        Desk.TidyOne(data, p, tidy);
                        moved += tidy.Moved - before;
                        continue;
                    }
                    if (data.Items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
                    Item it;
                    if (ShortcutInfo.IsShortcut(p))
                    {
                        var dest = Store.UniqueIn(Store.ShortcutDir, Path.GetFileName(p));
                        File.Copy(p, dest);
                        it = Importer.FromShortcut(dest, Path.GetFileNameWithoutExtension(p));
                    }
                    else if (File.Exists(p) || Directory.Exists(p)) it = Importer.FromFile(p);
                    else continue;
                    data.Items.Add(it);
                    added++;
                }
                catch { }
            }
            added += tidy.Added;
            Save(); Refresh();
            if (added + moved == 0) ShowToast("Déjà dans l'Étagère");
            else ShowToast(Plural(Math.Max(added, moved), "élément ajouté", "éléments ajoutés"));
        }

        void NavTo(string tag)
        {
            var map = new Dictionary<string, string> { { "all", "NavAll" }, { "Jeux", "NavGames" } };
            F<RadioButton>(map[tag]).IsChecked = true;
        }

        // ───────────────────────── Démarrage avec Windows ─────────────────────────

        static bool StartupEnabled()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("Etagere") != null; }
            catch { return false; }
        }

        void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("Etagere", "\"" + Assembly.GetExecutingAssembly().Location + "\" --minimized");
                    else k.DeleteValue("Etagere", false);
                }
                ShowToast(on ? "L'Étagère démarrera avec Windows" : "L'Étagère ne démarrera plus avec Windows");
            }
            catch { }
        }

        // ───────────────────────── Dialogue et notification ─────────────────────────

        void ShowDialog(string title, string text, string ok, string cancel, string input, Action<string> onOk)
        {
            DlgTitle.Text = title;
            DlgText.Text = text ?? "";
            DlgText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            DlgOk.Content = ok;
            DlgCancel.Content = cancel ?? "";
            DlgCancel.Visibility = cancel == null ? Visibility.Collapsed : Visibility.Visible;
            DlgInputBorder.Visibility = input == null ? Visibility.Collapsed : Visibility.Visible;
            dlgOk = onOk;
            Overlay.Visibility = Visibility.Visible;
            Overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            if (input != null)
            {
                DlgInput.Text = input;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { DlgInput.Focus(); DlgInput.SelectAll(); }));
            }
            else Keyboard.ClearFocus();
        }

        void CloseDialog(bool ok)
        {
            if (Overlay.Visibility != Visibility.Visible) return;
            Overlay.Visibility = Visibility.Collapsed;
            var act = dlgOk;
            dlgOk = null;
            if (ok && act != null) act(DlgInput.Text);
        }

        void ShowToast(string msg)
        {
            ToastText.Text = msg;
            Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            toastTimer.Stop();
            toastTimer.Start();
        }

        void Save() { try { Store.Save(data); } catch { } }

        static string Plural(int n, string one, string many) { return n + " " + (n > 1 ? many : one); }
    }
}
