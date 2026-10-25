using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BiomeCrafting
{
    internal enum Category { Weapon, Shield, Armor, Ammo, Tool, Food, Potion, Utility, Material, Other }

    /// <summary>
    /// Wylicza biom przepisu na podstawie najlepszego materiału (rekurencyjnie: brąz -> miedź + cyna)
    /// oraz wymaganej stacji. Ręczne przypisania z configu mają pierwszeństwo.
    /// </summary>
    internal static class BiomeClassifier
    {
        public const int BiomeCount = 8;

        public static readonly string[] BiomeNamesPl =
        {
            "Łąki", "Czarny Las", "Bagna", "Góry", "Równiny", "Mgliste Ziemie", "Popielne Ziemie", "Głęboka Północ"
        };

        private static readonly string[] BiomeNamesEn =
        {
            "Meadows", "Black Forest", "Swamp", "Mountain", "Plains", "Mistlands", "Ashlands", "Deep North"
        };

        private static readonly string[] BiomeTokens =
        {
            "$biome_meadows", "$biome_blackforest", "$biome_swamp", "$biome_mountain",
            "$biome_plains", "$biome_mistlands", "$biome_ashlands", "$biome_deepnorth"
        };

        private static readonly string[] BiomeNameCache = new string[BiomeCount];

        /// <summary>Nazwa biomu z tłumaczenia gry (tak jak na mapie), z polską nazwą zapasową.</summary>
        public static string BiomeName(int i)
        {
            if (i < 0 || i >= BiomeCount) return "?";
            if (BiomeNameCache[i] != null) return BiomeNameCache[i];

            string name = Txt.Polish ? BiomeNamesPl[i] : BiomeNamesEn[i];
            if (Localization.instance != null)
            {
                string loc = Localization.instance.Localize(BiomeTokens[i]);
                if (!string.IsNullOrEmpty(loc) && !loc.StartsWith("[") && !loc.StartsWith("$"))
                {
                    name = loc;
                    BiomeNameCache[i] = name; // zapamiętaj tylko prawdziwe tłumaczenie
                }
            }
            return name;
        }

        /// <summary>Czy znamy pochodzenie materiału (z tabeli albo przez przepis, który go tworzy).</summary>
        public static bool IsKnownMaterial(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return false;
            EnsureState();
            return _overrides.ContainsKey(prefab) || BaseTiers.ContainsKey(prefab) || Producers.ContainsKey(prefab)
                || IsUpgradeStatue(prefab, out _) || TryTierFromName(prefab, out _)
                || prefab.StartsWith("Mold", StringComparison.OrdinalIgnoreCase)
                || AutoBiomes.TryGet(prefab, out _);
        }

        private static readonly Dictionary<string, int> BaseTiers =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        static BiomeClassifier()
        {
            Add(0, "Wood", "Stone", "Flint", "Resin", "LeatherScraps", "DeerHide", "Feathers", "HardAntler",
                "Raspberry", "Mushroom", "Honey", "NeckTail", "Dandelion", "RawMeat", "DeerMeat", "QueenBee",
                "TrophyEikthyr", "TrophyDeer", "TrophyBoar", "TrophyNeck");

            Add(1, "RoundLog", "FineWood", "CopperOre", "Copper", "TinOre", "Tin", "Bronze", "BronzeNails",
                "SurtlingCore", "GreydwarfEye", "Thistle", "Blueberries", "Carrot", "CarrotSeeds", "TrollHide",
                "AncientSeed", "BoneFragments", "Coal", "YellowMushroom", "CryptKey", "Chitin", "Amber",
                "AmberPearl", "TrophyTheElder", "TrophyGreydwarf", "TrophyGreydwarfBrute", "TrophyGreydwarfShaman",
                "TrophyFrostTroll", "TrophySkeleton");

            Add(2, "IronScrap", "IronOre", "Iron", "IronNails", "Chain", "ElderBark", "Guck", "Bloodbag",
                "Entrails", "Ooze", "Root", "WitheredBone", "Turnip", "TurnipSeeds", "Wishbone", "SerpentScale",
                "SerpentMeat", "TrophyBonemass", "TrophyDraugr", "TrophyDraugrElite", "TrophyBlob", "TrophyLeech",
                "TrophySerpent", "TrophyAbomination", "TrophyWraith", "TrophySurtling");

            Add(3, "SilverOre", "Silver", "Obsidian", "WolfPelt", "WolfFang", "WolfClaw", "WolfMeat",
                "WolfHairBundle", "FreezeGland", "Crystal", "DragonTear", "DragonEgg", "Onion", "OnionSeeds",
                "JuteRed", "TrophyDragonQueen", "TrophyWolf", "TrophyFenring", "TrophyCultist", "TrophyUlv",
                "TrophyHatchling", "TrophySGolem");

            Add(4, "BlackMetalScrap", "BlackMetal", "Flax", "LinenThread", "Barley", "BarleyFlour", "Needle",
                "LoxPelt", "LoxMeat", "Tar", "GoblinTotem", "Cloudberry", "ChickenEgg", "ChickenMeat",
                "YagluthDrop", "TrophyGoblinKing", "TrophyLox", "TrophyDeathsquito", "TrophyGrowth",
                "TrophyGoblin", "TrophyGoblinBrute", "TrophyGoblinShaman");

            Add(5, "Carapace", "Eitr", "Sap", "BlackCore", "Wisp", "JuteBlue", "Mandible", "ScaleHide",
                "Softtissue", "BugMeat", "GiantBloodSack", "RoyalJelly", "BlackMarble", "YggdrasilWood", "Bilebag",
                "MushroomJotunPuffs", "MushroomMagecap", "HareMeat", "QueenDrop", "DvergrNeedle",
                "TrophySeekerQueen", "TrophySeeker", "TrophySeekerBrute", "TrophyGjall", "TrophyTick",
                "TrophyDvergr", "TrophyHare");

            Add(6, "FlametalOre", "Flametal", "FlametalOreNew", "FlametalNew", "Grausten", "AskBladder", "AskHide",
                "Blackwood", "CharredBone", "CharcoalResin", "MorgenSinew", "MorgenHeart", "MoltenCore",
                "ProustitePowder", "SulfurStone", "Vineberry", "VineberrySeeds", "Fiddleheadfern",
                "CelestialFeather", "BellFragment", "FaderDrop", "AsksvinMeat", "VoltureMeat",
                "BoneMawSerpentMeat", "BonemawSerpentTooth", "MushroomSmokePuff", "TrophyFader",
                "TrophyAsksvin", "TrophyMorgen", "TrophyCharredMelee", "TrophyFallenValkyrie", "TrophyVolture",
                "TrophyBonemawSerpent");

            // ryby: tam, gdzie się je łowi
            Add(0, "Fish1", "FishingBait", "FishingRod");                 // okoń
            Add(1, "Fish2");                                             // szczupak
            Add(2, "Fish3", "Fish5", "Fish8", "Fish12");                 // tuńczyk, troll-ryba, dorsz koralowy, rozdymka (bagna / ocean)
            Add(3, "Fish4_cave");                                        // tetra z jaskiń
            Add(4, "Fish6", "Fish7");                                    // śledź, granik
            Add(5, "Fish9");                                             // żabnica
            Add(6, "Fish11");                                            // ryba magmowa
            Add(7, "Fish10");                                            // łosoś północny

            // Daleka Północ (1.0) – z listy kontrolnej z gry
            Add(7, "Lingonberry", "Lingonberries", "Gold", "Frostwood", "NornThread", "OrbThunderBlood", "OrbFrostFire",
                "MooseHide", "MooseSinew", "MooseMeat", "CookedMooseMeat", "TrophyMoose", "SealHide", "SealBlubber",
                "Kale", "Poteitr", "Oat", "OatFlour", "Ice", "Leatherstraps", "ElakingHairBundle", "BarkaBranch",
                "MoleClaws", "TrophyJotunWitch", "CrownJewel", "TrophyBlob_Morkhalla", "OozeMork");

            // pozostałe materiały z 1.0 / Bog Witch / Call to Arms
            Add(0, "CookedDeerMeat", "CookedMeat", "FishCooked", "AxeHead1", "AxeHead2", "BlobVial",
                "FireworksRocket_White", "CuredSquirrelHamstring", "PungentPebbles", "FragrantBundle",
                "FreshSeaweed", "PowderedDragonEgg", "YmirRemains",
                "ScytheHandle", "Hook"); // o biomie i tak decydują pozostałe składniki
            Add(1, "BjornHide", "BjornPaw", "TrophyBjorn", "CookedBjornMeat", "Ruby", "MushroomYellow", "MushroomBzerker");
            Add(2, "SerpentMeatCooked");
            Add(4, "UndeadBjornRibcage", "TrophyBjornUndead", "Bread", "LoxPie");
            Add(5, "CookedBugMeat", "MisthareSupreme", "DvergrKeyFragment");
            Add(6, "GemstoneRed", "GemstoneGreen", "GemstoneBlue", "FaderEmber", "CookedAsksvinMeat", "VoltureEgg",
                "DyrnwynTipFragment", "DyrnwynHiltFragment", "DyrnwynBladeFragment");
        }

        private static void Add(int tier, params string[] names)
        {
            foreach (var n in names) BaseTiers[n] = tier;
        }

        // ---- stan zależny od świata / configu ----

        private static ObjectDB _db;
        private static string _overridesRaw;
        private static ZoneSystem _zone;
        private static Dictionary<string, int> _overrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<Recipe>> Producers = new Dictionary<string, List<Recipe>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> ItemTierCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Recipe, int> RecipeTierCache = new Dictionary<Recipe, int>();

        private static void EnsureState()
        {
            ObjectDB db = ObjectDB.instance;
            string raw = Plugin.BiomeOverrides.Value ?? "";
            ZoneSystem zone = ZoneSystem.instance;
            if (db == _db && raw == _overridesRaw && zone == _zone) return;

            _db = db;
            _zone = zone;
            _overridesRaw = raw;
            Producers.Clear();
            ItemTierCache.Clear();
            RecipeTierCache.Clear();
            _overrides = ParseOverrides(raw);

            if (db == null || db.m_recipes == null) return;
            foreach (Recipe r in db.m_recipes)
            {
                if (r == null || r.m_item == null) continue;
                string name = r.m_item.gameObject.name;
                if (!Producers.TryGetValue(name, out List<Recipe> list)) Producers[name] = list = new List<Recipe>();
                list.Add(r);
            }

            if (Plugin.AutoDetect.Value && zone != null) AutoBiomes.Rebuild();

            if (Plugin.DumpRecipes.Value && zone != null) DumpAll(db);
        }

        /// <summary>Zapisuje listę wszystkich przepisów z przypisanym biomem do pliku – do sprawdzenia klasyfikacji.</summary>
        private static void DumpAll(ObjectDB db)
        {
            try
            {
                string L(string t) => Localization.instance != null ? Localization.instance.Localize(t) : t;
                var rows = new List<string[]>();

                foreach (Recipe r in db.m_recipes)
                {
                    if (r == null || r.m_item == null || !r.m_enabled) continue;
                    var s = r.m_item.m_itemData?.m_shared;
                    if (s == null) continue;

                    int biome = GetBiome(r);
                    CraftingStation st = r.m_craftingStation;
                    var mats = new List<string>();
                    if (r.m_resources != null)
                        foreach (var req in r.m_resources)
                            if (req != null && req.m_resItem != null && req.m_amount > 0)
                            {
                                string mp = req.m_resItem.gameObject.name;
                                string mb = IsKnownMaterial(mp) ? BiomeName(MaterialBiome(mp)) : "NIEZNANY";
                                if (IsAutoOnly(mp)) mb += " (auto: " + AutoBiomes.SourceOf(mp) + ")";
                                mats.Add($"{L(req.m_resItem.m_itemData.m_shared.m_name)} x{req.m_amount} ({mp}) [{mb}]");
                            }

                    rows.Add(new[]
                    {
                        biome.ToString(),
                        BiomeName(biome),
                        Filters.CategoryKey(s, r.m_item.gameObject.name),
                        st != null ? L(st.m_name) : "(ręcznie)",
                        L(s.m_name),
                        r.m_item.gameObject.name,
                        string.Join(", ", mats.ToArray())
                    });
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Biom\tKategoria\tStacja\tNazwa\tPrefab\tMateriały");
                foreach (var row in rows.OrderBy(x => x[0]).ThenBy(x => x[3]).ThenBy(x => x[4]))
                    sb.AppendLine(string.Join("\t", row.Skip(1).ToArray()));

                string path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "BiomeCrafting_przepisy.txt");
                System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true));
                Plugin.Log.LogInfo($"Zapisano listę {rows.Count} przepisów do {path}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Nie udało się zapisać listy przepisów: {e.Message}");
            }
        }

        public static int GetBiome(Recipe recipe)
        {
            if (recipe == null) return 0;
            EnsureState();
            if (RecipeTierCache.TryGetValue(recipe, out int cached)) return cached;

            int tier;
            string product = recipe.m_item != null ? recipe.m_item.gameObject.name : null;
            if (product != null && _overrides.TryGetValue(product, out int forced))
                tier = forced;
            else if (TryTierFromName(product, out int byName))
                tier = byName;
            else
                tier = RecipeTier(recipe, 0);

            tier = Mathf.Clamp(tier, 0, BiomeCount - 1);
            RecipeTierCache[recipe] = tier;
            return tier;
        }

        /// <summary>Biom, z którego pochodzi materiał (prefab), np. "Iron" -> Bagna.</summary>
        public static int MaterialBiome(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return 0;
            EnsureState();
            return Mathf.Clamp(ItemTier(prefab, 0), 0, BiomeCount - 1);
        }

        /// <summary>Czy materiał da się wytworzyć (np. brąz), a nie tylko znaleźć.</summary>
        public static bool IsCraftable(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return false;
            EnsureState();
            return Producers.ContainsKey(prefab);
        }

        private static int RecipeTier(Recipe r, int depth)
        {
            int tier = StationTier(r.m_craftingStation);
            if (r.m_resources == null) return tier;

            // przepisy "wystarczy jeden ze składników" (np. surowa ryba z dowolnej ryby):
            // liczy się najwcześniejszy składnik, a nie najpóźniejszy
            bool anyOne = Traverse.Create(r).Field("m_requireOnlyOneIngredient").GetValue() is bool b && b;
            int best = anyOne ? int.MaxValue : int.MinValue;

            foreach (Piece.Requirement req in r.m_resources)
            {
                if (req == null || req.m_resItem == null || req.m_amount <= 0) continue;
                string prefab = req.m_resItem.gameObject.name;
                // posążki ulepszeń (Upgrader0..7) są wspólne dla wszystkich biomów – nie wpływają na biom
                if (IsUpgradeStatue(prefab, out _)) continue;
                int t = ItemTier(prefab, depth);
                best = anyOne ? Math.Min(best, t) : Math.Max(best, t);
            }

            if (best == int.MaxValue || best == int.MinValue) return tier;
            return Math.Max(tier, best);
        }

        /// <summary>Posążki ulepszeń z 1.0: Upgrader{N}Weapon / Upgrader{N}Armor, N = poziom materiału.</summary>
        private static bool IsUpgradeStatue(string prefab, out int level)
        {
            level = 0;
            if (prefab == null || !prefab.StartsWith("Upgrader", StringComparison.OrdinalIgnoreCase) || prefab.Length < 9)
                return false;
            return int.TryParse(prefab.Substring(8, 1), out level);
        }

        /// <summary>Czy biom materiału pochodzi wyłącznie z automatycznego wykrywania (do listy kontrolnej).</summary>
        private static bool IsAutoOnly(string prefab)
        {
            return !_overrides.ContainsKey(prefab) && !BaseTiers.ContainsKey(prefab) && !Producers.ContainsKey(prefab)
                   && !IsUpgradeStatue(prefab, out _) && !TryTierFromName(prefab, out _)
                   && !prefab.StartsWith("Mold", StringComparison.OrdinalIgnoreCase)
                   && AutoBiomes.TryGet(prefab, out _);
        }

        private static int ItemTier(string prefab, int depth)
        {
            if (_overrides.TryGetValue(prefab, out int t)) return t;
            if (BaseTiers.TryGetValue(prefab, out t)) return t;
            if (ItemTierCache.TryGetValue(prefab, out t)) return t;
            if (IsUpgradeStatue(prefab, out t)) return Mathf.Clamp(t, 0, BiomeCount - 1);
            if (TryTierFromName(prefab, out t)) return t;
            if (prefab.StartsWith("Mold", StringComparison.OrdinalIgnoreCase)) return 7; // formy odlewnicze z Dalekiej Północy
            // surowiec spoza tabeli, którego nie da się wytworzyć – biom wykryty z danych gry
            if (!Producers.ContainsKey(prefab) && AutoBiomes.TryGet(prefab, out t)) return t;
            if (depth > 8) return 0;

            ItemTierCache[prefab] = 0; // ochrona przed pętlą
            int result = 0;
            if (Producers.TryGetValue(prefab, out List<Recipe> producers))
            {
                // kilka przepisów na ten sam materiał (np. surowa ryba z różnych ryb) – liczy się najwcześniejszy
                result = int.MaxValue;
                foreach (Recipe p in producers)
                    result = Math.Min(result, RecipeTier(p, depth + 1));
                if (result == int.MaxValue) result = 0;
            }
            ItemTierCache[prefab] = result;
            return result;
        }

        /// <summary>
        /// Minimalny biom wynikający z samej stacji. Tylko dla stacji, które naprawdę pojawiają się
        /// dopiero w danym biomie. Blat przygotowawczy, czara miodna czy stół rzemieślniczy obsługują
        /// przepisy z wielu biomów, więc tam decydują wyłącznie materiały.
        /// </summary>
        private static int StationTier(CraftingStation station)
        {
            if (station == null) return 0;
            string key = ((station.m_name ?? "") + " " + station.gameObject.name).ToLowerInvariant();

            if (key.Contains("blackforge")) return 5;
            if (key.Contains("magetable")) return 5;
            if (key.Contains("meadcauldron") || key.Contains("preptable") || key.Contains("artisan")) return 0;
            if (key.Contains("forge")) return 1;
            if (key.Contains("cauldron")) return 1;
            if (key.Contains("stonecutter")) return 1;
            return 0;
        }

        /// <summary>
        /// Przynęty, uczty i przyprawy mają biom w nazwie prefabu
        /// (np. FishingBaitSwamp, FeastPlains_Material, SpiceMountains).
        /// </summary>
        private static bool TryTierFromName(string prefab, out int tier)
        {
            tier = 0;
            if (string.IsNullOrEmpty(prefab)) return false;
            bool named = prefab.IndexOf("Bait", StringComparison.OrdinalIgnoreCase) >= 0
                      || prefab.StartsWith("Feast", StringComparison.OrdinalIgnoreCase)
                      || prefab.StartsWith("Spice", StringComparison.OrdinalIgnoreCase);
            if (!named) return false;

            string p = prefab.ToLowerInvariant();
            if (p.Contains("deepnorth")) { tier = 7; return true; }
            if (p.Contains("ashlands")) { tier = 6; return true; }
            if (p.Contains("mistlands")) { tier = 5; return true; }
            if (p.Contains("plains")) { tier = 4; return true; }
            if (p.Contains("cave") || p.Contains("mountain")) { tier = 3; return true; }
            if (p.Contains("swamp") || p.Contains("ocean")) { tier = 2; return true; }
            if (p.Contains("forest")) { tier = 1; return true; }
            if (p.Contains("meadow")) { tier = 0; return true; }
            return false;
        }

        // ---- kategorie ----

        private static readonly string[] SkillOrder =
        {
            "Swords", "Axes", "Clubs", "Knives", "Spears", "Polearms", "Bows", "Crossbows",
            "ElementalMagic", "BloodMagic", "Unarmed"
        };

        private static readonly string[] ArmorOrder = { "Helmet", "Chest", "Legs", "Shoulder", "Hands" };

        public static Category GetCategory(ItemDrop.ItemData.SharedData s)
        {
            if (s == null) return Category.Other;
            string type = s.m_itemType.ToString();
            string skill = s.m_skillType.ToString();

            switch (type)
            {
                case "OneHandedWeapon":
                case "TwoHandedWeapon":
                case "TwoHandedWeaponLeft":
                case "Bow":
                case "Attach_Atgeir":
                    return skill == "Pickaxes" ? Category.Tool : Category.Weapon;
                case "Shield":
                    return Category.Shield;
                case "Helmet":
                case "Chest":
                case "Legs":
                case "Shoulder":
                case "Hands":
                    return Category.Armor;
                case "Ammo":
                case "AmmoNonEquipable":
                    return Category.Ammo;
                case "Tool":
                case "Torch":
                    return Category.Tool;
                case "Consumable":
                    return (s.m_food > 0f || s.m_foodStamina > 0f || s.m_foodEitr > 0f) ? Category.Food : Category.Potion;
                case "Utility":
                case "Trinket":
                    return Category.Utility;
                case "Material":
                    return Category.Material;
                default:
                    return Category.Other;
            }
        }

        public static int SubOrder(ItemDrop.ItemData.SharedData s, Category c)
        {
            if (s == null) return 0;
            if (c == Category.Weapon)
            {
                int i = Array.IndexOf(SkillOrder, s.m_skillType.ToString());
                return i < 0 ? SkillOrder.Length : i;
            }
            if (c == Category.Armor)
            {
                int i = Array.IndexOf(ArmorOrder, s.m_itemType.ToString());
                return i < 0 ? ArmorOrder.Length : i;
            }
            return 0;
        }

        // ---- parsowanie configu ----

        private static Dictionary<string, int> ParseOverrides(string raw)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(raw)) return result;

            foreach (string part in raw.Split(';', ','))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string item = part.Substring(0, eq).Trim();
                string biome = part.Substring(eq + 1).Trim();
                if (TryParseBiome(biome, out int tier))
                    result[item] = tier;
                else
                    Plugin.Log.LogWarning($"Nieznany biom \"{biome}\" w przypisaniu \"{part.Trim()}\".");
            }
            return result;
        }

        public static bool TryParseBiome(string s, out int tier)
        {
            tier = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (int.TryParse(s.Trim(), out tier)) return tier >= 0 && tier < BiomeCount;

            switch (Normalize(s))
            {
                case "meadows": case "laki": tier = 0; return true;
                case "blackforest": case "czarnylas": case "las": tier = 1; return true;
                case "swamp": case "bagna": case "bagno": tier = 2; return true;
                case "mountain": case "mountains": case "gory": tier = 3; return true;
                case "plains": case "rowniny": tier = 4; return true;
                case "mistlands": case "mgliste": case "mglisteziemie": tier = 5; return true;
                case "ashlands": case "popielne": case "popielneziemie": tier = 6; return true;
                case "deepnorth": case "glebokapolnoc": case "polnoc": case "dalekapolnoc": tier = 7; return true;
            }
            return false;
        }

        private static string Normalize(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in s.ToLowerInvariant())
            {
                switch (ch)
                {
                    case ' ': case '_': case '-': break;
                    case 'ą': sb.Append('a'); break;
                    case 'ć': sb.Append('c'); break;
                    case 'ę': sb.Append('e'); break;
                    case 'ł': sb.Append('l'); break;
                    case 'ń': sb.Append('n'); break;
                    case 'ó': sb.Append('o'); break;
                    case 'ś': sb.Append('s'); break;
                    case 'ź': case 'ż': sb.Append('z'); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }
    }
}
