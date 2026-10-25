using System.Linq;
using UnityEngine;

namespace BiomeCrafting
{
    /// <summary>
    /// Wspólny wygląd paneli moda (IMGUI) w stylu Valheim: czcionka gry, ciemne drewno ze złotą ramką,
    /// własne checkboxy i przyciski. Plus rysowanie ikon przedmiotów.
    /// </summary>
    internal static class Ui
    {
        public const float RefHeight = 1080f;

        public static GUIStyle Box, Title, Section, Toggle, Button, SmallButton, Hint, Label, LabelRight, IconButton;
        public static Texture2D TrashTex, CheckOff, CheckOn;
        private static bool _ready;
        private static Font _font;

        public static readonly Color Gold = new Color(1f, 0.78f, 0.36f, 1f);
        public static readonly Color Text = new Color(0.93f, 0.89f, 0.80f, 1f);
        public static readonly Color Muted = new Color(0.70f, 0.66f, 0.58f, 1f);

        public static float Scale =>
            Mathf.Max(0.6f, Screen.height / RefHeight) * Mathf.Clamp(Plugin.FilterPanelScale.Value, 0.5f, 2f);

        public static void Ensure()
        {
            if (_ready) return;
            _ready = true;

            _font = FindGameFont();

            Color wood = new Color(0.17f, 0.12f, 0.08f, 0.94f);
            Color border = new Color(0.55f, 0.40f, 0.20f, 1f);

            Box = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 10, 10),
                border = new RectOffset(4, 4, 4, 4)
            };
            Box.normal.background = FramedTex(32, wood, border, 2);

            Title = Make(GUI.skin.label, 20, FontStyle.Bold, Gold);
            Section = Make(GUI.skin.label, 15, FontStyle.Bold, Gold);
            Section.margin = new RectOffset(0, 0, 6, 2);
            Label = Make(GUI.skin.label, 14, FontStyle.Normal, Text);
            Label.wordWrap = false;
            Label.alignment = TextAnchor.MiddleLeft;
            LabelRight = new GUIStyle(Label) { alignment = TextAnchor.MiddleRight };
            Hint = Make(GUI.skin.label, 11, FontStyle.Italic, Muted);
            Hint.wordWrap = true;

            // checkbox rysujemy sami (Ui.Toggle), styl służy tylko do tekstu
            Toggle = Make(GUI.skin.label, 14, FontStyle.Normal, Text);
            Toggle.padding = new RectOffset(22, 2, 2, 2);
            Toggle.alignment = TextAnchor.MiddleLeft;
            Toggle.wordWrap = false;
            CheckOff = CheckTex(false, border);
            CheckOn = CheckTex(true, border);

            Button = Make(GUI.skin.button, 13, FontStyle.Bold, Text);
            Button.normal.background = FramedTex(16, new Color(0.30f, 0.21f, 0.12f, 1f), border, 1);
            Button.hover.background = FramedTex(16, new Color(0.42f, 0.29f, 0.15f, 1f), Gold, 1);
            Button.active.background = Button.hover.background;
            Button.border = new RectOffset(3, 3, 3, 3);
            Button.hover.textColor = Color.white;

            SmallButton = new GUIStyle(Button) { fontSize = 14, padding = new RectOffset(0, 0, 0, 2) };
            IconButton = new GUIStyle(Button) { padding = new RectOffset(3, 3, 3, 3) };
            IconButton.normal.background = FramedTex(16, new Color(0.22f, 0.14f, 0.10f, 1f), new Color(0.40f, 0.28f, 0.16f, 1f), 1);

