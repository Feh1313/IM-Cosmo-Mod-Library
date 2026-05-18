using HarmonyLib;
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SimpleJSON;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace ModButtons
{
    // --- 1. HARMONY PATCHES ---

    [HarmonyPatch(typeof(PopupManager), "Start")]
    public class PopupManager_Start
    {
        public static void Postfix()
        {
            ModButtonsUtils.GenerateButtonsPopup();
            ModButtonsBootstrap.EnsureButtonInstalled();
        }
    }

    [HarmonyPatch(typeof(Tabs_Manager), "Awake")]
    public class Tabs_Manager_Awake { public static void Postfix() => ModButtonsBootstrap.EnsureButtonInstalled(); }

    [HarmonyPatch(typeof(Tabs_Manager), nameof(Tabs_Manager.OpenTab))]
    public class Tabs_Manager_OpenTab
    {
        public static void Postfix(Tabs_Manager._tab._type __0)
        {
            if (__0 == Tabs_Manager._tab._type.settings) ModButtonsBootstrap.EnsureButtonInstalled();
        }
    }

    // --- 2. BOOTSTRAPPER ---
    public sealed class ModButtonsBootstrap : MonoBehaviour
    {
        private const int MaxInstallAttempts = 240;
        private const float RetryIntervalSeconds = 0.10f;
        
        private static ModButtonsBootstrap instance;
        private int attempts;
        private float nextAttemptAt;

        public static void EnsureButtonInstalled()
        {
            if (ModButtonsUtils.TryInstallActionHubButton()) { DestroyInstance(); return; }
            if (instance != null) return;
            
            Camera camera = Camera.main;
            if (camera == null) return;

            instance = camera.gameObject.GetComponent<ModButtonsBootstrap>() ?? camera.gameObject.AddComponent<ModButtonsBootstrap>();
            instance.attempts = 0;
            instance.nextAttemptAt = Time.unscaledTime;
        }

        private static void DestroyInstance()
        {
            if (instance != null) UnityEngine.Object.Destroy(instance);
            instance = null;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextAttemptAt) return;
            nextAttemptAt = Time.unscaledTime + RetryIntervalSeconds;
            attempts++;
            if (ModButtonsUtils.TryInstallActionHubButton() || attempts >= MaxInstallAttempts) DestroyInstance();
        }
    }

    // --- 3. MULTI-MOD LOCALIZATION MANAGER ---
    internal static class ModButtonsLocalization
    {
        private const string LocalizationDirectoryName = "Localization";
        private const string EnglishFolderName = "en";
        private const string StringsFileName = "strings.txt";
        private const int MaxLocalizationEntries = 4096;
        private const int MaxLineLength = 8192;
        private const int MaxValueLength = 4096;
        
        private static readonly Dictionary<string, Dictionary<string, string>> ModDictionaries = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        internal static string Get(string modPath, string key, string fallback)
        {
            if (string.IsNullOrEmpty(modPath) || string.IsNullOrEmpty(key)) return fallback;
            EnsureLoaded(modPath);

            if (ModDictionaries.TryGetValue(modPath, out var modDict))
            {
                if (modDict.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            return fallback;
        }

        internal static void EnsureLoaded(string modPath)
        {
            if (string.IsNullOrEmpty(modPath) || ModDictionaries.ContainsKey(modPath)) return;

            Dictionary<string, string> dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ModDictionaries[modPath] = dict;

            try
            {
                string localizationDir = Path.Combine(modPath, LocalizationDirectoryName);
                if (!Directory.Exists(localizationDir)) return;

                LoadFile(Path.Combine(localizationDir, EnglishFolderName, StringsFileName), dict);
                LoadFile(Path.Combine(localizationDir, StringsFileName), dict); 

                string configuredLang = GetConfiguredLanguageCode();
                if (!string.IsNullOrEmpty(configuredLang) && configuredLang != EnglishFolderName)
                {
                    string alias = GetAlias(configuredLang);
                    LoadFile(Path.Combine(localizationDir, configuredLang, StringsFileName), dict);
                    if (alias != configuredLang) LoadFile(Path.Combine(localizationDir, alias, StringsFileName), dict);
                }
            }
            catch { }
        }

        private static void LoadFile(string path, Dictionary<string, string> dict)
        {
            if (!File.Exists(path)) return;

            try
            {
                string[] lines = File.ReadAllLines(path);
                foreach (string raw in lines)
                {
                    if (dict.Count >= MaxLocalizationEntries) return;
                    if (string.IsNullOrEmpty(raw) || raw.Length > MaxLineLength) continue;

                    string trimmed = raw.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#") || trimmed.StartsWith(";") || trimmed.StartsWith("//")) continue;

                    int sepIndex = raw.IndexOf('=');
                    if (sepIndex <= 0) continue;

                    string key = raw.Substring(0, sepIndex).Trim();
                    string value = raw.Substring(sepIndex + 1);
                    if (!string.IsNullOrEmpty(key))
                    {
                        dict[key] = SanitizeValue(value.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t"));
                    }
                }
            }
            catch { }
        }

        private static string GetConfiguredLanguageCode()
        {
            try
            {
                if (staticVars.Settings != null && !string.IsNullOrEmpty(staticVars.Settings.Language))
                {
                    return staticVars.Settings.Language.Trim().ToLowerInvariant();
                }
            }
            catch { }
            return string.Empty;
        }

        private static string GetAlias(string code)
        {
            switch (code)
            {
                case "english": case "enus": case "enusutf8": return "en";
                case "japanese": case "ja": return "jp";
                case "schinese": case "tchinese": case "zh": case "zhcn": return "cn";
                case "russian": case "ukrainian": case "ua": return "ru";
                case "portuguese": case "brazilian": case "pt": case "pt-br": case "pt_br": return "ptbr";
                default: return code;
            }
        }

        private static string SanitizeValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length > MaxValueLength) value = value.Substring(0, MaxValueLength);
            return value.Replace('\0', ' ').Replace('<', '＜').Replace('>', '＞');
        }
    }

    // --- 4. CORE LOGIC ---
    class ModButtonsUtils
    {
        // --- CONSTANTS ---
        private const string SettingsContainerPath = "ScrollRect/Container";
        private const string HubButtonObjName = "ModButtonsHubButton";
        private const string HubButtonLabel = "Mod Actions";
        private const string TargetModMenuButton = "ModMenuButton";
        private const string FallbackSettingsName = "Settings";
        private const string FallbackGraphicsName = "Graphics";
        
        private const string PopupObjName = "ModButtonsPopup";
        private const string UIPanelName = "Panel";
        private const string UISettingsContainerName = "Settings_Container";
        private const string UICancelButtonName = "Cancel";
        private const string UIVerticalContainerName = "VerticalContainer";
        private const string UIDividerName = "Divider";
        
        private const string TargetDirectory = "ModButtons";
        private const string JsonFileName = "buttons.json";
        
        private const string JsonKeyLabel = "label";
        private const string JsonKeyCodeLabel = "codeLabel"; 
        private const string JsonKeyIcon = "icon";
        private const string JsonKeyAssembly = "assembly";
        private const string JsonKeyClass = "class";
        private const string JsonKeyMethod = "method";
        private const string LocKeyModTitle = "mod.title";
        
        private const string PrefixTitle = "Title_";
        private const string PrefixGrid = "Grid_";
        private const string PrefixCell = "Cell_";
        private const string PrefixButton = "Btn_";
        private const string DotSeparator = ".";
        private const string LogErrorPrefix = "[ModButtons] Execution failed:";
        
        private const int CustomPopupID = 998;
        private const int TitleFontSize = 24;
        
        private const float MaxCellSize = 72f; 
        private const float MinButtonSize = 64f;
        private const float MaxButtonSize = 72f;

        private const float GridCellSpacing = 10f;
        private const float VerticalLayoutSpacing = 15f;
        private const float DividerHeight = 3f;
        private const float FallbackBtnMinWidth = 200f;
        private const float FallbackBtnMinHeight = 40f;

        public static bool TryInstallActionHubButton()
        {
            mainScript main = Camera.main?.GetComponent<mainScript>();
            if (main?.Data == null) return false;

            Tabs_Manager tabsManager = main.Data.GetComponent<Tabs_Manager>();
            Tabs_Manager._tab settingsTab = tabsManager?.GetTab(Tabs_Manager._tab._type.settings);
            if (settingsTab?.Tab == null) return false;

            Transform settingsContainer = settingsTab.Tab.transform.Find(SettingsContainerPath);
            if (settingsContainer == null) return false;

            if (settingsContainer.Find(HubButtonObjName) != null) return true;

            GameObject templateButton = settingsContainer.GetComponentsInChildren<Button>(true).FirstOrDefault()?.gameObject;
            if (templateButton == null) return false;

            GameObject modActionButton = CloneMainButton(templateButton, settingsContainer, HubButtonObjName, HubButtonLabel);

            Transform modMenusButton = settingsContainer.Find(TargetModMenuButton); 
            
            if (modMenusButton != null)
            {
                modActionButton.transform.SetSiblingIndex(modMenusButton.GetSiblingIndex() + 1);
            }
            else
            {
                Transform standardSettingsBtn = settingsContainer.Cast<Transform>().FirstOrDefault(t => t.name.Contains(FallbackSettingsName) || t.name.Contains(FallbackGraphicsName));
                if (standardSettingsBtn != null)
                {
                    modActionButton.transform.SetSiblingIndex(standardSettingsBtn.GetSiblingIndex() + 1);
                }
                else
                {
                    modActionButton.transform.SetSiblingIndex(0); 
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(settingsContainer as RectTransform);
            return true;
        }

        public static GameObject GenerateButtonsPopup()
        {
            PopupManager pm = Camera.main?.GetComponent<mainScript>()?.Data?.GetComponent<PopupManager>();
            if (pm == null) return null;
            if (pm.GetByType((PopupManager._type)CustomPopupID) != null) return pm.GetByType((PopupManager._type)CustomPopupID).obj;

            GameObject originalPopup = pm.GetByType(PopupManager._type.settings_difficulty).obj;
            GameObject modButtonsObj = UnityEngine.Object.Instantiate(originalPopup, originalPopup.transform.parent, false);
            modButtonsObj.name = PopupObjName;

            Transform panel = modButtonsObj.transform.Find(UIPanelName);
            
            Transform settingsContainer = panel.Find(UISettingsContainerName);
            if (settingsContainer != null) UnityEngine.Object.DestroyImmediate(settingsContainer.gameObject);
            
            GameObject cancelButton = panel.Find(UICancelButtonName)?.gameObject;
            if (cancelButton != null)
            {
                Button btn = cancelButton.GetComponent<Button>();
                btn.onClick = new Button.ButtonClickedEvent();
                btn.onClick.AddListener(() => PopupManager.Close_());
            }

            GameObject vContainer = new GameObject(UIVerticalContainerName, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            vContainer.transform.SetParent(panel, false);
            
            RectTransform rect = vContainer.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.1f, 0.2f);
            rect.anchorMax = new Vector2(0.9f, 0.8f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            VerticalLayoutGroup vLayout = vContainer.GetComponent<VerticalLayoutGroup>();
            vLayout.spacing = VerticalLayoutSpacing;
            vLayout.childAlignment = TextAnchor.UpperCenter;
            vLayout.childControlHeight = false; 
            
            vContainer.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            PopulateCustomButtons(vContainer.transform, panel);

            PopupManager._popup newPopup = new() { type = (PopupManager._type)CustomPopupID, obj = modButtonsObj, BGBlur = true, BGDarken = true };
            Array.Resize(ref pm.popups, pm.popups.Length + 1);
            pm.popups[pm.popups.Length - 1] = newPopup;

            return modButtonsObj;
        }

        private static void PopulateCustomButtons(Transform verticalContainer, Transform templateSource)
        {
            string relativePath = Path.Combine(TargetDirectory, JsonFileName);
            GameObject templateButton = templateSource.Find(UICancelButtonName)?.gameObject;

            foreach (Mods._mod mod in Mods._Mods)
            {
                if (!mod.IsEnabled()) continue;

                string filepath = Path.Combine(mod.Path, relativePath).Replace("\\", "/");
                if (!File.Exists(filepath)) continue;

                JSONArray jsonArray = JSON.Parse(File.ReadAllText(filepath)).AsArray;
                if (jsonArray.Count == 0) continue;

                ModButtonsLocalization.EnsureLoaded(mod.Path);
                string localizedTitle = ModButtonsLocalization.Get(mod.Path, LocKeyModTitle, mod.Title);

                GameObject titleObj = new GameObject(PrefixTitle + mod.Title, typeof(RectTransform), typeof(TextMeshProUGUI));
                titleObj.transform.SetParent(verticalContainer, false);
                TextMeshProUGUI titleText = titleObj.GetComponent<TextMeshProUGUI>();
                titleText.text = localizedTitle;
                titleText.fontSize = TitleFontSize;
                titleText.alignment = TextAlignmentOptions.Center;
                titleText.color = Color.black;

                GameObject gridContainer = new GameObject(PrefixGrid + mod.Title, typeof(RectTransform), typeof(GridLayoutGroup));
                gridContainer.transform.SetParent(verticalContainer, false);
                
                GridLayoutGroup grid = gridContainer.GetComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(MaxCellSize, MaxCellSize); // Invisible cell container limits
                grid.spacing = new Vector2(GridCellSpacing, GridCellSpacing);
                grid.startAxis = GridLayoutGroup.Axis.Horizontal;
                grid.childAlignment = TextAnchor.UpperCenter;

                foreach (JSONNode item in jsonArray)
                {
                    string fallbackLabel = item[JsonKeyLabel];
                    string codeLabel = item[JsonKeyCodeLabel];
                    string iconName = item[JsonKeyIcon]; 
                    string targetAssembly = item[JsonKeyAssembly]; 
                    string targetClass = item[JsonKeyClass]; 
                    string targetMethod = item[JsonKeyMethod]; 

                    if (string.IsNullOrEmpty(codeLabel)) codeLabel = targetClass + DotSeparator + targetMethod;
                    
                    string localizedLabel = ModButtonsLocalization.Get(mod.Path, codeLabel, fallbackLabel);
                    string iconPath = string.IsNullOrEmpty(iconName) ? string.Empty : Path.Combine(mod.Path, TargetDirectory, iconName);
                    
                    CreateActionButton(gridContainer.transform, templateButton, localizedLabel, iconPath, targetAssembly, targetClass, targetMethod);
                }

                GameObject divider = new GameObject(UIDividerName, typeof(RectTransform), typeof(Image));
                divider.transform.SetParent(verticalContainer, false);
                divider.GetComponent<Image>().color = new Color(0, 0, 0, 0.5f);
                RectTransform divRect = divider.GetComponent<RectTransform>();
                divRect.sizeDelta = new Vector2(0, DividerHeight);
            }
        }

        private static void CreateActionButton(Transform parentGrid, GameObject templateBtn, string label, string iconPath, string asmName, string className, string methodName)
        {
            GameObject btnObj;
            bool hasIcon = !string.IsNullOrEmpty(iconPath) && File.Exists(iconPath);

            if (hasIcon)
            {
                // Create an invisible 72x72 container cell to satisfy the GridLayoutGroup
                GameObject cellObj = new GameObject(PrefixCell + label, typeof(RectTransform));
                cellObj.transform.SetParent(parentGrid, false);

                // Place the actual button inside the cell
                btnObj = new GameObject(PrefixButton + label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(ButtonDefault));
                btnObj.transform.SetParent(cellObj.transform, false);
                
                RectTransform rect = btnObj.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(MinButtonSize, MinButtonSize); // Default before load
                
                Image img = btnObj.GetComponent<Image>();
                img.preserveAspect = true;

                mainScript runner = Camera.main?.GetComponent<mainScript>();
                if (runner != null)
                {
                    runner.StartCoroutine(LoadTexture.LoadSprite(iconPath, img, delegate 
                    { 
                        if (img.sprite != null && img.sprite.texture != null)
                        {
                            // Clamp actual interactive button size based on loaded image dimensions
                            float w = Mathf.Clamp(img.sprite.texture.width, MinButtonSize, MaxButtonSize);
                            float h = Mathf.Clamp(img.sprite.texture.height, MinButtonSize, MaxButtonSize);
                            rect.sizeDelta = new Vector2(w, h);
                        }
                    }));
                }

                ButtonDefault btnDef = btnObj.GetComponent<ButtonDefault>();
                btnDef.DefaultTooltip = label;
                btnDef.SetTooltip(label);
            }
            else
            {
                // Put fallback text buttons inside a layout cell too so grid doesn't squash them
                GameObject cellObj = new GameObject(PrefixCell + label, typeof(RectTransform));
                cellObj.transform.SetParent(parentGrid, false);

                if (templateBtn != null)
                {
                    btnObj = CloneMainButton(templateBtn, cellObj.transform, PrefixButton + label, label);
                    
                    RectTransform rect = btnObj.GetComponent<RectTransform>();
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.anchoredPosition = Vector2.zero;
                    
                    LayoutElement layoutElement = btnObj.AddComponent<LayoutElement>();
                    layoutElement.minWidth = FallbackBtnMinWidth;
                    layoutElement.minHeight = FallbackBtnMinHeight;
                    layoutElement.ignoreLayout = true; // Prevents grid from crushing the fallback
                }
                else return;
            }

            Button btn = btnObj.GetComponent<Button>();
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(() =>
            {
                try
                {
                    Assembly targetAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == asmName);
                    if (targetAsm == null) return;
                    Type targetType = targetAsm.GetType(className);
                    if (targetType == null) return;
                    MethodInfo method = targetType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                    method?.Invoke(null, null);
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LogErrorPrefix} {className}.{methodName}: {e.Message}");
                }
            });
        }

        private static GameObject CloneMainButton(GameObject oldButton, Transform parent, string name, string label)
        {
            GameObject newBtn = UnityEngine.Object.Instantiate(oldButton, parent);
            newBtn.name = name;
            TextMeshProUGUI text = newBtn.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault();
            if (text != null) text.text = label;
            
            Button btn = newBtn.GetComponent<Button>();
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(() => PopupManager.OpenPopup((PopupManager._type)CustomPopupID));
            return newBtn;
        }
    }
}