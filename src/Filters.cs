using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BiomeCrafting
{
    /// <summary>
    /// Stan filtrów (biomy, kategorie, statystyki jedzenia) zapisywany w configu
    /// i logika sprawdzania, czy przepis przechodzi przez filtry.
    /// </summary>
    internal static class Filters
    {
        public struct Option
        {
            public string Key;
            private readonly string _pl, _en;
            public string Label => Txt.T(_pl, _en);
            public Option(string key, string pl, string en) { Key = key; _pl = pl; _en = en; }
        }

        public static readonly Option[] Categories =
        {
            new Option("Swords", "Miecze", "Swords"),
            new Option("Axes", "Topory", "Axes"),
            new Option("Clubs", "Maczugi", "Clubs"),
            new Option("Knives", "Noże", "Knives"),
            new Option("Spears", "Włócznie", "Spears"),
            new Option("Polearms", "Drzewcowe", "Polearms"),
            new Option("Bows", "Łuki", "Bows"),
            new Option("Crossbows", "Kusze", "Crossbows"),
            new Option("Magic", "Magia", "Magic"),
            new Option("Shield", "Tarcze", "Shields"),
            new Option("Armor", "Zbroja", "Armor"),
            new Option("Ammo", "Amunicja", "Ammo"),
            new Option("Tool", "Narzędzia", "Tools"),
            new Option("Food", "Jedzenie", "Food"),
            new Option("Potion", "Mikstury", "Potions"),
            new Option("Utility", "Użytkowe", "Utility"),
            new Option("Material", "Materiały", "Materials"),
            new Option("Other", "Inne", "Other"),
        };

        public static readonly Option[] FoodStats =
        {
            new Option("HP", "Zdrowie", "Health"),
            new Option("Stamina", "Kondycja", "Stamina"),
            new Option("Eitr", "Eitr", "Eitr"),
        };

        public static readonly Option[] MeadEffects =
        {
            new Option("Health", "Zdrowie", "Health"),
            new Option("Stamina", "Kondycja", "Stamina"),
            new Option("Eitr", "Eitr", "Eitr"),
            new Option("Resist", "Odporności", "Resistances"),
            new Option("Other", "Inne efekty", "Other effects"),
        };

        // ---- stan

        private static string _rawBiomes, _rawCats, _rawFood, _rawMead;
        private static HashSet<int> _biomes = new HashSet<int>();
        private static HashSet<string> _cats = new HashSet<string>();
        private static HashSet<string> _food = new HashSet<string>();
        private static HashSet<string> _mead = new HashSet<string>();

        private static void Sync()
        {
            string b = Plugin.FilterBiomes.Value ?? "";
            string c = Plugin.FilterCategories.Value ?? "";
            string f = Plugin.FilterFood.Value ?? "";
            string m = Plugin.FilterMead.Value ?? "";
            if (b == _rawBiomes && c == _rawCats && f == _rawFood && m == _rawMead) return;

            _rawBiomes = b; _rawCats = c; _rawFood = f; _rawMead = m;
            _biomes = new HashSet<int>(Split(b).Select(s => int.TryParse(s, out int v) ? v : -1).Where(v => v >= 0));
            _cats = new HashSet<string>(Split(c), StringComparer.OrdinalIgnoreCase);
            _food = new HashSet<string>(Split(f), StringComparer.OrdinalIgnoreCase);
            _mead = new HashSet<string>(Split(m), StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> Split(string s) =>
            s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0);

        public static bool AnyActive
        {
            get { Sync(); return _biomes.Count > 0 || _cats.Count > 0 || _food.Count > 0 || _mead.Count > 0; }
        }

        public static bool IsBiomeOn(int biome) { Sync(); return _biomes.Contains(biome); }
        public static bool IsCategoryOn(string key) { Sync(); return _cats.Contains(key); }
        public static bool IsFoodOn(string key) { Sync(); return _food.Contains(key); }
        public static bool IsMeadOn(string key) { Sync(); return _mead.Contains(key); }

        public static void SetMead(string key, bool on)
        {
            Sync();
            if (on) _mead.Add(key); else _mead.Remove(key);
            Plugin.FilterMead.Value = string.Join(",", MeadEffects.Where(o => _mead.Contains(o.Key)).Select(o => o.Key).ToArray());
        }

        public static void SetBiome(int biome, bool on)
        {
            Sync();
            if (on) _biomes.Add(biome); else _biomes.Remove(biome);
            Plugin.FilterBiomes.Value = string.Join(",", _biomes.OrderBy(i => i).Select(i => i.ToString()).ToArray());
        }

        public static void SetCategory(string key, bool on)
        {
            Sync();
            if (on) _cats.Add(key); else _cats.Remove(key);
            Plugin.FilterCategories.Value = string.Join(",", Categories.Where(o => _cats.Contains(o.Key)).Select(o => o.Key).ToArray());
        }

        public static void SetFood(string key, bool on)
        {
            Sync();
            if (on) _food.Add(key); else _food.Remove(key);
            Plugin.FilterFood.Value = string.Join(",", FoodStats.Where(o => _food.Contains(o.Key)).Select(o => o.Key).ToArray());
        }

        public static void ClearAll()
        {
            Plugin.FilterBiomes.Value = "";
            Plugin.FilterCategories.Value = "";
            Plugin.FilterFood.Value = "";
            Plugin.FilterMead.Value = "";
            Sync();
        }

        // ---- dopasowanie

        /// <summary>Puste zaznaczenie w grupie = brak ograniczenia w tej grupie.</summary>
        public static bool Passes(Recipe recipe)
        {
            Sync();
            if (_biomes.Count == 0 && _cats.Count == 0 && _food.Count == 0 && _mead.Count == 0) return true;
            if (recipe == null || recipe.m_item == null) return true;

            if (_biomes.Count > 0 && !_biomes.Contains(BiomeClassifier.GetBiome(recipe)))
                return false;

            var shared = recipe.m_item.m_itemData?.m_shared;
            string prefab = recipe.m_item.gameObject.name;
            string cat = CategoryKey(shared, prefab);

            // półprodukty z blatu przygotowawczego ("Nieupieczony …") – oceniamy gotowe danie po upieczeniu
            var cooked = CookedResult(prefab);
            if (cooked != null && BiomeClassifier.GetCategory(cooked) == Category.Food)
            {
                cat = "Food";
                shared = cooked;
            }

            if (_cats.Count > 0)
            {
                if (!_cats.Contains(cat)) return false;
            }
            else if (_food.Count > 0 || _mead.Count > 0)
            {
                // zaznaczono tylko statystyki jedzenia / efekty miodów -> pokazujemy samo jedzenie / miody
                bool ok = (_food.Count > 0 && cat == "Food") || (_mead.Count > 0 && cat == "Potion");
                if (!ok) return false;
            }

            if (cat == "Food" && _food.Count > 0)
            {
                // jedzenie: musi mieć WSZYSTKIE zaznaczone statystyki
                foreach (string stat in _food)
                    if (!FoodHas(shared, stat)) return false;
            }

            if (cat == "Potion" && _mead.Count > 0)
            {
                // miody: wystarczy JEDEN z zaznaczonych efektów (każdy miód ma jeden efekt)
                if (!_mead.Contains(MeadEffect(prefab, shared))) return false;
            }
            return true;
        }

        /// <summary>Jak CategoryKey, ale brzeczki miodowe (MeadBase…) liczy jako miody, a nie materiały.</summary>
        public static string CategoryKey(ItemDrop.ItemData.SharedData s, string prefab)
        {
            if (IsMead(prefab)) return "Potion";
            return CategoryKey(s);
        }

        // ---- półprodukty do upieczenia

        private static readonly Dictionary<string, ItemDrop.ItemData.SharedData> CookedCache =
            new Dictionary<string, ItemDrop.ItemData.SharedData>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, GameObject> _conversions;
        private static ObjectDB _cookedDb;

        /// <summary>
        /// Dla półproduktu (np. MeatPlatterUncooked) zwraca dane gotowego dania:
        /// najpierw z przepisów pieca / rusztu (CookingStation), potem po nazwie bez "Uncooked".
        /// null, gdy przedmiot nie jest półproduktem.
        /// </summary>
        public static ItemDrop.ItemData.SharedData CookedResult(string prefab)
        {
            if (string.IsNullOrEmpty(prefab) || ObjectDB.instance == null) return null;
            if (ObjectDB.instance != _cookedDb)
            {
                _cookedDb = ObjectDB.instance;
                CookedCache.Clear();
                _conversions = null;
            }
            if (CookedCache.TryGetValue(prefab, out var cached)) return cached;

            // jeśli sam przedmiot ma już statystyki jedzenia (gra pokazuje je w opisie), używamy ich.
            // Wyjątek: półprodukty "…Uncooked" – w 1.0 część z nich ma w danych nieaktualne statystyki
            // (np. nieupieczony poteitr), więc dla nich liczy się to, co faktycznie wychodzi z pieca.
            GameObject self = ObjectDB.instance.GetItemPrefab(prefab);
            if (IsFood(self) && !prefab.EndsWith("Uncooked", StringComparison.OrdinalIgnoreCase))
            {
                var own = self.GetComponent<ItemDrop>().m_itemData.m_shared;
                CookedCache[prefab] = own;
                return own;
            }

            ItemDrop.ItemData.SharedData result = null;
            try
            {
                BuildConversions();
                GameObject to = null;
                if (_conversions != null) _conversions.TryGetValue(prefab, out to);

                if (to == null && prefab.EndsWith("Uncooked", StringComparison.OrdinalIgnoreCase))
                    to = ObjectDB.instance.GetItemPrefab(prefab.Substring(0, prefab.Length - "Uncooked".Length));

                // uczty: FeastOceans_Material -> stawiany obiekt FeastOceans, z którego je się porcje
                if (to == null && prefab.EndsWith("_Material", StringComparison.OrdinalIgnoreCase))
                    to = FeastFood(prefab.Substring(0, prefab.Length - "_Material".Length));

                ItemDrop drop = to != null ? to.GetComponent<ItemDrop>() : null;
                result = drop != null ? drop.m_itemData?.m_shared : null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Nie udało się sprawdzić półproduktu {prefab}: {e.Message}");
            }

            if (result == null && prefab.StartsWith("Feast", StringComparison.OrdinalIgnoreCase))
                LogFeastDebug(prefab);

            CookedCache[prefab] = result;
            return result;
        }

        /// <summary>Szuka przedmiotu-jedzenia powiązanego z ucztą (jako przedmiot albo w komponentach stawianego obiektu).</summary>
        private static GameObject FeastFood(string baseName)
        {
            GameObject item = ObjectDB.instance.GetItemPrefab(baseName);
            if (IsFood(item)) return item;

            GameObject piece = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(baseName) : null;
            if (piece == null) return null;

            var gameAsm = typeof(Player).Assembly;
            foreach (MonoBehaviour mb in piece.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Assembly != gameAsm) continue;
                foreach (var f in mb.GetType().GetFields(System.Reflection.BindingFlags.Instance |
                                                          System.Reflection.BindingFlags.Public |
                                                          System.Reflection.BindingFlags.NonPublic))
                {
                    object v;
                    try { v = f.GetValue(mb); } catch { continue; }
                    GameObject g = v as GameObject ?? (v as ItemDrop)?.gameObject;
                    if (IsFood(g)) return g;
                }
            }
            return null;
        }

        /// <summary>Gdy nie uda się znaleźć statystyk uczty – zapisz w logu, z czego składa się jej obiekt (do poprawki).</summary>
        private static void LogFeastDebug(string prefab)
        {
            try
            {
                string baseName = prefab.EndsWith("_Material", StringComparison.OrdinalIgnoreCase)
                    ? prefab.Substring(0, prefab.Length - "_Material".Length) : prefab;
                GameObject piece = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(baseName) : null;
                string comps = piece == null ? "(brak obiektu " + baseName + ")"
                    : string.Join(", ", piece.GetComponentsInChildren<Component>(true).Select(c => c.GetType().Name).Distinct().ToArray());
                Plugin.Log.LogInfo($"[uczta] {prefab}: nie znaleziono statystyk jedzenia. Komponenty: {comps}");
            }
            catch { }
        }

        private static bool IsFood(GameObject go)
        {
            var sh = go != null ? go.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
            return sh != null && (sh.m_food > 0f || sh.m_foodStamina > 0f || sh.m_foodEitr > 0f);
        }

        private static void BuildConversions()
        {
            if (_conversions != null || ZNetScene.instance == null || ZNetScene.instance.m_prefabs == null) return;
            _conversions = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

            foreach (GameObject go in ZNetScene.instance.m_prefabs)
            {
                if (go == null) continue;
                CookingStation cs = go.GetComponent<CookingStation>();
                if (cs == null) continue;
                if (!(HarmonyLib.Traverse.Create(cs).Field("m_conversion").GetValue() is System.Collections.IEnumerable list)) continue;

                foreach (object conv in list)
                {
                    var t = HarmonyLib.Traverse.Create(conv);
                    ItemDrop from = t.Field("m_from").GetValue() as ItemDrop;
                    ItemDrop to = t.Field("m_to").GetValue() as ItemDrop;
                    if (from == null || to == null) continue;
                    string key = from.gameObject.name;
                    if (!_conversions.ContainsKey(key)) _conversions[key] = to.gameObject;
                }
            }
        }

        public static bool IsMead(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return false;
            string p = prefab.ToLowerInvariant();
            return p.StartsWith("meadbase") || p.StartsWith("mead") || p.StartsWith("barleywine");
        }

        /// <summary>Efekt miodu z nazwy prefabu (np. MeadBaseStaminaMinor, MeadFrostResist, BarleyWineBase).</summary>
        public static string MeadEffect(string prefab, ItemDrop.ItemData.SharedData s)
        {
            string p = (prefab ?? "").ToLowerInvariant();
            if (p.Contains("health")) return "Health";
            if (p.Contains("stamina")) return "Stamina";
            if (p.Contains("eitr")) return "Eitr";
            if (p.Contains("resist") || p.Contains("barleywine")) return "Resist";
            return "Other";
        }

        public static string CategoryKey(ItemDrop.ItemData.SharedData s)
        {
            Category c = BiomeClassifier.GetCategory(s);
            switch (c)
            {
                case Category.Weapon:
                    string skill = s.m_skillType.ToString();
                    switch (skill)
                    {
                        case "Swords": case "Axes": case "Clubs": case "Knives": case "Spears":
                        case "Polearms": case "Bows": case "Crossbows":
                            return skill;
                        case "ElementalMagic": case "BloodMagic":
                            return "Magic";
                        default:
                            return "Other";
                    }
                case Category.Shield: return "Shield";
                case Category.Armor: return "Armor";
                case Category.Ammo: return "Ammo";
                case Category.Tool: return "Tool";
                case Category.Food: return "Food";
                case Category.Potion: return "Potion";
                case Category.Utility: return "Utility";
                case Category.Material: return "Material";
                default: return "Other";
            }
        }

        /// <summary>
        /// Czy dana statystyka jest "główna" dla jedzenia: co najmniej połowa najwyższej z trzech.
        /// Dzięki temu "Kondycja" nie łapie każdego jedzenia, które daje choć trochę kondycji.
        /// </summary>
        public static bool FoodHas(ItemDrop.ItemData.SharedData s, string stat)
        {
            if (s == null) return false;
            float hp = s.m_food, st = s.m_foodStamina, ei = s.m_foodEitr;
            float max = Mathf.Max(hp, Mathf.Max(st, ei));
            if (max <= 0f) return false;

            float v = stat == "HP" ? hp : stat == "Stamina" ? st : stat == "Eitr" ? ei : 0f;
            return v > 0f && v >= max * 0.5f;
        }
    }
}
