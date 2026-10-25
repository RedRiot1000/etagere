using System;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;

namespace Etagere
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += (s, e) =>
            {
                MessageBox.Show("Oups, une erreur inattendue :\n\n" + e.Exception.Message, "Étagère",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Handled = true;
            };
            app.Resources.MergedDictionaries.Add((ResourceDictionary)LoadXaml("Theme.xaml"));

            bool minimized = Array.IndexOf(args, "--minimized") >= 0;
            var win = new MainWindow();
            if (minimized) win.WindowState = WindowState.Minimized;

            // --snap fichier.png : capture de la fenêtre puis fermeture (pour vérifier le rendu)
            int snap = Array.IndexOf(args, "--snap");
            if (snap >= 0 && snap + 1 < args.Length)
            {
                var path = args[snap + 1];
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(Array.IndexOf(args, "--scan") >= 0 ? 7 : 3) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    var el = (FrameworkElement)win.Content;
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)el.ActualWidth, (int)el.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(el);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    using (var fs = System.IO.File.Create(path)) enc.Save(fs);
                    win.Close();
                };
                win.ContentRendered += (s, e) => { if (Array.IndexOf(args, "--scan") >= 0) win.StartScan(); if (Array.IndexOf(args, "--random") >= 0) win.PickRandom(); timer.Start(); };
            }
            app.Run(win);
        }

        public static object LoadXaml(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Etagere." + name))
                return XamlReader.Load(s);
        }
    }
}
