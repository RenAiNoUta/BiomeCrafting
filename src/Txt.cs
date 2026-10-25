using HarmonyLib;

namespace BiomeCrafting
{
    /// <summary>
    /// Teksty moda w dwóch językach. Polski, gdy gra jest po polsku; w każdym innym języku angielski.
    /// Można wymusić w configu (Jezyk = PL / EN).
    /// </summary>
    internal static class Txt
    {
        private static bool? _polish;
        private static string _lastSetting;

        public static bool Polish
        {
            get
            {
                string setting = (Plugin.Language.Value ?? "Auto").Trim().ToUpperInvariant();
                if (setting != _lastSetting) { _lastSetting = setting; _polish = null; }

                if (setting == "PL") return true;
                if (setting == "EN") return false;
                if (_polish.HasValue) return _polish.Value;

                if (Localization.instance == null) return true; // jeszcze nie wiadomo – nie zapamiętuj
                string lang = Traverse.Create(Localization.instance).Method("GetSelectedLanguage").GetValue() as string;
                _polish = string.IsNullOrEmpty(lang) || lang.IndexOf("polish", System.StringComparison.OrdinalIgnoreCase) >= 0;
                return _polish.Value;
            }
        }

        public static string T(string pl, string en) => Polish ? pl : en;

        /// <summary>Wywołaj po zmianie języka w grze.</summary>
        public static void Reset() => _polish = null;
    }
}
