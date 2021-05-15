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
    [BepInPlugin("projjm.buildingHealthMode", "Building Health Mode", "1.1.2")]
    [BepInProcess("valheim.exe")]
    public class BuildingHealthMode : BaseUnityPlugin
    {
        public class WearsData
        {
            public Vector3 pos;
            public float healthPercentage;
            public WearNTear nextData = null;
        }

        private static ConfigEntry<bool> activateOnHammerRepairMode;
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

        private static bool _needsFirstUpdate = false;
        private static bool isUpdatingAsync = false;
        private static bool isUsingAutomaticMode = false;
        private static bool wasForceDisabledOnAuto = false;
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
            activateOnHammerRepairMode = Config.Bind<bool>("General", "activateOnHammerRepairMode", true, "Activate Building Health Mode automatically when using the hammer and repair mode is selected");
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
                {
                    _needsFirstUpdate = true;
                    if (activateOnHammerRepairMode.Value)
                    {
                        var t = Player.m_localPlayer.GetSelectedPiece();
                        if (t != null && t.m_repairPiece)
                            isUsingAutomaticMode = true;
                        else
                            isUsingAutomaticMode = false;
                    }
                    else
                        isUsingAutomaticMode = false;
                }


                if (!_modEnabled)
                {
                    if (HighlightedWears.Count != 0)
                        DisabledModHighlights();

                    if (isUsingAutomaticMode)
                        wasForceDisabledOnAuto = true;
                    else
                        wasForceDisabledOnAuto = false;
                }
            }
        }

        private static void DisabledModHighlights()
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
                    HighlightPiece(wearNTear);
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
                    HighlightPiece(wearNTear);
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
            float healthDiffMinimum = 5f;

            if (HighlightedWears.Contains(wearNTear))
            {
                if (distance > maxDistance)
                {
                    HighlightedWears.Remove(wearNTear);
                    wearNTear.ResetHighlight();
                    return false;
                }

                float healthDif = Mathf.Abs(WearsDataCache[wearNTear].healthPercentage - healthPercentage);
                if (healthDif > healthDiffMinimum)
                    return true;
                else
                    return false;
            }

            if (distance <= maxDistance)
                return true;
            else
                return false;
        }

        private static void HighlightPiece(WearNTear wearNTear) => HighlightPiece(wearNTear, wearNTear.GetHealthPercentage());

        private static void HighlightPiece(WearNTear wearNTear, float healthPercentage)
        {
            if (wearNTear.m_oldMaterials == null || wearNTear.m_oldMaterials.Count == 0)
                StoreOldMaterials(wearNTear);

            if (!HighlightedWears.Contains(wearNTear))
                HighlightedWears.Add(wearNTear);

            WearsDataCache[wearNTear].healthPercentage = healthPercentage;

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

        private static void StoreOldMaterials(WearNTear wearNTear)
        {
            wearNTear.m_oldMaterials = new List<WearNTear.OldMeshData>();
            foreach (Renderer highlightRenderer in wearNTear.GetHighlightRenderers())
            {
                WearNTear.OldMeshData item = default(WearNTear.OldMeshData);
                item.m_materials = highlightRenderer.sharedMaterials;
                item.m_color = new Color[item.m_materials.Length];
                item.m_emissiveColor = new Color[item.m_materials.Length];
                for (int i = 0; i < item.m_materials.Length; i++)
                {
                    if (item.m_materials[i].HasProperty("_Color"))
                    {
                        item.m_color[i] = item.m_materials[i].GetColor("_Color");
                    }
                    if (item.m_materials[i].HasProperty("_EmissionColor"))
                    {
                        item.m_emissiveColor[i] = item.m_materials[i].GetColor("_EmissionColor");
                    }
                }
                item.m_renderer = highlightRenderer;
                wearNTear.m_oldMaterials.Add(item);
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
                        HighlightPiece(__instance);
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

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        public class EnableOnHammerRepair
        {
            public static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer)
                    return;

                if (!activateOnHammerRepairMode.Value)
                    return;

                Piece selected = __instance.GetSelectedPiece();
                bool inRepairMode = (selected != null && __instance.GetSelectedPiece().m_repairPiece);
                if (!_modEnabled)
                {
                    if (inRepairMode && !wasForceDisabledOnAuto)
                    {
                        _modEnabled = true;
                        _needsFirstUpdate = true;
                        isUsingAutomaticMode = true;
                    }

                    if (!inRepairMode && wasForceDisabledOnAuto)
                        wasForceDisabledOnAuto = false;
                }
                else
                {
                    if (!inRepairMode && isUsingAutomaticMode)
                    {
                        _modEnabled = false;
                        isUsingAutomaticMode = false;
                        wasForceDisabledOnAuto = false;
                        if (!_modEnabled && HighlightedWears.Count != 0)
                            DisabledModHighlights();
                    }
                }
            }

        }


        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Highlight))]
        class HighlightWhenOnFix
        {
            public static bool Prefix(WearNTear __instance)
            {
                if (__instance.m_oldMaterials == null)
                {
                    __instance.m_oldMaterials = new List<WearNTear.OldMeshData>();
                    foreach (Renderer highlightRenderer in __instance.GetHighlightRenderers())
                    {
                        WearNTear.OldMeshData item = default(WearNTear.OldMeshData);
                        item.m_materials = highlightRenderer.sharedMaterials;
                        item.m_color = new Color[item.m_materials.Length];
                        item.m_emissiveColor = new Color[item.m_materials.Length];
                        for (int i = 0; i < item.m_materials.Length; i++)
                        {
                            if (item.m_materials[i].HasProperty("_Color"))
                            {
                                item.m_color[i] = item.m_materials[i].GetColor("_Color");
                            }
                            if (item.m_materials[i].HasProperty("_EmissionColor"))
                            {
                                item.m_emissiveColor[i] = item.m_materials[i].GetColor("_EmissionColor");
                            }
                        }
                        item.m_renderer = highlightRenderer;
                        __instance.m_oldMaterials.Add(item);
                    }
                }
                Color color = new Color(0.6f, 0.8f, 1f);

                float supportColorValue;

                if (!_modEnabled)
                {
                    supportColorValue = __instance.GetSupportColorValue();
                    if (supportColorValue >= 0f)
                    {
                        color = Color.Lerp(new Color(1f, 0f, 0f), new Color(0f, 1f, 0f), supportColorValue);
                        Color.RGBToHSV(color, out var H, out var S, out var V);
                        S = Mathf.Lerp(1f, 0.5f, supportColorValue);
                        V = Mathf.Lerp(1.2f, 0.9f, supportColorValue);
                        color = Color.HSVToRGB(H, S, V);
                    }
                }
                
                foreach (WearNTear.OldMeshData oldMaterial in __instance.m_oldMaterials)
                {
                    if ((bool)oldMaterial.m_renderer)
                    {
                        Material[] materials = oldMaterial.m_renderer.materials;
                        foreach (Material obj in materials)
                        {
                            obj.SetColor("_EmissionColor", color * 0.4f);
                            obj.color = color;
                        }
                    }
                }
                __instance.CancelInvoke("ResetHighlight");
                __instance.Invoke("ResetHighlight", 0.2f);
                return false;
            }
        }


    }
}
