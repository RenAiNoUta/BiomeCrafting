using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BiomeCrafting
{
    /// <summary>
    /// Przebudowuje listę przepisów w InventoryGui: sortowanie po biomach, nagłówki (zwijane),
    /// nieodkryte przepisy / przepisy z innych stacji na szaro i zablokowane do craftowania.
    /// Wszystkie prywatne pola gry czytane są przez refleksję, żeby mod przeżył drobne aktualizacje.
    /// </summary>
    internal static class CraftingListPatch
    {
        private enum LockReason { Undiscovered, OtherStation }

        private class Entry
        {
            public object Pair;
            public GameObject Element;
            public Recipe Recipe;
            public int Biome;
            public Category Category;
            public int Sub;
            public string Name;
        }

        private static readonly List<GameObject> Headers = new List<GameObject>();
        private static readonly Dictionary<Recipe, LockReason> Locked = new Dictionary<Recipe, LockReason>();
        private static readonly HashSet<int> GroupsWithRecipes = new HashSet<int>();

        private static readonly Color HeaderColor = new Color(1f, 0.78f, 0.36f, 1f);
        private const float HeaderLeftPadding = 8f;
        private static readonly Color DefaultUndiscoveredColor = new Color(0.92f, 0.45f, 0.40f, 1f);
        private static readonly Color DefaultOtherStationColor = new Color(0.78f, 0.63f, 1.00f, 1f);
        private static readonly Color DefaultCraftableColor = new Color(0.55f, 0.90f, 0.55f, 1f);
        private static readonly Color DefaultMissingMaterialsColor = new Color(1.00f, 0.95f, 0.55f, 1f);
        private static readonly Color DefaultStationLevelColor = new Color(1.00f, 0.63f, 0.30f, 1f);
        private static readonly Color LockedIconColor = new Color(0.55f, 0.55f, 0.55f, 1f);

        private static Color ParseColor(string html, Color fallback)
        {
            if (!string.IsNullOrWhiteSpace(html) && ColorUtility.TryParseHtmlString(html.Trim(), out Color c))
                return c;
            return fallback;
        }
        private static readonly Color HiddenIconColor = new Color(0f, 0f, 0f, 1f);

        private static MethodInfo _inCraftTab;
        private static MethodInfo _updateCraftingPanel;
        private static bool _warnedMismatch;

        // ------------------------------------------------------------------ patching

        public static void Apply(Harmony harmony)
        {
            Type gui = typeof(InventoryGui);
            _inCraftTab = FindMethod(gui, "InCraftTab");
            _updateCraftingPanel = FindMethod(gui, "UpdateCraftingPanel");

            Patch(harmony, gui, "UpdateRecipeList", nameof(UpdateRecipeList_Prefix), nameof(UpdateRecipeList_Postfix));
            Patch(harmony, gui, "UpdateRecipe", null, nameof(UpdateRecipe_Postfix));
            Patch(harmony, gui, "OnCraftPressed", nameof(BlockLocked_Prefix), null);
            Patch(harmony, gui, "DoCrafting", nameof(BlockLocked_Prefix), null);
        }

        private static MethodInfo FindMethod(Type t, string name)
        {
            try
            {
                return t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.Name == name);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Błąd przy szukaniu {t.Name}.{name}: {e.Message}");
                return null;
            }
        }

        private static void Patch(Harmony harmony, Type t, string method, string prefix, string postfix)
        {
            MethodInfo target = FindMethod(t, method);
            if (target == null)
            {
                Plugin.Log.LogWarning($"Nie znaleziono {t.Name}.{method} – ta część moda nie zadziała (może zmiana w wersji gry).");
                return;
            }
            try
            {
                harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(CraftingListPatch), prefix)),
                    postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(CraftingListPatch), postfix)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Nie udało się podpiąć {t.Name}.{method}: {e}");
            }
        }

        // ------------------------------------------------------------------ UpdateRecipeList

        public static void UpdateRecipeList_Prefix(InventoryGui __instance, List<Recipe> recipes)
        {
            DestroyHeaders();
            Locked.Clear();
            HintCache.Clear();
            StationHintCache.Clear();
            GroupsWithRecipes.Clear();

            if (!Plugin.SortByBiome.Value || recipes == null) return;
            Player player = Player.m_localPlayer;
            if (player == null) return;

            try
            {
                bool craftTab = IsCraftTab(__instance);
                if (craftTab && (Plugin.ShowUndiscovered.Value || Plugin.ShowOtherStations.Value))
                    AddLockedRecipes(player, recipes);

                // w zakładce "Ulepsz" nie ma nagłówków, więc nic nie zwijamy
                bool canCollapse = Plugin.ShowHeaders.Value && craftTab;
                HashSet<int> collapsed = GetCollapsed();

                for (int i = recipes.Count - 1; i >= 0; i--)
                {
                    Recipe r = recipes[i];
                    if (r == null || r.m_item == null) continue;

                    if (!Filters.Passes(r))
                    {
                        recipes.RemoveAt(i);
                        Locked.Remove(r);
                        continue;
                    }

                    int biome = BiomeClassifier.GetBiome(r);
                    GroupsWithRecipes.Add(biome);
                    if (canCollapse && collapsed.Contains(biome))
                        recipes.RemoveAt(i);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Błąd w UpdateRecipeList (prefix): {e}");
            }
        }

        private static void AddLockedRecipes(Player player, List<Recipe> recipes)
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null || db.m_recipes == null) return;

            var present = new HashSet<Recipe>(recipes);
            CraftingStation station = player.GetCurrentCraftingStation();

            foreach (Recipe r in db.m_recipes)
            {
                if (r == null || !r.m_enabled || r.m_item == null || present.Contains(r)) continue;
                var shared = r.m_item.m_itemData?.m_shared;
                if (shared == null || shared.m_icons == null || shared.m_icons.Length == 0) continue;

                bool known = player.IsRecipeKnown(shared.m_name);
                bool sameStation = IsSameStation(r, station);

                if (known && sameStation) continue;                          // gra ukryła go z innego powodu
                if (!sameStation && !Plugin.ShowOtherStations.Value) continue;
                if (!known && !Plugin.ShowUndiscovered.Value) continue;

                recipes.Add(r);
                present.Add(r);
                Locked[r] = known ? LockReason.OtherStation : LockReason.Undiscovered;
            }
        }

        private static bool IsSameStation(Recipe r, CraftingStation current)
        {
            CraftingStation required = r.GetRequiredStation(1);
            if (required == null)
            {
                if (current == null) return true;
                return Traverse.Create(current).Field("m_showBasicRecipies").GetValue() is bool b && b;
            }
            return current != null && required.m_name == current.m_name;
        }

        public static void UpdateRecipeList_Postfix(InventoryGui __instance)
        {
            if (!Plugin.SortByBiome.Value) return;
            try
            {
                Relayout(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Błąd w UpdateRecipeList (postfix): {e}");
            }
        }

        private static void Relayout(InventoryGui gui)
        {
            Traverse tr = Traverse.Create(gui);
            IList pairs = tr.Field("m_availableRecipes").GetValue() as IList;
            List<GameObject> elements = tr.Field("m_recipeList").GetValue<List<GameObject>>();
            RectTransform root = tr.Field("m_recipeListRoot").GetValue<RectTransform>();
            GameObject prefab = tr.Field("m_recipeElementPrefab").GetValue<GameObject>();
            if (pairs == null || root == null) return;

            if (elements != null && elements.Count != pairs.Count && !_warnedMismatch)
            {
                _warnedMismatch = true;
                Plugin.Log.LogWarning($"Liczba elementów ({elements.Count}) różni się od liczby przepisów ({pairs.Count}).");
            }

            // Zbierz wpisy
            var entries = new List<Entry>();
            for (int i = 0; i < pairs.Count; i++)
            {
                object pair = pairs[i];
                Recipe recipe = PairRecipe(pair);
                GameObject element = PairElement(pair) ?? (elements != null && i < elements.Count ? elements[i] : null);
                if (recipe == null || element == null) continue;

                var shared = recipe.m_item != null ? recipe.m_item.m_itemData?.m_shared : null;
                Category cat = BiomeClassifier.GetCategory(shared);
                entries.Add(new Entry
                {
                    Pair = pair,
                    Element = element,
                    Recipe = recipe,
                    Biome = BiomeClassifier.GetBiome(recipe),
                    Category = cat,
                    Sub = BiomeClassifier.SubOrder(shared, cat),
                    Name = shared != null ? Localize(shared.m_name) : recipe.name
                });
            }
            if (entries.Count == 0 && GroupsWithRecipes.Count == 0) return;

            // Odstęp między wierszami
            float space = tr.Field("m_recipeListSpace").GetValue() is float f ? f : 0f;
            if (space <= 0f && entries.Count >= 2)
                space = Mathf.Abs(Pos(entries[0].Element).y - Pos(entries[1].Element).y);
            if (space <= 0f) space = 32f;
            float x = entries.Count > 0 ? Pos(entries[0].Element).x : 0f;

            HashSet<int> collapsed = GetCollapsed();
            // zakładka "Ulepsz": jedna lista bez nagłówków (nadal posortowana od najstarszego biomu)
            bool headers = Plugin.ShowHeaders.Value && prefab != null && IsCraftTab(gui);

            pairs.Clear();
            elements?.Clear();

            int row = 0;
            for (int biome = 0; biome < BiomeClassifier.BiomeCount; biome++)
            {
                var group = entries.Where(e => e.Biome == biome)
                                   .OrderBy(e => (int)e.Category)
                                   .ThenBy(e => e.Sub)
                                   .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                                   .ToList();

                bool isCollapsed = headers && collapsed.Contains(biome);
                if (group.Count == 0 && !(isCollapsed && GroupsWithRecipes.Contains(biome))) continue;

                if (headers)
                {
                    CreateHeader(prefab, root, biome, isCollapsed, new Vector2(x, -row * space));
                    row++;
                }

                foreach (Entry e in group)
                {
                    pairs.Add(e.Pair);
                    elements?.Add(e.Element);
                    if (e.Element.transform is RectTransform rt)
                        rt.anchoredPosition = new Vector2(x, -row * space);
                    if (Locked.TryGetValue(e.Recipe, out LockReason reason))
                        StyleLocked(e.Element, reason);
                    else if (Plugin.ColorKnown.Value)
                        StyleKnown(e);
                    row++;
                }
            }

            float baseSize = tr.Field("m_recipeListBaseSize").GetValue() is float bs ? bs : 0f;
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(baseSize, row * space));
        }

        // ------------------------------------------------------------------ nagłówki

        private static void CreateHeader(GameObject prefab, RectTransform root, int biome, bool isCollapsed, Vector2 pos)
        {
            GameObject go = UnityEngine.Object.Instantiate(prefab, root);
            go.name = "BiomeCrafting_Header_" + biome;
            go.SetActive(true); // prefab gry jest nieaktywny – bez tego wiersz był pusty i nieklikalny
            if (go.transform is RectTransform rt) rt.anchoredPosition = pos;

            Component label = FindLabel(go);
            foreach (Transform child in go.transform)
            {
                if (label == null || !label.transform.IsChildOf(child))
                    child.gameObject.SetActive(false);
            }
            // upewnij się, że etykieta i jej rodzice są włączeni
            for (Transform t = label != null ? label.transform : null; t != null && t != go.transform; t = t.parent)
                t.gameObject.SetActive(true);

            // rozciągnij etykietę na całą szerokość wiersza (bez miejsca na ikonę), z małym marginesem
            if (label != null && label.transform is RectTransform lrt && label.transform.parent == go.transform)
            {
                lrt.anchorMin = new Vector2(0f, 0f);
                lrt.anchorMax = new Vector2(1f, 1f);
                lrt.pivot = new Vector2(0f, 0.5f);
                lrt.offsetMin = new Vector2(HeaderLeftPadding, 0f);
                lrt.offsetMax = new Vector2(-4f, 0f);
            }
            if (label is TMP_Text htmp) htmp.alignment = TextAlignmentOptions.MidlineLeft;
            else if (label is Text hleg) hleg.alignment = TextAnchor.MiddleLeft;

            SetLabel(label, (isCollapsed ? "[+]  " : "[-]  ") + BiomeClassifier.BiomeName(biome), HeaderColor, bold: true);

            Button btn = go.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick = new Button.ButtonClickedEvent();
                int b = biome;
                btn.onClick.AddListener(() => ToggleBiome(b));
            }

            Headers.Add(go);
        }

        private static void DestroyHeaders()
        {
            foreach (GameObject h in Headers)
                if (h != null) UnityEngine.Object.Destroy(h);
            Headers.Clear();
        }

        /// <summary>Rozwija biom (bez odświeżania – wywołujący odświeża panel sam).</summary>
        internal static void ExpandBiome(int biome)
        {
            HashSet<int> collapsed = GetCollapsed();
            if (!collapsed.Remove(biome)) return;
            Plugin.CollapsedBiomes.Value = string.Join(",", collapsed.OrderBy(i => i).Select(i => i.ToString()).ToArray());
        }

        private static void ToggleBiome(int biome)
        {
            HashSet<int> collapsed = GetCollapsed();
            if (!collapsed.Remove(biome)) collapsed.Add(biome);
            Plugin.CollapsedBiomes.Value = string.Join(",", collapsed.OrderBy(i => i).Select(i => i.ToString()).ToArray());
            RefreshPanel();

            // Gra po odświeżeniu przewija do zaznaczonego przepisu – ustawiamy widok na klikniętym nagłówku.
            // Robimy to przez kilka klatek, bo przewijanie w grze bywa płynne i nadpisałoby jedno ustawienie.
            if (Plugin.Runner != null)
                Plugin.Runner.StartCoroutine(ScrollToHeader(biome));
        }

        private static IEnumerator ScrollToHeader(int biome)
        {
            for (int frame = 0; frame < 10; frame++)
            {
                ApplyScroll(biome);
                yield return null;
            }
        }

        private static void ApplyScroll(int biome)
        {
            try
            {
                string name = "BiomeCrafting_Header_" + biome;
                GameObject header = Headers.FirstOrDefault(h => h != null && h.name == name);
                if (header == null) return;

                ScrollRect sr = header.GetComponentInParent<ScrollRect>();
                if (sr == null || sr.content == null) return;

                Canvas.ForceUpdateCanvases();
                RectTransform viewport = sr.viewport != null ? sr.viewport : (RectTransform)sr.transform;
                float scrollable = sr.content.rect.height - viewport.rect.height;
                sr.velocity = Vector2.zero;

                if (scrollable <= 0f)
                {
                    sr.verticalNormalizedPosition = 1f;
                    return;
                }

                float fromTop = -((RectTransform)header.transform).anchoredPosition.y;
                sr.verticalNormalizedPosition = Mathf.Clamp01(1f - fromTop / scrollable);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Nie udało się przewinąć listy: {e.Message}");
            }
        }

        internal static void RefreshPanel()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || _updateCraftingPanel == null) return;
            try
            {
                var ps = _updateCraftingPanel.GetParameters();
                object[] args = ps.Select(p => p.ParameterType == typeof(bool) ? (object)false
                                               : (p.HasDefaultValue ? p.DefaultValue : null)).ToArray();
                _updateCraftingPanel.Invoke(gui, args);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Nie udało się odświeżyć panelu craftingu: {e}");
            }
        }

        private static HashSet<int> GetCollapsed()
        {
            var set = new HashSet<int>();
            string raw = Plugin.CollapsedBiomes.Value;
            if (string.IsNullOrEmpty(raw)) return set;
            foreach (string s in raw.Split(','))
                if (int.TryParse(s.Trim(), out int v)) set.Add(v);
            return set;
        }

        // ------------------------------------------------------------------ zablokowane przepisy

        private static void StyleLocked(GameObject element, LockReason reason)
        {
            bool hide = reason == LockReason.Undiscovered && Plugin.HideUndiscoveredNames.Value;

            Color textColor = reason == LockReason.Undiscovered
                ? Palette.Undiscovered
                : Palette.OtherStation;

            Component label = FindLabel(element);
            SetLabel(label, hide ? "???" : null, textColor, bold: false);

            Transform icon = element.transform.Find("icon");
            Image img = icon != null ? icon.GetComponent<Image>() : null;
            if (img != null) img.color = hide ? HiddenIconColor : LockedIconColor;
        }

        /// <summary>Koloruje znany przepis: można wytworzyć / za niski poziom stacji / brak materiałów.</summary>
        private static void StyleKnown(Entry e)
        {
            Component label = FindLabel(e.Element);
            if (label == null) return;

            Player player = Player.m_localPlayer;
            int quality = PairQuality(e.Pair);

            Color color;
            if (StationLevelTooLow(player, e.Recipe, quality))
                color = Palette.StationLevel;
            else if (CanCraft(e.Pair, player, e.Recipe, quality, label))
                color = Palette.Craftable;
            else
                color = Palette.MissingMaterials;

            SetLabel(label, null, color, bold: false);
        }

        private static int PairQuality(object pair)
        {
            // W zakładce ulepszeń para zawiera przedmiot z ekwipunku – ulepszamy do poziomu +1
            Traverse t = Traverse.Create(pair);
            var item = (t.Property("ItemData").GetValue() ?? t.Property("Value").GetValue()) as ItemDrop.ItemData;
            return item != null ? item.m_quality + 1 : 1;
        }

        private static bool StationLevelTooLow(Player player, Recipe recipe, int quality)
        {
            try
            {
                if (player == null) return false;
                CraftingStation current = player.GetCurrentCraftingStation();
                CraftingStation required = recipe.GetRequiredStation(quality);
                if (current == null || required == null || required.m_name != current.m_name) return false;
                return current.GetLevel() < recipe.GetRequiredStationLevel(quality);
            }
            catch
            {
                return false;
            }
        }

        private static MethodInfo _haveRequirements;
        private static bool _haveRequirementsSearched;

        private static bool CanCraft(object pair, Player player, Recipe recipe, int quality, Component label)
        {
            // 1) gra mogła już to policzyć w parze (nowsze wersje)
            if (Traverse.Create(pair).Property("CanCraft").GetValue() is bool fromPair) return fromPair;

            // 2) zapytaj gracza: Player.HaveRequirements(Recipe, bool discover, int quality, ...)
            if (!_haveRequirementsSearched)
            {
                _haveRequirementsSearched = true;
                _haveRequirements = typeof(Player).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "HaveRequirements") return false;
                        var p = m.GetParameters();
                        return p.Length >= 3 && p[0].ParameterType == typeof(Recipe)
                               && p[1].ParameterType == typeof(bool) && p[2].ParameterType == typeof(int);
                    });
            }
            if (_haveRequirements != null && player != null)
            {
                try
                {
                    var ps = _haveRequirements.GetParameters();
                    object[] args = new object[ps.Length];
                    args[0] = recipe; args[1] = false; args[2] = quality;
                    for (int i = 3; i < ps.Length; i++)
                        args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue
                                : (ps[i].ParameterType == typeof(int) ? (object)1 : null);
                    return (bool)_haveRequirements.Invoke(player, args);
                }
                catch { /* spróbuj niżej */ }
            }

            // 3) ostatecznie: gra maluje na biało to, co da się wytworzyć
            if (label is TMP_Text tmp) return tmp.color.grayscale > 0.9f;
            if (label is Text legacy) return legacy.color.grayscale > 0.9f;
            return true;
        }

        public static void UpdateRecipe_Postfix(InventoryGui __instance)
        {
            if (!Plugin.SortByBiome.Value) return;
            try
            {
                object selPair = Traverse.Create(__instance).Field("m_selectedRecipe").GetValue();
                Recipe r = selPair as Recipe ?? PairRecipe(selPair);
                if (r == null) return;

                // półprodukty i uczty: co da gotowe danie
                if (!(Locked.TryGetValue(r, out LockReason lr) && lr == LockReason.Undiscovered && Plugin.HideUndiscoveredNames.Value))
                    AppendCookedStats(__instance, r);

                if (!Locked.TryGetValue(r, out LockReason reason))
                {
                    // znany przepis – jeśli stacja ma za niski poziom, dopisz czego brakuje
                    if (Plugin.ShowUnlockHints.Value)
                    {
                        int quality = selPair is Recipe ? 1 : PairQuality(selPair);
                        Player player = Player.m_localPlayer;
                        if (StationLevelTooLow(player, r, quality))
                        {
                            Component d = Traverse.Create(__instance).Field("m_recipeDecription").GetValue() as Component;
                            AppendHint(d, GetStationHint(player, r, quality), StationHintMarker);
                        }
                    }
                    return;
                }

                Traverse tr = Traverse.Create(__instance);
                Button craft = tr.Field("m_craftButton").GetValue<Button>();
                if (craft != null) craft.interactable = false;

                bool hide = reason == LockReason.Undiscovered && Plugin.HideUndiscoveredNames.Value;
                Component desc = tr.Field("m_recipeDecription").GetValue() as Component;

                if (hide)
                {
                    SetLabel(tr.Field("m_recipeName").GetValue() as Component, "???", null, bold: false);
                    Image icon = tr.Field("m_recipeIcon").GetValue<Image>();
                    if (icon != null) icon.color = HiddenIconColor;
                    SetLabel(desc, GetHint(r, reason, hide: true), null, bold: false);
                }
                else if (Plugin.ShowUnlockHints.Value)
                {
                    AppendHint(desc, GetHint(r, reason, hide: false));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Błąd w UpdateRecipe: {e}");
            }
        }

        // ------------------------------------------------------------------ podpowiedzi "jak odblokować"

        private static string HintMarker => Txt.T("Jak odblokować:", "How to unlock:");
        private static readonly Dictionary<Recipe, string> HintCache = new Dictionary<Recipe, string>();

        private static void AppendHint(Component desc, string hint, string marker = null)
        {
            marker = marker ?? HintMarker;
            if (desc == null || string.IsNullOrEmpty(hint)) return;
            string current = desc is TMP_Text tmp ? tmp.text : (desc is Text legacy ? legacy.text : null);
            if (current == null || current.Contains(marker)) return; // już dopisane
            SetLabel(desc, current + "\n\n" + hint, null, bold: false);
        }

        // ---- statystyki gotowego dania (półprodukty z blatu, uczty)

        private static string CookedMarker => Txt.T("Gotowe danie:", "Finished dish:");

        private static void AppendCookedStats(InventoryGui gui, Recipe r)
        {
            if (r.m_item == null) return;
            var cooked = Filters.CookedResult(r.m_item.gameObject.name);
            if (cooked == null || (cooked.m_food <= 0f && cooked.m_foodStamina <= 0f && cooked.m_foodEitr <= 0f)) return;
            // przedmiot ma własne statystyki jedzenia – gra już je pokazuje, nie dublujemy
            if (ReferenceEquals(cooked, r.m_item.m_itemData?.m_shared)) return;

            var sb = new System.Text.StringBuilder();
            sb.Append("<color=#FFC85C>").Append(CookedMarker).Append("</color> ").Append(Localize(cooked.m_name)).Append('\n');
            var own = r.m_item.m_itemData?.m_shared;
            if (own != null && (own.m_food > 0f || own.m_foodStamina > 0f || own.m_foodEitr > 0f))
                sb.Append(Txt.T("<i>(statystyki wyżej to dane półproduktu – po upieczeniu obowiązują poniższe)</i>\n",
                                "<i>(the stats above belong to the uncooked item – after baking these apply)</i>\n"));
            if (cooked.m_food > 0f) sb.Append(Txt.T("Zdrowie: ", "Health: ")).Append("<color=#FF8A80>").Append(Mathf.RoundToInt(cooked.m_food)).Append("</color>\n");
            if (cooked.m_foodStamina > 0f) sb.Append(Txt.T("Kondycja: ", "Stamina: ")).Append("<color=#FFE680>").Append(Mathf.RoundToInt(cooked.m_foodStamina)).Append("</color>\n");
            if (cooked.m_foodEitr > 0f) sb.Append(Txt.T("Eitr: ", "Eitr: ")).Append("<color=#9FC8FF>").Append(Mathf.RoundToInt(cooked.m_foodEitr)).Append("</color>\n");
            if (cooked.m_foodRegen > 0f) sb.Append(Txt.T("Regeneracja: ", "Healing: ")).Append(cooked.m_foodRegen.ToString("0.#")).Append(Txt.T(" pkt / tik\n", " hp/tick\n"));
            if (cooked.m_foodBurnTime > 0f) sb.Append(Txt.T("Czas działania: ", "Duration: ")).Append(Mathf.RoundToInt(cooked.m_foodBurnTime / 60f)).Append(" min");

            Component desc = Traverse.Create(gui).Field("m_recipeDecription").GetValue() as Component;
            AppendHint(desc, sb.ToString(), CookedMarker);
        }

        // ---- podpowiedź dla "za niski poziom stacji" (pomarańczowe)

        private static string StationHintMarker => Txt.T("Za niski poziom stacji:", "Station level too low:");
        private static readonly Dictionary<string, string> StationHintCache = new Dictionary<string, string>();

        private static string GetStationHint(Player player, Recipe r, int quality)
        {
            CraftingStation current = player != null ? player.GetCurrentCraftingStation() : null;
            if (current == null) return null;

            int have = current.GetLevel();
            int need = r.GetRequiredStationLevel(quality);
            string key = r.GetInstanceID() + ":" + quality + ":" + have;
            if (StationHintCache.TryGetValue(key, out string cached)) return cached;

            string text = "<color=" + Palette.ToHtml(Palette.StationLevel) + ">" + StationHintMarker + "</color>\n" + StationLevelDetails(current, have, need);
            StationHintCache[key] = text;
            return text;
        }

        /// <summary>"Kuźnia: potrzebny poziom X, teraz Y" + lista brakujących ulepszeń.</summary>
        private static string StationLevelDetails(CraftingStation current, int have, int need)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Localize(current.m_name)).Append(Txt.T(": potrzebny poziom ", ": required level ")).Append(need)
              .Append(Txt.T(", teraz ", ", now ")).Append(have).Append(Txt.T(" (brakuje ", " (missing ")).Append(need - have).Append(").");

            List<string> missing = MissingExtensions(current);
            int maxLevel = 1 + CountExtensions(current); // każde ulepszenie daje +1 poziom
            if (need > maxLevel)
            {
                sb.Append(Txt.T(
                    $"\nW tej wersji gry stacja ma maksymalnie poziom {maxLevel} – potrzebnego ulepszenia jeszcze nie ma w grze. " +
                    "Prawdopodobnie pojawi się w przyszłej aktualizacji.",
                    $"\nIn this game version the station goes up to level {maxLevel} – the required upgrade isn't in the game yet. " +
                    "It will probably arrive in a future update."));
            }
            else if (missing.Count > 0)
            {
                sb.Append(Txt.T("\nPostaw obok stacji ulepszenia, których jeszcze nie ma:\n", "\nBuild these upgrades next to the station:\n"));
                sb.Append(string.Join("\n", missing.Select(m => "• " + m).ToArray()));
            }
            else
            {
                sb.Append(Txt.T("\nMod nie znalazł brakującego ulepszenia – wszystkie, które zna, już stoją. " +
                                "Kolejny poziom może wymagać nowego elementu z aktualizacji gry albo ulepszenie stoi poza zasięgiem stacji.",
                                "\nThe mod found no missing upgrade – all the ones it knows are built. " +
                                "The next level may need a new piece from a game update, or an upgrade is out of the station's range."));
            }
            return sb.ToString();
        }

        /// <summary>Ulepszenia (StationExtension) pasujące do stacji, których przy niej jeszcze nie ma.</summary>
        private static List<string> MissingExtensions(CraftingStation station)
        {
            var result = new List<string>();
            ZNetScene scene = ZNetScene.instance;
            if (scene == null || scene.m_prefabs == null) return result;

            var attached = new HashSet<string>();
            if (Traverse.Create(station).Field("m_attachedExtensions").GetValue() is IEnumerable list)
            {
                foreach (object o in list)
                {
                    if (o is StationExtension ext && ext != null)
                    {
                        Piece p = ext.GetComponent<Piece>();
                        attached.Add(p != null ? p.m_name : ext.gameObject.name);
                    }
                }
            }

            var seen = new HashSet<string>();
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null) continue;
                StationExtension ext = prefab.GetComponent<StationExtension>();
                if (ext == null || ext.m_craftingStation == null) continue;
                if (ext.m_craftingStation.m_name != station.m_name) continue;

                Piece piece = prefab.GetComponent<Piece>();
                string token = piece != null ? piece.m_name : prefab.name;
                if (attached.Contains(token) || !seen.Add(token)) continue;
                result.Add(Localize(token));
            }

            LogExtensions(station, seen, attached);
            return result;
        }

        /// <summary>Ile różnych ulepszeń w ogóle istnieje w grze dla tej stacji.</summary>
        private static int CountExtensions(CraftingStation station)
        {
            ZNetScene scene = ZNetScene.instance;
            if (station == null || scene == null || scene.m_prefabs == null) return 0;
            var names = new HashSet<string>();
            foreach (GameObject prefab in scene.m_prefabs)
            {
                StationExtension ext = prefab != null ? prefab.GetComponent<StationExtension>() : null;
                if (ext == null || ext.m_craftingStation == null || ext.m_craftingStation.m_name != station.m_name) continue;
                Piece piece = prefab.GetComponent<Piece>();
                names.Add(piece != null ? piece.m_name : prefab.name);
            }
            return names.Count;
        }

        private static readonly HashSet<string> LoggedStations = new HashSet<string>();

        /// <summary>Jednorazowo zapisuje w logu, jakie ulepszenia mod zna dla stacji i które stoją (do diagnozy).</summary>
        private static void LogExtensions(CraftingStation station, HashSet<string> missingTokens, HashSet<string> attached)
        {
            if (station == null || !LoggedStations.Add(station.m_name)) return;
            try
            {
                var all = new List<string>();
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    StationExtension ext = prefab != null ? prefab.GetComponent<StationExtension>() : null;
                    if (ext == null || ext.m_craftingStation == null || ext.m_craftingStation.m_name != station.m_name) continue;
                    all.Add(prefab.name);
                }
                Plugin.Log.LogInfo($"[ulepszenia] {Localize(station.m_name)} (poziom {station.GetLevel()}): " +
                                   $"znane ulepszenia: {string.Join(", ", all.ToArray())}; " +
                                   $"postawione: {string.Join(", ", attached.Select(Localize).ToArray())}");
            }
            catch { }
        }

        private static string GetHint(Recipe r, LockReason reason, bool hide)
        {
            string key = (hide ? "H" : "N");
            if (HintCache.TryGetValue(r, out string cached) && cached.StartsWith(key)) return cached.Substring(1);

            string text = BuildHint(r, reason, hide);
            HintCache[r] = key + text;
            return text;
        }

        private static string BuildHint(Recipe r, LockReason reason, bool hide)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<color=#FFC85C>").Append(HintMarker).Append("</color>\n");

            CraftingStation station = r.GetRequiredStation(1);
            int stationLevel = 1;
            try { stationLevel = r.GetRequiredStationLevel(1); } catch { }
            string stationText = station != null
                ? Localize(station.m_name) + (stationLevel > 1 ? Txt.T($" (poziom {stationLevel})", $" (level {stationLevel})") : "")
                : null;

            if (reason == LockReason.OtherStation)
            {
                sb.Append(stationText != null ? Txt.T("Wytwórz przy: ", "Craft at: ") + stationText : Txt.T("Wytwórz bez stacji (z ekwipunku).", "Craft without a station (from inventory)."));
                return sb.ToString();
            }

            int biome = BiomeClassifier.GetBiome(r);
            if (hide)
            {
                sb.Append(Txt.T("Przedmiot z biomu: ", "Item from biome: ")).Append(BiomeClassifier.BiomeName(biome))
                  .Append(Txt.T(".\nZbieraj nowe materiały z tego biomu.", ".\nCollect new materials from this biome."));
                return sb.ToString();
            }

            HashSet<string> known = KnownMaterials(Player.m_localPlayer);
            var missing = new List<string>();
            var all = new List<string>();

            if (r.m_resources != null)
            {
                foreach (Piece.Requirement req in r.m_resources)
                {
                    if (req == null || req.m_resItem == null || req.m_amount <= 0) continue;
                    string token = req.m_resItem.m_itemData?.m_shared?.m_name;
                    if (string.IsNullOrEmpty(token)) continue;

                    string prefab = req.m_resItem.gameObject.name;
                    string where = BiomeClassifier.BiomeName(BiomeClassifier.MaterialBiome(prefab));
                    if (BiomeClassifier.IsCraftable(prefab)) where = Txt.T("wytwórz, ", "craft, ") + where;
                    string line = "• " + Localize(token) + "  <color=#B0B0B0>(" + where + ")</color>";

                    all.Add(line);
                    if (known == null || !known.Contains(token)) missing.Add(line);
                }
            }

            if (known == null)
            {
                sb.Append(Txt.T("Zdobądź (podnieś) każdy z materiałów:\n", "Pick up each of these materials:\n")).Append(string.Join("\n", all.ToArray()));
            }
            else if (missing.Count > 0)
            {
                sb.Append(Txt.T("Podnieś choć raz:\n", "Pick up at least once:\n")).Append(string.Join("\n", missing.ToArray()));
            }
            else
            {
                sb.Append(Txt.T("Materiały już znasz – brakuje wymaganej stacji.", "You know all materials – the required station is missing."));
            }

            if (stationText != null)
                sb.Append(Txt.T("\nStacja: ", "\nStation: ")).Append(stationText);

            // jeśli dodatkowo obecna stacja ma za niski poziom – dopisz brakujące ulepszenia
            Player player = Player.m_localPlayer;
            if (StationLevelTooLow(player, r, 1))
            {
                CraftingStation current = player.GetCurrentCraftingStation();
                sb.Append("\n\n<color=" + Palette.ToHtml(Palette.StationLevel) + ">").Append(StationHintMarker).Append("</color>\n")
                  .Append(StationLevelDetails(current, current.GetLevel(), stationLevel));
            }

            return sb.ToString();
        }

        private static HashSet<string> KnownMaterials(Player player)
        {
            if (player == null) return null;
            object raw = Traverse.Create(player).Field("m_knownMaterial").GetValue();
            if (raw is HashSet<string> set) return set;
            if (raw is IEnumerable<string> seq) return new HashSet<string>(seq);
            return null;
        }

        public static bool BlockLocked_Prefix(InventoryGui __instance)
        {
            Recipe r = SelectedRecipe(__instance);
            if (r == null || !Locked.TryGetValue(r, out LockReason reason)) return true;

            string msg = reason == LockReason.Undiscovered
                ? Txt.T("Najpierw odkryj ten przepis", "Discover this recipe first")
                : Txt.T("Ten przepis wymaga innej stacji", "This recipe needs a different station");
            if (MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, msg);
            return false;
        }

        // ------------------------------------------------------------------ pomocnicze

        internal static bool InCraftTab(InventoryGui gui) => IsCraftTab(gui);
        internal static Recipe GetSelectedRecipe(InventoryGui gui) => SelectedRecipe(gui);

        private static bool IsCraftTab(InventoryGui gui)
        {
            if (_inCraftTab == null) return true;
            try { return (bool)_inCraftTab.Invoke(gui, null); }
            catch { return true; }
        }

        private static Recipe SelectedRecipe(InventoryGui gui)
        {
            object sel = Traverse.Create(gui).Field("m_selectedRecipe").GetValue();
            if (sel is Recipe direct) return direct;
            return PairRecipe(sel);
        }

        private static Recipe PairRecipe(object pair)
        {
            if (pair == null) return null;
            Traverse t = Traverse.Create(pair);
            return (t.Property("Recipe").GetValue() as Recipe)
                ?? (t.Property("Key").GetValue() as Recipe)
                ?? (t.Field("m_recipe").GetValue() as Recipe);
        }

        private static GameObject PairElement(object pair)
        {
            if (pair == null) return null;
            Traverse t = Traverse.Create(pair);
            return (t.Property("InterfaceElement").GetValue() as GameObject)
                ?? (t.Field("m_interfaceElement").GetValue() as GameObject);
        }

        private static Vector2 Pos(GameObject go)
        {
            return go != null && go.transform is RectTransform rt ? rt.anchoredPosition : Vector2.zero;
        }

        private static string Localize(string token)
        {
            return Localization.instance != null ? Localization.instance.Localize(token) : token;
        }

        /// <summary>Szuka etykiety z nazwą (TextMeshPro albo stary UI.Text).</summary>
        private static Component FindLabel(GameObject go)
        {
            Transform name = go.transform.Find("name");
            if (name != null)
            {
                Component c = (Component)name.GetComponent<TMP_Text>() ?? name.GetComponent<Text>();
                if (c != null) return c;
            }
            return (Component)go.GetComponentInChildren<TMP_Text>(true) ?? go.GetComponentInChildren<Text>(true);
        }

        private static void SetLabel(Component label, string text, Color? color, bool bold)
        {
            if (label is TMP_Text tmp)
            {
                if (text != null) tmp.text = text;
                if (color.HasValue) tmp.color = color.Value;
                if (bold) tmp.fontStyle |= FontStyles.Bold;
            }
            else if (label is Text legacy)
            {
                if (text != null) legacy.text = text;
                if (color.HasValue) legacy.color = color.Value;
                if (bold) legacy.fontStyle = FontStyle.Bold;
            }
        }
    }
}
