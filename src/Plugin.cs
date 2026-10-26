using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace BiomeCrafting
{
    [BepInPlugin(Guid, ModName, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "renainouta.biomecrafting";
        public const string ModName = "BiomeCrafting";
        public const string Version = "0.7.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        internal static FilterPanel Runner;

        internal static ConfigEntry<bool> SortByBiome;
        internal static ConfigEntry<bool> ShowHeaders;
        internal static ConfigEntry<bool> ShowUndiscovered;
        internal static ConfigEntry<bool> HideUndiscoveredNames;
        internal static ConfigEntry<bool> ShowOtherStations;
        internal static ConfigEntry<bool> ShowUnlockHints;
        internal static ConfigEntry<bool> ColorKnown;
        internal static ConfigEntry<string> CraftableColor;
        internal static ConfigEntry<string> MissingMaterialsColor;
        internal static ConfigEntry<string> StationLevelColor;
        internal static ConfigEntry<string> UndiscoveredColor;
        internal static ConfigEntry<string> OtherStationColor;
        internal static ConfigEntry<string> CollapsedBiomes;
        internal static ConfigEntry<string> BiomeOverrides;
        internal static ConfigEntry<bool> DumpRecipes;
        internal static ConfigEntry<bool> AutoDetect;
        internal static ConfigEntry<string> Language;
        internal static ConfigEntry<bool> ShowFilterPanel;
        internal static ConfigEntry<bool> ShowShoppingList;
        internal static ConfigEntry<string> ShoppingListPosition;
        internal static ConfigEntry<bool> ColorblindMode;
        internal static ConfigEntry<float> FilterPanelScale;
        internal static ConfigEntry<string> FilterBiomes;
        internal static ConfigEntry<string> FilterCategories;
        internal static ConfigEntry<string> FilterFood;
        internal static ConfigEntry<string> FilterMead;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            SortByBiome = Config.Bind("1 - Ogolne", "SortujPoBiomach", true,
                "Sortuje listę przepisów według biomów (Łąki, Czarny Las, Bagna...), a w ramach biomu według kategorii.");
            ShowHeaders = Config.Bind("1 - Ogolne", "PokazNaglowki", true,
                "Pokazuje nagłówki biomów na liście. Kliknięcie nagłówka zwija/rozwija grupę.");

            ShowUndiscovered = Config.Bind("2 - Widocznosc", "PokazNieodkryte", false,
                "Pokazuje przepisy, których jeszcze nie odkryłeś (nie da się ich wytworzyć). " +
                "Można to też przełączać checkboxem w panelu filtrów.");
            HideUndiscoveredNames = Config.Bind("2 - Widocznosc", "UkryjNazwyNieodkrytych", false,
                "Tryb bez spoilerów: nieodkryte przepisy pokazują się jako ??? z zaciemnioną ikoną.");
            ShowOtherStations = Config.Bind("2 - Widocznosc", "PokazZInnychStacji", false,
                "Pokazuje na szaro także przepisy wymagające innej stacji (np. kuźni przy warsztacie).");

            ShowUnlockHints = Config.Bind("2 - Widocznosc", "PokazJakOdblokowac", true,
                "Po zaznaczeniu nieodkrytego przepisu dopisuje w opisie, których materiałów jeszcze nie podniosłeś i z jakiego biomu są.");

            ColorblindMode = Config.Bind("5 - Kolory", "TrybDaltonistow", false,
                "Zamienia kolory moda na paletę rozróżnialną przy daltonizmie (niebieski, żółty, pomarańczowy, różowofioletowy). " +
                "Przy wyłączonym trybie działają kolory ustawione poniżej. Zmiana wymaga ponownego uruchomienia gry.");
            ColorKnown = Config.Bind("5 - Kolory", "KolorujNauczone", true,
                "Koloruje nauczone przepisy według stanu. Wyłącz, żeby zostawić kolory gry (biały / szary).");
            CraftableColor = Config.Bind("5 - Kolory", "KolorMoznaWytworzyc2", "#8CE68C",
                "Nauczony, masz materiały – można wytworzyć teraz. Domyślnie jasnozielony.");
            MissingMaterialsColor = Config.Bind("5 - Kolory", "KolorBrakMaterialow2", "#FFF28C",
                "Nauczony, ale brakuje materiałów. Domyślnie jasnożółty.");
            StationLevelColor = Config.Bind("5 - Kolory", "KolorZaNiskiPoziomStacji2", "#FFA04D",
                "Nauczony, ale stacja ma za niski poziom (trzeba ją rozbudować). Domyślnie pomarańczowy.");
            UndiscoveredColor = Config.Bind("5 - Kolory", "KolorNieodkrytych", "#EB7366",
                "Jeszcze nienauczony. Domyślnie jasnoczerwony.");
            OtherStationColor = Config.Bind("5 - Kolory", "KolorZInnychStacji", "#C8A0FF",
                "Wymaga innej stacji (gdy włączone PokazZInnychStacji). Domyślnie jasnofioletowy.");

            CollapsedBiomes = Config.Bind("3 - Stan", "ZwinieteBiomy", "",
                "Numery zwiniętych biomów (zapisywane automatycznie). 0=Łąki 1=Czarny Las 2=Bagna 3=Góry 4=Równiny 5=Mgliste Ziemie 6=Popielne Ziemie 7=Głęboka Północ.");
            BiomeOverrides = Config.Bind("4 - Reczne przypisania", "PrzypisaniaBiomow", "",
                "Ręczne przypisanie przedmiotu do biomu, gdy automat się pomyli. Format: NazwaPrefabu=Biom; kolejny=Biom. " +
                "Biom jako numer (0-7) lub nazwa, np. \"ArrowFire=Laki; KnifeChitin=Bagna\".");

            Language = Config.Bind("1 - Ogolne", "Jezyk", "Auto",
                "Język tekstów moda: Auto (polski, gdy gra jest po polsku, w innych językach angielski), PL albo EN. / " +
                "Mod text language: Auto (Polish if the game is in Polish, otherwise English), PL or EN.");
            AutoDetect = Config.Bind("4 - Reczne przypisania", "AutoWykrywanieBiomow", true,
                "Dla surowców spoza wbudowanej tabeli wykrywa biom z danych gry (gdzie pojawia się stwór, z którego wypada, " +
                "albo gdzie rośnie roślina / złoże). Ręczne przypisania mają pierwszeństwo.");
            DumpRecipes = Config.Bind("4 - Reczne przypisania", "ZapiszListePrzepisow", true,
                "Po wejściu do świata zapisuje BepInEx\\config\\BiomeCrafting_przepisy.txt z biomem i kategorią każdego przepisu (do sprawdzenia klasyfikacji).");

            ShowFilterPanel = Config.Bind("6 - Filtry", "PokazPanelFiltrow", true,
                "Pokazuje panel filtrów po lewej stronie okna craftingu.");
            ShowShoppingList = Config.Bind("6 - Filtry", "PokazListeMaterialow", true,
                "Przycisk \"+ Lista\" przy przepisie i lista materiałów do zebrania (po prawej stronie ekranu, także przy skrzyni).");
            ShoppingListPosition = Config.Bind("6 - Filtry", "PozycjaListy", "LewyDol",
                "Gdzie wyświetlać listę materiałów: LewyDol, LewaGora, PrawyDol, PrawaGora.");
            FilterPanelScale = Config.Bind("6 - Filtry", "SkalaPanelu", 1.0f,
                "Wielkość panelu filtrów (0.5 – 2.0).");
            FilterBiomes = Config.Bind("6 - Filtry", "FiltrBiomy", "",
                "Zaznaczone biomy (zapisywane automatycznie).");
            FilterCategories = Config.Bind("6 - Filtry", "FiltrKategorie", "",
                "Zaznaczone kategorie (zapisywane automatycznie).");
            FilterFood = Config.Bind("6 - Filtry", "FiltrJedzenie", "",
                "Zaznaczone statystyki jedzenia (zapisywane automatycznie).");
            FilterMead = Config.Bind("6 - Filtry", "FiltrMiody", "",
                "Zaznaczone efekty miodów (zapisywane automatycznie).");

            _harmony = new Harmony(Guid);
            CraftingListPatch.Apply(_harmony);

            // osobny obiekt, żeby panel i przewijanie działały niezależnie od obiektu BepInEx
            var host = new UnityEngine.GameObject("BiomeCrafting_Host");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            Runner = host.AddComponent<FilterPanel>();

            Log.LogInfo($"{ModName} {Version} załadowany.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