            TrashTex = MakeTrashTex();
        }

        private static GUIStyle Make(GUIStyle baseStyle, int size, FontStyle fs, Color c)
        {
            var s = new GUIStyle(baseStyle) { fontSize = size, fontStyle = fs };
            if (_font != null) s.font = _font;
            s.normal.textColor = c;
            return s;
        }

        /// <summary>Czcionka gry (Averia / Norse), jeśli jest dostępna jako zwykła czcionka Unity.</summary>
        private static Font FindGameFont()
        {
            try
            {
                Font[] fonts = Resources.FindObjectsOfTypeAll<Font>();
                return fonts.FirstOrDefault(f => f != null && f.name.ToLowerInvariant().Contains("averiaseriflibre-bold"))
                    ?? fonts.FirstOrDefault(f => f != null && f.name.ToLowerInvariant().Contains("averia"));
            }
            catch { return null; }
        }

        /// <summary>Checkbox w stylu moda: kwadracik + tekst; kolor tekstu zielony (lub niebieski), gdy zaznaczony.</summary>
        public static bool DoToggle(bool on, string text, params GUILayoutOption[] options)
        {
            var content = new GUIContent(text);
            Rect r = GUILayoutUtility.GetRect(content, Toggle, options);
            bool hover = r.Contains(Event.current.mousePosition);

            GUI.DrawTexture(new Rect(r.x + 1, r.y + (r.height - 16f) / 2f, 16f, 16f), on ? CheckOn : CheckOff);

            Color old = Toggle.normal.textColor;
            Toggle.normal.textColor = on ? Palette.ToggleOn : (hover ? Color.white : Text);
            GUI.Label(r, content, Toggle);
            Toggle.normal.textColor = old;

            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) return !on;
            return on;
        }

        // ------------------------------------------------------------------ ikony

        /// <summary>Rysuje sprite (także z atlasu) w prostokącie IMGUI.</summary>
        public static void DrawSprite(Rect r, Sprite s, Color tint)
        {
            if (s == null || s.texture == null) return;
            Rect tr = s.textureRect;
            var uv = new Rect(tr.x / s.texture.width, tr.y / s.texture.height,
                              tr.width / s.texture.width, tr.height / s.texture.height);
            Color old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(r, s.texture, uv, true);
            GUI.color = old;
        }

        // ------------------------------------------------------------------ tekstury

        private static Texture2D NewTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            return t;
        }

        private static Texture2D FramedTex(int size, Color fill, Color border, int bw)
        {
            Texture2D t = NewTex(size, size);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool edge = x < bw || y < bw || x >= size - bw || y >= size - bw;
                    t.SetPixel(x, y, edge ? border : fill);
                }
            t.Apply();
            return t;
        }

        private static Texture2D CheckTex(bool on, Color border)
        {
            const int s = 16;
            Texture2D t = NewTex(s, s);
            Color clear = new Color(0, 0, 0, 0);
            Color box = new Color(0.12f, 0.08f, 0.05f, 1f);
            Color mark = Palette.ToggleOn;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    Color c = clear;
                    if (x >= 1 && x <= 14 && y >= 1 && y <= 14)
                    {
                        bool edge = x == 1 || x == 14 || y == 1 || y == 14;
                        c = edge ? border : box;
                        if (on && x >= 4 && x <= 11 && y >= 4 && y <= 11) c = mark;
                    }
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        private static Texture2D MakeTrashTex()
        {
            // proste piksele kosza 16x16 (y od dołu)
            string[] art =
            {
                "................",
                "......####......",
                "..############..",
                "..############..",
                "................",
                "...##########...",
                "...#.#.##.#.#...",
                "...#.#.##.#.#...",
                "...#.#.##.#.#...",
                "...#.#.##.#.#...",
                "...#.#.##.#.#...",
                "...#.#.##.#.#...",
                "...#.#.##.#.#...",
                "....########....",
                "................",
                "................",
            };
            Texture2D t = NewTex(16, 16);
            Color c = new Color(0.85f, 0.75f, 0.65f, 1f);
            for (int row = 0; row < 16; row++)
                for (int x = 0; x < 16; x++)
                    t.SetPixel(x, 15 - row, art[row][x] == '#' ? c : new Color(0, 0, 0, 0));
            t.Apply();
            return t;
        }

        /// <summary>Prostokąt elementu UI gry w pikselach ekranu (oś Y od góry, jak w IMGUI).</summary>
        public static Rect ScreenRect(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Canvas canvas = rt.GetComponentInParent<Canvas>();
            if (canvas != null) canvas = canvas.rootCanvas;
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 tr = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            return new Rect(bl.x, Screen.height - tr.y, tr.x - bl.x, tr.y - bl.y);
        }
    }

    /// <summary>
    /// Kolory stanów. W normalnym trybie z configu (bez zmian), w trybie dla daltonistów
    /// paleta Okabe-Ito, rozróżnialna przy najczęstszych rodzajach daltonizmu.
    /// </summary>
    internal static class Palette
    {
        private static bool CB => Plugin.ColorblindMode.Value;

        private static Color Cfg(string html, Color fallback) =>
            !string.IsNullOrWhiteSpace(html) && ColorUtility.TryParseHtmlString(html.Trim(), out Color c) ? c : fallback;

        private static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out Color c) ? c : Color.white;

        public static Color Craftable => CB ? Hex("#56B4E9") : Cfg(Plugin.CraftableColor.Value, Hex("#8CE68C"));
        public static Color MissingMaterials => CB ? Hex("#F0E442") : Cfg(Plugin.MissingMaterialsColor.Value, Hex("#FFF28C"));
        public static Color StationLevel => CB ? Hex("#E69F00") : Cfg(Plugin.StationLevelColor.Value, Hex("#FFA04D"));
        public static Color Undiscovered => CB ? Hex("#CC79A7") : Cfg(Plugin.UndiscoveredColor.Value, Hex("#EB7366"));
        public static Color OtherStation => CB ? Hex("#BBBBBB") : Cfg(Plugin.OtherStationColor.Value, Hex("#C8A0FF"));

        /// <summary>Lista materiałów: masz dość / brakuje.</summary>
        public static Color Enough => CB ? Hex("#56B4E9") : Hex("#8CE68C");
        public static Color NotEnough => CB ? Hex("#E69F00") : Hex("#FF8A80");

        public static Color ToggleOn => CB ? Hex("#56B4E9") : Hex("#8CE68C");

        public static string ToHtml(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
