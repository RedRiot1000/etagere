using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Etagere
{
    // Thèmes de couleur : on remplace les ressources « DynamicResource » de Theme.xaml au niveau de l'application
    public class ThemeDef
    {
        public string Name;
        public Color A, B, C, Glow, Blob2, Blob3, Light;
    }

    public static class Themes
    {
        static Color H(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }

        static ThemeDef T(string name, string a, string b, string c, string glow, string blob2, string blob3, string light)
        {
            return new ThemeDef { Name = name, A = H(a), B = H(b), C = H(c), Glow = H(glow), Blob2 = H(blob2), Blob3 = H(blob3), Light = H(light) };
        }

        public static readonly ThemeDef[] All =
        {
            T("Violet",      "#8B5CF6", "#6366F1", "#06B6D4", "#7C3AED", "#EC4899", "#06B6D4", "#A78BFA"),
            T("Rouge gamer", "#EF4444", "#F43F5E", "#F97316", "#DC2626", "#F97316", "#EF4444", "#FCA5A5"),
            T("Vert néon",   "#10B981", "#14B8A6", "#84CC16", "#059669", "#84CC16", "#14B8A6", "#6EE7B7"),
            T("Océan",       "#3B82F6", "#0EA5E9", "#22D3EE", "#2563EB", "#22D3EE", "#6366F1", "#93C5FD"),
            T("Rose",        "#EC4899", "#D946EF", "#A855F7", "#DB2777", "#A855F7", "#F472B6", "#F9A8D4"),
            T("Or",          "#F59E0B", "#F97316", "#EAB308", "#D97706", "#EF4444", "#F59E0B", "#FCD34D")
        };

        public static ThemeDef Find(string name) { return All.FirstOrDefault(t => t.Name == name) ?? All[0]; }

        public static Brush Swatch(ThemeDef t)
        {
            var g = new LinearGradientBrush(t.A, t.C, new Point(0, 0), new Point(1, 1));
            g.Freeze();
            return g;
        }

        static Color Alpha(Color c, byte a) { return Color.FromArgb(a, c.R, c.G, c.B); }

        static RadialGradientBrush Blob(Color c, byte a)
        {
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(Alpha(c, a), 0));
            b.GradientStops.Add(new GradientStop(Alpha(c, 0), 1));
            b.Freeze();
            return b;
        }

        public static void Apply(string name)
        {
            var t = Find(name);
            var r = Application.Current.Resources;
            var accent = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            accent.GradientStops.Add(new GradientStop(t.A, 0));
            accent.GradientStops.Add(new GradientStop(t.B, 0.5));
            accent.GradientStops.Add(new GradientStop(t.C, 1));
            accent.Freeze();
            var nav = new LinearGradientBrush(Alpha(t.A, 0x47), Alpha(t.C, 0x0A), new Point(0, 0), new Point(1, 0));
            nav.Freeze();
            r["Accent"] = accent;
            r["NavSel"] = nav;
            r["Glow"] = t.Glow;
            r["AccentSolid"] = new SolidColorBrush(t.A);
            r["AccentLight"] = new SolidColorBrush(t.Light);
            r["AccentSoft"] = new SolidColorBrush(Alpha(t.A, 0x40));
            r["Blob1"] = Blob(t.Glow, 0x4D);
            r["Blob2"] = Blob(t.Blob2, 0x26);
            r["Blob3"] = Blob(t.Blob3, 0x33);
        }
    }
}
