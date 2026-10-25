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
        TextBlock CountAll, CountFav, CountGames, CountApps, CountPapers, CountOther;
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
            Width = 1240; Height = 790; MinWidth = 900; MinHeight = 640;
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
            CountApps = F<TextBlock>("CountApps"); CountOther = F<TextBlock>("CountOther"); CountPapers = F<TextBlock>("CountPapers");
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
                    var files = Desk.FindClutter();
                    if (files.Count > 0)
                        ShowDialog("Bienvenue dans l'Étagère !",
                            "J'ai trouvé " + DescribeClutter(files) + " sur ton bureau. Je peux les ranger ici pour que ton bureau soit tout propre.\n\nTu pourras tout remettre en un clic quand tu veux.",
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

            foreach (var n in new[] { "NavAll", "NavFav", "NavRecent", "NavGames", "NavApps", "NavPapers", "NavOther" })
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
            F<Button>("BtnFindGames").Click += (s, e) => StartScan();
            F<Button>("BtnEmptyGames").Click += (s, e) => StartScan();
            WireScan();
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
                if (Overlay.Visibility == Visibility.Visible || ScanPanel.Visibility == Visibility.Visible || Keyboard.FocusedElement is TextBox) return;
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
            if (Overlay.Visibility != Visibility.Visible && ScanPanel.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape && !scanning) { CloseScan(); e.Handled = true; }
                return;
            }
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
                : view == Tiles.Papers ? Plural(shown.Count, "document", "documents")
                : Plural(shown.Count, "élément", "éléments");
            CountAll.Text = data.Items.Count.ToString();
            CountFav.Text = data.Items.Count(i => i.Favorite).ToString();
            CountGames.Text = data.Items.Count(i => i.Category == Tiles.Games).ToString();
            CountApps.Text = data.Items.Count(i => i.Category == Tiles.Apps).ToString();
            CountOther.Text = data.Items.Count(i => i.Category == Tiles.Other).ToString();
            CountPapers.Text = data.Items.Count(i => i.Category == Tiles.Papers).ToString();

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
                    : view == Tiles.Papers ? "Aucun papier important pour l'instant.\nRange ton bureau, ou glisse ici tes PDF, scans et factures."
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
                if (p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    Process.Start("explorer.exe", "\"" + p + "\"");
                else if (IsUri(p))
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
            if (it.Category == Tiles.Games) PlayTracker.Start(it, Dispatcher, OnSessionEnd);
        }

        // Le jeu vient d'être quitté : on revient au premier plan avec le résumé de la partie
        void OnSessionEnd(Item it, long seconds)
        {
            it.LastSessionSeconds = seconds;
            it.TotalSeconds += seconds;
            it.LastPlayed = DateTime.Now.Ticks;
            Save();
            Refresh();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            if (Overlay.Visibility == Visibility.Visible || ScanPanel.Visibility == Visibility.Visible)
            {
                ShowToast(it.Name + " : " + Fmt.Duration(seconds) + " de jeu");
                return;
            }
            ShowDialog("Partie terminée 🎮",
                "Tu as joué à " + it.Name + " pendant " + Fmt.Duration(seconds) + ".\n\nTemps total sur ce jeu : " + Fmt.Duration(it.TotalSeconds) + ".",
                "Cool !", null, null, null);
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
            foreach (var cat in Tiles.All)
            {
                var c = cat;
                var glyph = c == it.Category ? "" : c == Tiles.Games ? "" : c == Tiles.Apps ? "" : c == Tiles.Papers ? "" : "";
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
            if (!IsUri(it.Path) && !it.Path.StartsWith("shell:")) add("", "Ouvrir l'emplacement du fichier", () => RevealInExplorer(it));
            if (it.FromDesktop) add("", "Remettre sur le bureau", () =>
            {
                if (Desk.Restore(it)) { Save(); ShowToast(it.Name + " est de retour sur le bureau"); }
            });
            add("", "Retirer de l'Étagère", () => ShowDialog("Retirer « " + it.Name + " » ?",
                ShortcutInfo.IsDocument(it.Path) ? "Le fichier n'est pas supprimé : il reste là où il est" + (it.Path.StartsWith(Store.PapersDir, StringComparison.OrdinalIgnoreCase) ? " (Documents\\Papiers importants)." : ".")
                : it.FromDesktop ? "Le raccourci part à la corbeille. Le programme lui-même n'est pas désinstallé."
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
            var files = Desk.FindClutter();
            if (files.Count == 0) { ShowToast("Ton bureau est déjà tout propre ✨"); return; }
            if (confirm)
            {
                bool docs = files.Any(ShortcutInfo.IsDocument);
                ShowDialog("Ranger le bureau ?",
                    "J'ai trouvé " + DescribeClutter(files) + " sur ton bureau. Je range tout dans l'Étagère"
                    + (docs ? " : les documents vont dans le dossier « Documents\\Papiers importants »." : ".")
                    + "\n\nRien n'est supprimé : tu pourras tout remettre en un clic.",
                    "Ranger", "Annuler", null, _ => TidyDesktop(false));
                return;
            }

            var r = Desk.Tidy(data, files);
            Save();
            NavTo("all");
            Refresh();
            ShowToast(Plural(r.Moved, "élément rangé", "éléments rangés") + " ✨");

            if (r.Locked > 0)
                ShowDialog("Presque fini !",
                    Plural(r.Locked, "élément est partagé", "éléments sont partagés") + " entre tous les comptes du PC. Windows demande les droits administrateur pour les enlever du bureau (ils sont déjà dans l'Étagère).\n\nRelancer l'Étagère en administrateur pour finir le rangement ?",
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
            if (items.Count == 0) { ShowToast("Rien à remettre sur le bureau"); return; }
            ShowDialog("Tout remettre sur le bureau ?",
                Plural(items.Count, "élément retournera", "éléments retourneront") + " sur ton bureau. Ils resteront aussi visibles dans l'Étagère.",
                "Remettre", "Annuler", null, _ =>
                {
                    int n = items.Count(Desk.Restore);
                    Save(); Refresh();
                    ShowToast(Plural(n, "élément remis", "éléments remis") + " sur le bureau");
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
                    same.Category = g.Category;
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

        // ───────────────────────── Scan de tout le PC ─────────────────────────

        List<Item> scanResults = new List<Item>();
        bool scanning;
        FrameworkElement ScanPanel, ScanBusy, ScanEmpty, ScanTools;
        ItemsControl ScanList;
        TextBlock ScanTitle, ScanStatus, ScanSpinner;
        RadioButton ScanTabGames, ScanTabApps;
        Button BtnScanAdd;

        void WireScan()
        {
            ScanPanel = F<FrameworkElement>("ScanPanel"); ScanBusy = F<FrameworkElement>("ScanBusy"); ScanEmpty = F<FrameworkElement>("ScanEmpty");
            ScanTools = F<FrameworkElement>("ScanTools"); ScanList = F<ItemsControl>("ScanList");
            ScanTitle = F<TextBlock>("ScanTitle"); ScanStatus = F<TextBlock>("ScanStatus"); ScanSpinner = F<TextBlock>("ScanSpinner");
            ScanTabGames = F<RadioButton>("ScanTabGames"); ScanTabApps = F<RadioButton>("ScanTabApps"); BtnScanAdd = F<Button>("BtnScanAdd");

            ScanTabGames.Checked += (s, e) => ShowScanList();
            ScanTabApps.Checked += (s, e) => ShowScanList();
            F<Button>("BtnScanAll").Click += (s, e) => { foreach (Item i in ScanList.ItemsSource ?? new List<Item>()) i.Selected = true; };
            F<Button>("BtnScanNone").Click += (s, e) => { foreach (Item i in ScanList.ItemsSource ?? new List<Item>()) i.Selected = false; };
            F<Button>("BtnScanCancel").Click += (s, e) => CloseScan();
            BtnScanAdd.Click += (s, e) => AddScanned();
            // L'étiquette de catégorie : jeu ⇄ appli
            ScanList.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var b = e.OriginalSource as Button;
                if (b == null || (b.Tag as string) != "cat") return;
                var it = b.DataContext as Item;
                if (it == null) return;
                it.Category = it.Category == Tiles.Games ? Tiles.Apps : Tiles.Games;
                it.Selected = true;
                ShowScanList();
                e.Handled = true;
            }));
        }

        public void StartScan()
        {
            if (scanning) return;
            scanning = true;
            ScanPanel.Visibility = Visibility.Visible;
            ScanPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            ScanTitle.Text = "Recherche sur tout ton PC…";
            ScanStatus.Text = "Je prépare la recherche…";
            ScanBusy.Visibility = Visibility.Visible;
            ScanEmpty.Visibility = Visibility.Collapsed;
            ScanTools.Visibility = Visibility.Hidden;
            ScanList.ItemsSource = null;
            BtnScanAdd.IsEnabled = false;
            BtnScanAdd.Content = "Ajouter";
            ScanSpinner.RenderTransform.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });

            Action<string> status = msg => Dispatcher.BeginInvoke(new Action(() => ScanStatus.Text = msg));
            System.Threading.Tasks.Task.Run(() => Scanner.Run(data, status)).ContinueWith(t =>
            {
                scanning = false;
                ScanSpinner.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, null);
                ScanBusy.Visibility = Visibility.Collapsed;
                scanResults = t.IsFaulted ? new List<Item>() : t.Result;
                foreach (var it in scanResults)
                    it.PropertyChanged += (s, e) => { if (e.PropertyName == "Selected") UpdateScanButton(); };
                int games = scanResults.Count(i => i.Category == Tiles.Games), apps = scanResults.Count - games;
                if (scanResults.Count == 0)
                {
                    ScanTitle.Text = "Rien de nouveau : tout est déjà dans ton Étagère ✨";
                    ScanEmpty.Visibility = Visibility.Visible;
                }
                else
                {
                    ScanTitle.Text = "J'ai trouvé " + Plural(games, "jeu", "jeux") + " et " + Plural(apps, "application", "applications");
                    ScanTools.Visibility = Visibility.Visible;
                }
                if (games == 0 && apps > 0) ScanTabApps.IsChecked = true;
                else { ScanTabGames.IsChecked = true; ShowScanList(); }
                LoadArt(scanResults);
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ShowScanList()
        {
            if (ScanList == null || scanning) return;
            bool games = ScanTabGames.IsChecked == true;
            var list = scanResults.Where(i => (i.Category == Tiles.Games) == games).OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            ScanList.ItemsSource = list;
            LoadArt(list);   // icônes de la liste affichée en premier
            ScanTabGames.Content = "Jeux (" + scanResults.Count(i => i.Category == Tiles.Games) + ")";
            ScanTabApps.Content = "Applications (" + scanResults.Count(i => i.Category != Tiles.Games) + ")";
            ScanEmpty.Visibility = list.Count == 0 && scanResults.Count > 0 ? Visibility.Visible : ScanEmpty.Visibility;
            if (list.Count > 0) ScanEmpty.Visibility = Visibility.Collapsed;
            F<ScrollViewer>("ScanScroller").ScrollToTop();
            UpdateScanButton();
        }

        void UpdateScanButton()
        {
            int n = scanResults.Count(i => i.Selected);
            BtnScanAdd.IsEnabled = n > 0;
            BtnScanAdd.Content = n > 0 ? "Ajouter (" + n + ")" : "Ajouter";
        }

        void AddScanned()
        {
            var chosen = scanResults.Where(i => i.Selected).ToList();
            foreach (var it in chosen)
            {
                it.Added = DateTime.Now.Ticks;
                if (it.Key != null) data.Ignored.Remove(it.Key);
                data.Items.Add(it);
            }
            Save();
            CloseScan();
            NavTo(chosen.All(i => i.Category == Tiles.Games) ? "Jeux" : "all");
            Refresh();
            ShowToast(Plural(chosen.Count, "élément ajouté", "éléments ajoutés") + " ✨");
        }

        void CloseScan()
        {
            if (scanning) return;
            ScanPanel.Visibility = Visibility.Collapsed;
            ScanList.ItemsSource = null;
        }

        static string DescribeClutter(List<string> files)
        {
            int docs = files.Count(ShortcutInfo.IsDocument), links = files.Count - docs;
            if (docs == 0) return Plural(links, "raccourci", "raccourcis");
            if (links == 0) return Plural(docs, "document", "documents");
            return Plural(links, "raccourci", "raccourcis") + " et " + Plural(docs, "document", "documents");
        }

        void BrowseAdd()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Ajouter des programmes à l'Étagère",
                Filter = "Programmes, raccourcis et documents|*.exe;*.lnk;*.url;*.appref-ms;*.bat;" + string.Join(";", ShortcutInfo.DocExtensions.Select(x => "*" + x))
                    + "|Programmes et raccourcis|*.exe;*.lnk;*.url;*.appref-ms;*.bat|Tous les fichiers|*.*",
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
                    if ((ShortcutInfo.IsShortcut(p) || ShortcutInfo.IsDocument(p)) && Desk.IsDesktopFolder(Path.GetDirectoryName(p)))
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
