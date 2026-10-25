using System.Reflection;

// release.ps1 change ces valeurs à chaque publication
[assembly: AssemblyVersion("1.1.0")]
[assembly: AssemblyFileVersion("1.1.0")]
[assembly: AssemblyTitle("Étagère")]
[assembly: AssemblyProduct("Étagère")]

namespace Etagere
{
    public static class AppInfo
    {
        public const string Version = "1.1.0";
        public const string Repo = "RedRiot1000/etagere";   // dépôt GitHub qui publie les mises à jour

        // Affiché une fois après une mise à jour
        public const string WhatsNew =
            "• Bouton « Scanner » : trouve tous les jeux et applis du PC (Steam, Epic, GOG, Ubisoft, Xbox, menu Démarrer…)\n" +
            "• Meilleure détection jeu / appli\n" +
            "• Nouvelle catégorie « Papiers importants » : tes PDF, scans et documents du bureau\n" +
            "• Les dossiers du bureau sont rangés aussi\n" +
            "• Temps de jeu : durée de ta dernière partie quand tu quittes un jeu\n" +
            "• Tes propres catégories (« Nouvelle catégorie » à gauche)\n" +
            "• « Jouer à quoi ? » : un jeu au hasard\n" +
            "• Glisse-dépose les tuiles pour les ranger dans ton ordre\n" +
            "• 6 thèmes de couleur (en bas à gauche)";
    }
}
