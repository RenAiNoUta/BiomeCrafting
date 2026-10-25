using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BiomeCrafting
{
    /// <summary>
    /// Lista materiałów do zebrania: do 5 przedmiotów (każdy w dowolnej liczbie sztuk),
    /// suma materiałów i porównanie z zawartością plecaka. Widoczna przy otwartym ekwipunku,
    /// także przy skrzyni. Nie jest zapisywana między sesjami.
    /// </summary>
    internal static class ShoppingList
    {
        public const int MaxItems = 5;
        private const float PanelWidth = 360f;
        private const float RowH = 26f;

        private class Entry
        {
            public Recipe Recipe;
            public int Count;
        }

        private struct Need
        {
            public string Token;
            public Sprite Icon;
            public int Amount;
        }

        private static readonly List<Entry> Items = new List<Entry>();
        private static ObjectDB _db;
        private static Vector2 _scroll;

        // ------------------------------------------------------------------ dane

        public static bool Contains(Recipe r) => Items.Exists(e => e.Recipe == r);

        public static void Add(Recipe r)
        {
            if (r == null) return;
            Entry e = Items.Find(x => x.Recipe == r);
            if (e != null) { e.Count++; return; }
            if (Items.Count >= MaxItems)
            {
                Message(Txt.T($"Lista jest pełna (maks. {MaxItems} przedmiotów)", $"The list is full (max {MaxItems} items)"));
                return;
            }
            Items.Add(new Entry { Recipe = r, Count = 1 });
        }

        private static void Message(string text)
        {
            if (MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, text);
        }

        // ------------------------------------------------------------------ rysowanie

        public static void OnGUI(InventoryGui gui)
        {
            if (!Plugin.ShowShoppingList.Value) return;

            if (ObjectDB.instance != _db) // nowy świat / nowa sesja – czyścimy
            {
                _db = ObjectDB.instance;
                Items.Clear();
            }

            float scale = Ui.Scale;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            DrawAddButton(gui, scale);
            if (Items.Count > 0) DrawList(gui, scale);

            GUI.matrix = old;
        }

        /// <summary>Przycisk "+ Lista" w panelu szczegółów przepisu (tylko zakładka Twórz).</summary>
        private static void DrawAddButton(InventoryGui gui, float scale)
        {
            RectTransform crafting = FilterPanel.FindCraftingRect(gui);
            if (crafting == null || !crafting.gameObject.activeInHierarchy) return;
            if (!CraftingListPatch.InCraftTab(gui)) return;

            Recipe r = CraftingListPatch.GetSelectedRecipe(gui);
            if (r == null || r.m_item == null) return;

            Traverse tr = Traverse.Create(gui);
            Button craft = tr.Field("m_craftButton").GetValue<Button>();
            Image icon = tr.Field("m_recipeIcon").GetValue<Image>();
            RectTransform panel = craft != null ? craft.transform.parent as RectTransform : null;
            if (panel == null || icon == null) return;

            Rect p = Ui.ScreenRect(panel);
            Rect ic = Ui.ScreenRect((RectTransform)icon.transform);

            // mały kwadrat "+" w lewym górnym rogu ikony przedmiotu – nie zasłania opisu
            const float size = 24f;
            float x = ic.xMin / scale - size * 0.35f;
            float y = ic.yMin / scale - size * 0.35f;

            bool full = !Contains(r) && Items.Count >= MaxItems;
            var content = new GUIContent("+", full
                ? Txt.T($"Lista pełna (maks. {MaxItems})", $"List full (max {MaxItems})")
                : Txt.T("Dodaj do listy materiałów", "Add to materials list"));

            GUI.enabled = !full;
            if (GUI.Button(new Rect(x, y, size, size), content, Ui.SmallButton)) Add(r);
            GUI.enabled = true;
        }

        private static void DrawList(InventoryGui gui, float scale)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            Dictionary<string, int> have = CountInventory(player);

            // suma materiałów (kolejność jak w przepisach)
            var totals = new List<Need>();
            var index = new Dictionary<string, int>();
            foreach (Entry e in Items)
                foreach (Need n in NeedsOf(e))
                {
                    if (index.TryGetValue(n.Token, out int i))
                    {
                        Need t = totals[i]; t.Amount += n.Amount; totals[i] = t;
                    }
                    else
                    {
                        index[n.Token] = totals.Count;
                        totals.Add(n);
                    }
                }

            float screenW = Screen.width / scale, screenH = Screen.height / scale;
            float h = Mathf.Min(screenH - 140f, 110f + Items.Count * (RowH + 4f) + totals.Count * (RowH + 2f) + 40f);
            var area = PanelRect(gui, scale, screenW, screenH, h);

            // stałe szerokości kolumn – długie nazwy są przycinane, przyciski zawsze widoczne
            // pasek przewijania tylko, gdy treść się nie mieści – i wtedy rezerwujemy na niego miejsce
            float contentH = Items.Count * (RowH + 4f) + 6f + 30f + totals.Count * (RowH + 2f) + 6f;
            float availableH = area.height - 20f /*padding*/ - 36f /*tytuł*/;
            bool needScroll = contentH > availableH;
            float scrollW = needScroll ? Mathf.Max(GUI.skin.verticalScrollbar.fixedWidth, 16f) + 12f : 0f;
            float inner = PanelWidth - 24f /*padding*/ - scrollW;
            float itemNameW = inner - (RowH + 8f + RowH + 2f + RowH + 4f + 34f + RowH + 4f + 6f);
            float countW = 72f;
            float matNameW = inner - (RowH + 4f + countW + 4f);

            GUILayout.BeginArea(area, Ui.Box);

            GUILayout.BeginHorizontal();
            GUILayout.Label(Txt.T("Do zebrania", "To gather"), Ui.Title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Txt.T("Wyczyść", "Clear"), Ui.Button, GUILayout.Width(80f), GUILayout.Height(24f)))
                Items.Clear();
            GUILayout.EndHorizontal();

            if (needScroll)
                _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            else
            {
                _scroll = Vector2.zero;
                GUILayout.BeginVertical();
            }

            // --- przedmioty
            Entry remove = null;
            foreach (Entry e in Items.ToArray())
            {
                var shared = e.Recipe.m_item.m_itemData.m_shared;
                bool canCraftAll = true;
                foreach (Need n in NeedsOf(e))
                    if (Have(have, n.Token) < n.Amount) { canCraftAll = false; break; }

                GUILayout.BeginHorizontal(GUILayout.Height(RowH));

                // kosz (z odstępem), potem minus i plus, dalej nazwa
                if (GUILayout.Button(new GUIContent(Ui.TrashTex), Ui.IconButton, GUILayout.Width(RowH), GUILayout.Height(RowH)))
                    remove = e;
                GUILayout.Space(8f);
                if (GUILayout.Button("-", Ui.SmallButton, GUILayout.Width(RowH), GUILayout.Height(RowH)))
                {
                    e.Count--;
                    if (e.Count <= 0) remove = e;
                }
                GUILayout.Space(2f);
                if (GUILayout.Button("+", Ui.SmallButton, GUILayout.Width(RowH), GUILayout.Height(RowH)))
                    e.Count++;

                GUILayout.Space(4f);
                GUILayout.Label(e.Count + " ×", Ui.LabelRight, GUILayout.Width(34f), GUILayout.Height(RowH));
                Rect iconRect = GUILayoutUtility.GetRect(RowH, RowH, GUILayout.Width(RowH), GUILayout.Height(RowH));
                Ui.DrawSprite(iconRect, shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null, Color.white);

                Color old = Ui.Label.normal.textColor;
                Ui.Label.normal.textColor = canCraftAll ? Palette.Enough : Ui.Text;
                GUILayout.Space(4f);
                GUILayout.Label(Localize(shared.m_name), Ui.Label, GUILayout.Width(itemNameW), GUILayout.Height(RowH));
                Ui.Label.normal.textColor = old;
                GUILayout.Space(2f);


                GUILayout.EndHorizontal();
                GUILayout.Space(4f);
            }
            if (remove != null) Items.Remove(remove);

            // --- suma
            GUILayout.Space(6f);
            GUILayout.Label(Txt.T("Suma materiałów", "Total materials"), Ui.Section);
            foreach (Need n in totals)
            {
                int got = Have(have, n.Token);
                GUILayout.BeginHorizontal(GUILayout.Height(RowH));
                Rect ir = GUILayoutUtility.GetRect(RowH, RowH, GUILayout.Width(RowH), GUILayout.Height(RowH));
                Ui.DrawSprite(ir, n.Icon, Color.white);
                GUILayout.Space(4f);
                GUILayout.Label(Localize(n.Token), Ui.Label, GUILayout.Width(matNameW), GUILayout.Height(RowH));

                Color old = Ui.LabelRight.normal.textColor;
                Ui.LabelRight.normal.textColor = got >= n.Amount ? Palette.Enough : Palette.NotEnough;
                GUILayout.Label($"{got} / {n.Amount}", Ui.LabelRight, GUILayout.Width(countW), GUILayout.Height(RowH));
                Ui.LabelRight.normal.textColor = old;
                GUILayout.EndHorizontal();
                GUILayout.Space(2f);
            }

            if (needScroll) GUILayout.EndScrollView();
            else GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        /// <summary>
        /// Położenie panelu: najpierw róg z configu (domyślnie lewy dolny), a jeśli zasłaniałby
        /// okno skrzyni, ekwipunku albo craftingu – pierwszy wolny róg ekranu. Gdy żaden nie jest wolny,
        /// panel ląduje tuż pod oknem skrzyni albo obok niego.
        /// </summary>
        private static Rect PanelRect(InventoryGui gui, float scale, float screenW, float screenH, float h)
        {
            const float margin = 20f;
            List<Rect> avoid = GameWindows(gui, scale);

            string pos = (Plugin.ShoppingListPosition.Value ?? "").Trim().ToLowerInvariant();
            bool right = pos.Contains("praw") || pos.Contains("right");
            bool top = pos.Contains("gora") || pos.Contains("góra") || pos.Contains("top");

            Rect Corner(bool r, bool t) => new Rect(r ? screenW - PanelWidth - margin : margin,
                                                    t ? 90f : screenH - h - margin, PanelWidth, h);

            var candidates = new List<Rect>
            {
                Corner(right, top), Corner(false, false), Corner(true, false), Corner(true, true), Corner(false, true)
            };
            foreach (Rect c in candidates)
                if (!avoid.Exists(a => a.Overlaps(c))) return c;

            // brak wolnego rogu – szukamy miejsca pod oknami gry, potem obok nich
            float lowest = 0f, rightmost = 0f;
            foreach (Rect a in avoid) { lowest = Mathf.Max(lowest, a.yMax); rightmost = Mathf.Max(rightmost, a.xMax); }

            float spaceBelow = screenH - lowest - margin - 10f;
            if (spaceBelow >= 160f)
                return new Rect(margin, lowest + 10f, PanelWidth, Mathf.Min(h, spaceBelow));
            if (screenW - rightmost - margin - 10f >= PanelWidth)
                return new Rect(rightmost + 10f, screenH - h - margin, PanelWidth, h);

            return candidates[0];
        }

        /// <summary>Prostokąty widocznych okien gry (skrzynia, ekwipunek, crafting) w przestrzeni panelu.</summary>
        private static List<Rect> GameWindows(InventoryGui gui, float scale)
        {
            var list = new List<Rect>();
            Traverse tr = Traverse.Create(gui);
            foreach (string field in new[] { "m_container", "m_player" })
            {
                if (tr.Field(field).GetValue() is RectTransform rt && rt.gameObject.activeInHierarchy)
                    list.Add(ToPanelSpace(Ui.ScreenRect(rt), scale));
            }
            RectTransform crafting = FilterPanel.FindCraftingRect(gui);
            if (crafting != null && crafting.gameObject.activeInHierarchy)
                list.Add(ToPanelSpace(Ui.ScreenRect(crafting), scale));
            return list;
        }

        private static Rect ToPanelSpace(Rect r, float scale) =>
            new Rect(r.x / scale, r.y / scale, r.width / scale, r.height / scale);

        // ------------------------------------------------------------------ pomocnicze

        private static IEnumerable<Need> NeedsOf(Entry e)
        {
            if (e.Recipe == null || e.Recipe.m_resources == null) yield break;
            foreach (Piece.Requirement req in e.Recipe.m_resources)
            {
                if (req == null || req.m_resItem == null || req.m_amount <= 0) continue;
                var sh = req.m_resItem.m_itemData?.m_shared;
                if (sh == null) continue;
                yield return new Need
                {
                    Token = sh.m_name,
                    Icon = sh.m_icons != null && sh.m_icons.Length > 0 ? sh.m_icons[0] : null,
                    Amount = req.m_amount * e.Count
                };
            }
        }

        private static Dictionary<string, int> CountInventory(Player player)
        {
            var result = new Dictionary<string, int>();
            Inventory inv = player.GetInventory();
            if (inv == null) return result;
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                string token = item?.m_shared?.m_name;
                if (token == null) continue;
                result.TryGetValue(token, out int n);
                result[token] = n + item.m_stack;
            }
            return result;
        }

        private static int Have(Dictionary<string, int> have, string token) =>
            have.TryGetValue(token, out int n) ? n : 0;

        private static string Localize(string token) =>
            Localization.instance != null ? Localization.instance.Localize(token) : token;
    }
}
