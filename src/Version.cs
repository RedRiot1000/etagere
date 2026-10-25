using System.Reflection;

// release.ps1 change ces valeurs à chaque publication
[assembly: AssemblyVersion("1.0.0")]
[assembly: AssemblyFileVersion("1.0.0")]
[assembly: AssemblyTitle("Étagère")]
[assembly: AssemblyProduct("Étagère")]

namespace Etagere
{
    public static class AppInfo
    {
        public const string Version = "1.0.0";
        public const string Repo = "RedRiot1000/etagere";   // dépôt GitHub qui publie les mises à jour
    }
}
