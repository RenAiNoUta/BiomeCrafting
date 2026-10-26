using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BiomeCrafting
{
    /// <summary>
    /// Panel filtrów rysowany obok okna craftingu (Unity IMGUI) + host dla listy materiałów.
    /// Panel filtrów widoczny tylko przy otwartym panelu craftingu.
    /// </summary>
    internal class FilterPanel : MonoBehaviour
    {
        private const float BaseWidth = 270f;
        private Vector2 _scroll;

        private void OnGUI()
        {
            if (!Plugin.SortByBiome.Value) return;
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || !InventoryGui.IsVisible()) return;

            Ui.Ensure();

            if (Plugin.ShowFilterPanel.Value)
            {
                RectTransform anchor = FindCraftingRect(gui);
                if (anchor != null && anchor.gameObject.activeInHierarchy)
                    DrawPanel(anchor);
            }

            ShoppingList.OnGUI(gui);
        }

        private void DrawPanel(RectTransform anchor)
        {
            float scale = Ui.Scale;
            Rect craft = Ui.ScreenRect(anchor);

            float w = BaseWidth;
            float x = craft.xMin / scale - w - 10f;
            if (x < 4f) x = 4f;
            float y = craft.yMin / scale;
            float h = Mathf.Max(200f, craft.height / scale);

            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            GUILayout.BeginArea(new Rect(x, y, w, h), Ui.Box);
            DrawContent();
            GUILayout.EndArea();
            GUI.matrix = old;
        }

        private void DrawContent()
        {
            bool changed = false;

            GUILayout.BeginHorizontal();
            GUILayout.Label(Txt.T("Filtry", "Filters"), Ui.Title);
            GUILayout.FlexibleSpace();
            if (Filters.AnyActive && GUILayout.Button(Txt.T("Wyczyść", "Clear"), Ui.Button, GUILayout.Width(80f), GUILayout.Height(24f)))
            {
                Filters.ClearAll();
                changed = true;
            }
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);

            // --- widok (to nie filtr – przycisk "Wyczyść" go nie zmienia)
            GUILayout.Label(Txt.T("Pokaż", "Show"), Ui.Section);
            bool showUnd = Plugin.ShowUndiscovered.Value;
            bool nowUnd = Ui.DoToggle(showUnd, Txt.T("Nienauczone przepisy", "Unlearned recipes"));
            if (nowUnd != showUnd)
            {
                Plugin.ShowUndiscovered.Value = nowUnd;
                changed = true;
            }

            GUILayout.Space(6f);

            // --- biomy
            GUILayout.Label(Txt.T("Biom", "Biome"), Ui.Section);
            for (int b = 0; b < BiomeClassifier.BiomeCount; b++)
            {
                bool on = Filters.IsBiomeOn(b);
                bool now = Ui.DoToggle(on, BiomeClassifier.BiomeName(b));
                if (now != on)
                {
                    Filters.SetBiome(b, now);
                    if (now) CraftingListPatch.ExpandBiome(b); // zaznaczony biom ma być widoczny
                    changed = true;
                }
            }

            // --- kategorie (dwie kolumny)
            GUILayout.Space(6f);
            GUILayout.Label(Txt.T("Kategoria", "Category"), Ui.Section);
            var cats = Filters.Categories;
            for (int i = 0; i < cats.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                changed |= CategoryToggle(cats[i]);
                if (i + 1 < cats.Length) changed |= CategoryToggle(cats[i + 1]);
                else GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // --- jedzenie
            GUILayout.Space(6f);
            GUILayout.Label(Txt.T("Jedzenie – statystyka", "Food – main stat"), Ui.Section);
            foreach (var opt in Filters.FoodStats)
            {
                bool on = Filters.IsFoodOn(opt.Key);
                bool now = Ui.DoToggle(on, opt.Label);
                if (now != on) { Filters.SetFood(opt.Key, now); changed = true; }
            }
            GUILayout.Label(Txt.T("Zaznacz kilka, żeby pokazać jedzenie, które daje wszystkie naraz.",
                                  "Select several to show food that gives all of them."), Ui.Hint);

            // --- miody
            GUILayout.Space(6f);
            GUILayout.Label(Txt.T("Miody – efekt", "Meads – effect"), Ui.Section);
            var meads = Filters.MeadEffects;
            for (int i = 0; i < meads.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                changed |= MeadToggle(meads[i]);
                if (i + 1 < meads.Length) changed |= MeadToggle(meads[i + 1]);
                else GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.Label(Txt.T("Pokazuje miody i brzeczki z dowolnym z zaznaczonych efektów.",
                                  "Shows meads and mead bases with any of the selected effects."), Ui.Hint);

            GUILayout.EndScrollView();

            if (changed) CraftingListPatch.RefreshPanel();
        }

        private static float ColW => (BaseWidth - 44f) / 2f;

        private bool MeadToggle(Filters.Option opt)
        {
            bool on = Filters.IsMeadOn(opt.Key);
            bool now = Ui.DoToggle(on, opt.Label, GUILayout.Width(ColW));
            if (now == on) return false;
            Filters.SetMead(opt.Key, now);
            return true;
        }

        private bool CategoryToggle(Filters.Option opt)
        {
            bool on = Filters.IsCategoryOn(opt.Key);
            bool now = Ui.DoToggle(on, opt.Label, GUILayout.Width(ColW));
            if (now == on) return false;
            Filters.SetCategory(opt.Key, now);
            return true;
        }

        internal static RectTransform FindCraftingRect(InventoryGui gui)
        {
            Traverse tr = Traverse.Create(gui);
            RectTransform crafting = tr.Field("m_crafting").GetValue() as RectTransform;
            if (crafting != null) return crafting;

            RectTransform listRoot = tr.Field("m_recipeListRoot").GetValue() as RectTransform;
            if (listRoot == null) return null;
            ScrollRect sr = listRoot.GetComponentInParent<ScrollRect>();
            return sr != null ? (RectTransform)sr.transform : listRoot;
        }
    }
}
