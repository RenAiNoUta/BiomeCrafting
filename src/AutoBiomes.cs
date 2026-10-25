using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BiomeCrafting
{
    /// <summary>
    /// Automatyczne wykrywanie biomu surowców z danych gry:
    ///  - stwory: w jakim biomie się pojawiają (SpawnSystemList) i co z nich wypada (CharacterDrop),
    ///  - rośliny, złoża, krzaki, drzewa: w jakim biomie rosną (ZoneSystem.m_vegetation) i co dają.
    /// Gdy coś występuje w kilku biomach, liczy się najwcześniejszy.
    /// Wszystko przez refleksję, żeby zmiany w grze nie psuły kompilacji.
    /// </summary>
    internal static class AutoBiomes
    {
        private static readonly Dictionary<string, int> Tiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Assembly GameAssembly = typeof(Player).Assembly;
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static bool TryGet(string prefab, out int tier) => Tiers.TryGetValue(prefab, out tier);

        public static string SourceOf(string prefab) => Sources.TryGetValue(prefab, out string s) ? s : null;

        public static int Count => Tiers.Count;

        public static void Rebuild()
        {
            Tiers.Clear();
            Sources.Clear();
            try { ScanCreatures(); }
            catch (Exception e) { Plugin.Log.LogWarning($"Auto-biomy (stwory): {e.Message}"); }
            try { ScanVegetation(); }
            catch (Exception e) { Plugin.Log.LogWarning($"Auto-biomy (roślinność): {e.Message}"); }
            Plugin.Log.LogInfo($"Auto-biomy: wykryto {Tiers.Count} przedmiotów.");
        }

        // ------------------------------------------------------------------ stwory

        private static void ScanCreatures()
        {
            Type listType = AccessTools.TypeByName("SpawnSystemList");
            Type dropType = AccessTools.TypeByName("CharacterDrop");
            if (listType == null || dropType == null) return;

            foreach (UnityEngine.Object list in Resources.FindObjectsOfTypeAll(listType))
            {
                if (!(Traverse.Create(list).Field("m_spawners").GetValue() is IEnumerable spawners)) continue;
                foreach (object sd in spawners)
                {
                    Traverse t = Traverse.Create(sd);
                    if (t.Field("m_enabled").GetValue() is bool en && !en) continue;
                    GameObject creature = t.Field("m_prefab").GetValue() as GameObject;
                    int tier = BiomeTier(t.Field("m_biome").GetValue());
                    if (creature == null || tier < 0) continue;

                    Component drop = creature.GetComponent(dropType);
                    if (drop == null || !(Traverse.Create(drop).Field("m_drops").GetValue() is IEnumerable drops)) continue;
                    foreach (object d in drops)
                    {
                        GameObject item = Traverse.Create(d).Field("m_prefab").GetValue() as GameObject;
                        Record(item, tier, creature.name);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ roślinność / złoża

        private static void ScanVegetation()
        {
            if (ZoneSystem.instance == null) return;
            if (!(Traverse.Create(ZoneSystem.instance).Field("m_vegetation").GetValue() is IEnumerable veg)) return;

            foreach (object v in veg)
            {
                Traverse t = Traverse.Create(v);
                if (t.Field("m_enable").GetValue() is bool en && !en) continue;
                GameObject prefab = t.Field("m_prefab").GetValue() as GameObject;
                int tier = BiomeTier(t.Field("m_biome").GetValue());
                if (prefab == null || tier < 0) continue;

                var visited = new HashSet<GameObject>();
                ScanObject(prefab, tier, prefab.name, 0, visited);
            }
        }

        /// <summary>Przegląda komponenty obiektu i zbiera przedmioty, które z niego wypadają.</summary>
        private static void ScanObject(GameObject go, int tier, string source, int depth, HashSet<GameObject> visited)
        {
            if (go == null || depth > 3 || !visited.Add(go)) return;

            if (go.GetComponent<ItemDrop>() != null)
            {
                Record(go, tier, source);
                return;
            }

            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Assembly != GameAssembly) continue;
                if (mb is Character) continue; // nie idziemy w stwory pojawiające się z obiektów

                foreach (FieldInfo f in mb.GetType().GetFields(Fields))
                {
                    object val;
                    try { val = f.GetValue(mb); } catch { continue; }
                    if (val == null) continue;

                    if (val is GameObject g)
                        ScanObject(g, tier, source, depth + 1, visited);
                    else if (val is ItemDrop id)
                        Record(id.gameObject, tier, source);
                    else if (val is DropTable dt && dt.m_drops != null)
                        foreach (DropTable.DropData dd in dt.m_drops)
                            ScanObject(dd.m_item, tier, source, depth + 1, visited);
                }
            }
        }

        // ------------------------------------------------------------------ pomocnicze

        private static void Record(GameObject item, int tier, string source)
        {
            if (item == null || item.GetComponent<ItemDrop>() == null) return;
            string name = item.name;
            if (!Tiers.TryGetValue(name, out int old) || tier < old)
            {
                Tiers[name] = tier;
                Sources[name] = source;
            }
        }

        /// <summary>Flagi Heightmap.Biome -> najwcześniejszy biom moda (0-7), -1 gdy nieznany.</summary>
        private static int BiomeTier(object biomeFlags)
        {
            if (biomeFlags == null) return -1;
            long value;
            try { value = Convert.ToInt64(biomeFlags); } catch { return -1; }
            if (value == 0) return -1;

            int best = int.MaxValue;
            foreach (object flag in Enum.GetValues(biomeFlags.GetType()))
            {
                long fv = Convert.ToInt64(flag);
                if (fv == 0 || (fv & (fv - 1)) != 0) continue;   // tylko pojedyncze flagi
                if ((value & fv) == 0) continue;

                int t = TierOfBiomeName(flag.ToString());
                if (t >= 0 && t < best) best = t;
            }
            return best == int.MaxValue ? -1 : best;
        }

        private static int TierOfBiomeName(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "meadows": return 0;
                case "blackforest": return 1;
                case "swamp": return 2;
                case "ocean": return 2;
                case "mountain": return 3;
                case "plains": return 4;
                case "mistlands": return 5;
                case "ashlands": return 6;
                case "deepnorth": return 7;
                default: return -1;
            }
        }
    }
}
