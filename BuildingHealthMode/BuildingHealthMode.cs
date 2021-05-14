using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BuildingHealthMode
{
    [BepInPlugin("projjm.buildingHealthMode", "Building Health Mode", "1.0.0")]
    [BepInProcess("valheim.exe")]
    public class BuildingHealthMode : BaseUnityPlugin
    {
        public class WearsData
        {
            public Vector3 pos;
            public float healthPercentage;
            public WearNTear nextData = null;
        }

        private static ConfigEntry<KeyCode> ToggleBuildingHealthModeKey;
        private static ConfigEntry<bool> Asynchronous;
        private static ConfigEntry<int> AsyncObjectPerFrame;
        private static ConfigEntry<int> MaxCacheSize;
        private static ConfigEntry<int> CacheOverflowCleanupSize;
        private static ConfigEntry<float> MaxDistance;
        private static ConfigEntry<float> ModUpdateInterval;
        private static ConfigEntry<Color> FullHealthColor;
        private static ConfigEntry<Color> ZeroHealthColor;


        private readonly Harmony Harmony = new Harmony("projjm.buildingHealthMode");
        private static bool _modEnabled = false;

        private static List<WearNTear> HighlightedWears = new List<WearNTear>();
        private static Dictionary<WearNTear, WearsData> WearsDataCache = new Dictionary<WearNTear, WearsData>();

        private static WearsData lastAddedToCache;
        private static WearNTear firstElementCache;

        private float _timeSinceLastUpdate = 0f;

        private bool _needsFirstUpdate = false;
        private static bool isUpdatingAsync = false;

        private static int EmmissionID = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            BindConfigs();
            Harmony.PatchAll();
        }

        void OnDestroy()
        {
            ClearVars();
            Harmony.UnpatchSelf();
        }

        private void BindConfigs()
        {
            ToggleBuildingHealthModeKey = Config.Bind<KeyCode>("Key Bindings", "toggleBuildingHealthModeKey", KeyCode.H, "The key to press to toggle Building Health Mode");

            MaxDistance = Config.Bind<float>("Highlighing", "maxDistance", 20, "The max distance for objects to be highlighted");
            FullHealthColor = Config.Bind<Color>("Colors", "fullHealthColor", Color.green, "The colour of an object when it is at full health");
            ZeroHealthColor = Config.Bind<Color>("Colors", "zeroHealthColor", Color.red, "The colour of an object when it is at zero health");
            Asynchronous = Config.Bind<bool>("Optimization", "asynchronous", true, "Should the highlighting be asynchronous [coroutine] (Recommended ON, disabling this will mean you are likely to freeze for a few seconds upon enabling the mod");
            AsyncObjectPerFrame = Config.Bind<int>("Optimization", "asyncObjectsPerFrame", 20, "The number of objects to be processed (highlighted) per frame when Asynchronous is set to true");
            ModUpdateInterval = Config.Bind<float>("Optimization", "modUpdateInterval", 0.75f, "The interval (in seconds) between highlight checks on objects.");
            MaxCacheSize = Config.Bind<int>("Optimization", "maxCacheSize", 1500, "The max number of objects to cache. Increasing this value will improve FPS but consume more memory");
            CacheOverflowCleanupSize = Config.Bind<int>("Optimization", "cCacheOverflowCleanupSize", 200, "The number of objects to remove from the cache after exceeding the cache limit.");
        }
        private void ClearVars()
        {
            HighlightedWears.Clear();
            WearsDataCache.Clear();
            lastAddedToCache = null;
            firstElementCache = null;
            _modEnabled = false;
            _needsFirstUpdate = false;
            isUpdatingAsync = false;
        }

        void Update()
        {
            if (Player.m_localPlayer == null)
            {
                if (HighlightedWears.Count != 0 || WearsDataCache.Count != 0)
                    ClearVars();
                return;
            }

            bool async = Asynchronous.Value;

            UpdateInputs();
            if (ShouldUpdate())
            {
                if (async && !isUpdatingAsync)
                {
                    isUpdatingAsync = true;
                    StartCoroutine(UpdateBuildingHealthModeCo());
                }
                else if (!async)
                {
                    UpdateBuildingHealthMode();
                }
            }
        }

        private void UpdateInputs()
        {
            if (Input.GetKeyDown(ToggleBuildingHealthModeKey.Value))
            {
                _modEnabled = !_modEnabled;

                if (_modEnabled)
                    _needsFirstUpdate = true;

                if (!_modEnabled && HighlightedWears.Count != 0)
                    DisabledModHighlights();
            }
        }

        private void DisabledModHighlights()
        {
            HighlightedWears.ForEach(wear => wear.ResetHighlight());
            HighlightedWears.Clear();
        }

        private bool ShouldUpdate()
        {
            if (!_modEnabled)
                return false;

            _timeSinceLastUpdate += Time.deltaTime;
            if (_timeSinceLastUpdate > ModUpdateInterval.Value || _needsFirstUpdate)
            {
                if (_needsFirstUpdate)
                    _needsFirstUpdate = false;

                _timeSinceLastUpdate = 0f;
                return true;
            }
            else
            {
                return false;
            }
        }


        private static void UpdateBuildingHealthMode()
        {
            Vector3 playerPos = Player.m_localPlayer.transform.position;
            foreach (WearNTear wearNTear in WearNTear.GetAllInstaces())
            {
                UpdateDataCache(wearNTear);
                if (ShouldHighlight(wearNTear, playerPos))
                    Highlight(wearNTear);
            }
        }

        private static IEnumerator UpdateBuildingHealthModeCo()
        {
            Vector3 playerPos = Player.m_localPlayer.transform.position;
            int c = 0;
            int m = AsyncObjectPerFrame.Value;

            WearNTear[] instances = WearNTear.GetAllInstaces().OrderBy(
                i => Vector3.Distance(i.transform.position, playerPos)).ToArray();

            foreach (WearNTear wearNTear in instances)
            {
                if (c % m == 0)
                    yield return null;

                if (!_modEnabled)
                    break;

                if (wearNTear == null)
                    continue;

                UpdateDataCache(wearNTear);
                if (ShouldHighlight(wearNTear, playerPos))
                    Highlight(wearNTear);
                c++;
            }

            isUpdatingAsync = false;
        }

        private static void UpdateDataCache(WearNTear wearNTear)
        {
            if (!WearsDataCache.ContainsKey(wearNTear))
            {
                WearsData wd = new WearsData();
                wd.pos = wearNTear.gameObject.transform.position;
                wd.healthPercentage = wearNTear.GetHealthPercentage();

                if (WearsDataCache.Count == 0)
                    firstElementCache = wearNTear;

                WearsDataCache.Add(wearNTear, wd);

                if (lastAddedToCache != null)
                    lastAddedToCache.nextData = wearNTear;

                lastAddedToCache = wd;
                CheckShouldDisposeData();
            }
        }

        private static bool ShouldHighlight(WearNTear wearNTear, Vector3 playerPos)
        {
            Vector3 wearNTearPos = WearsDataCache[wearNTear].pos;
            float distance = Vector3.Distance(wearNTearPos, playerPos);
            float healthPercentage = wearNTear.GetHealthPercentage();
            float maxDistance = MaxDistance.Value;

            if (HighlightedWears.Contains(wearNTear))
            {
                if (distance > maxDistance)
                {
                    HighlightedWears.Remove(wearNTear);
                    wearNTear.ResetHighlight();
                    return false;
                }

                if (WearsDataCache[wearNTear].healthPercentage != healthPercentage)
                    return true;
                else
                    return false;
            }

            if (distance <= maxDistance)
                return true;
            else
                return false;
        }

        private static void Highlight(WearNTear wearNTear) => Highlight(wearNTear, wearNTear.GetHealthPercentage());

        private static void Highlight(WearNTear wearNTear, float healthPercentage)
        {
            //Debug.Log("Highlighting");
            if (wearNTear.m_oldMaterials == null || wearNTear.m_oldMaterials.Count == 0)
                wearNTear.Highlight();

            if (!HighlightedWears.Contains(wearNTear))
                HighlightedWears.Add(wearNTear);

            WearsDataCache[wearNTear].healthPercentage = healthPercentage;

            //Debug.Log("Changed Color");
            Color color = Color.Lerp(ZeroHealthColor.Value, FullHealthColor.Value, healthPercentage);
            foreach (WearNTear.OldMeshData oldMaterial in wearNTear.m_oldMaterials)
            {
                if ((bool)oldMaterial.m_renderer)
                {
                    Material[] materials = oldMaterial.m_renderer.materials;
                    foreach (Material obj in materials)
                    {
                        obj.SetColor(EmmissionID, color * 0.4f);
                        obj.color = color;
                    }
                }
            }
        }

        private static void CheckShouldDisposeData()
        {
            if (WearsDataCache.Count > MaxCacheSize.Value)
            {
                for (int i = 0; i < CacheOverflowCleanupSize.Value; i++)
                {
                    WearNTear first = firstElementCache;
                    WearNTear next = WearsDataCache[first].nextData;
                    WearsDataCache.Remove(first);
                    firstElementCache = next;
                }
            }
        }


        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.ResetHighlight))]
        public class ResetHighlight
        {
            public static bool Prefix(WearNTear __instance)
            {
                if (_modEnabled)
                {
                    if (HighlightedWears.Contains(__instance))
                    {
                        UpdateDataCache(__instance);
                        Highlight(__instance);
                        return false;
                    }
                    else
                    {
                        return true;
                    }
                }
                else
                {
                    return true;
                }
            }
        }

    }
}
