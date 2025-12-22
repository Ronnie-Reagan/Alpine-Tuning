using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

[assembly: MelonInfo(typeof(AlpineTuning.AlpineTuningMod), "Alpine Tuning", "2025.12.21", "Don")]
[assembly: MelonGame("Hanki Games", "Sledders")]

namespace AlpineTuning
{
    public class AlpineTuningMod : MelonMod
    {
        public static AlpineTuningMod Instance;

        // Live sled context we keep updated while the player rides
        public static VehicleScriptableObject ActiveSO;
        public static SnowmobileController ActiveController;
        public static Respawnable ActiveRespawn;
        public static Vector3 ActiveSpawnPos;
        public static Quaternion ActiveSpawnRot;

        // Defaults + saved tunes per sled
        private static readonly Dictionary<string, SledDefaults> DefaultDatabase = new Dictionary<string, SledDefaults>();
        private static readonly Dictionary<string, TunePreset> PersistentPresets = new Dictionary<string, TunePreset>();
        private static List<VehicleScriptableObject> SelectableSleds = new List<VehicleScriptableObject>();

        private static bool DefaultsBuilt;

        // Parts catalog
        private static readonly List<EnginePart> EngineParts = new List<EnginePart>();
        private static readonly List<TrackPart> TrackParts = new List<TrackPart>();
        private static readonly List<HandlingPart> HandlingParts = new List<HandlingPart>();

        // UI selections
        private int _selectedEngineIndex;
        private int _selectedTrackIndex;
        private int _selectedHandlingIndex;
        private int _selectedDonorIndex; // 0 = none, >0 = SelectableSleds[index-1]

        private string _currentSledKey = "";
        private TuneMode _tuneMode = TuneMode.Default;
        private bool _darkTheme = true;
        private bool _showUI = true;

        // UI layout/state
        private Rect _windowRect = new Rect(20, 20, 450, 520);
        private bool _windowInit;
        private bool _resizing;
        private Vector2 _resizeStartMouse;
        private Vector2 _resizeStartSize;
        private Vector2 _donorScroll;
        private Rect _toggleIconRect = new Rect(12f, 34f, 44f, 44f);
        private Texture2D _toggleIconTexture;

        private bool _hasPendingChanges;
        private int _lastAppliedEngineIndex = -1;
        private int _lastAppliedTrackIndex = -1;
        private int _lastAppliedHandlingIndex = -1;
        private int _lastAppliedDonorIndex = -1;

        private GUIStyle _labelStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _tooltipStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _dropdownStyle;
        private GUIStyle _iconButtonStyle;
        private readonly Color _alpineAccent = new Color(0.74f, 0.88f, 1f, 1f);

        private static readonly string ConfigRoot =
            Path.Combine(MelonEnvironment.UserDataDirectory, "AlpineTuning");
        private static readonly string LegacyConfigRoot =
            Path.Combine(MelonEnvironment.UserDataDirectory, "SleddersTuner");

        private static string DefaultsDir => Path.Combine(ConfigRoot, "Defaults");
        private static string PresetsDir => Path.Combine(ConfigRoot, "Presets");

        private enum TuneMode
        {
            Default,
            Auto
        }

        public override void OnInitializeMelon()
        {
            Instance = this;
            Directory.CreateDirectory(ConfigRoot);
            Directory.CreateDirectory(DefaultsDir);
            Directory.CreateDirectory(PresetsDir);
            MaybeMigrateLegacyConfig();

            BuildPartsCatalog();
            MelonLogger.Msg("Alpine Tuning initialized.");
        }

        private void MaybeMigrateLegacyConfig()
        {
            try
            {
                if (!Directory.Exists(LegacyConfigRoot))
                    return;

                bool hasNewContent = Directory.EnumerateFileSystemEntries(ConfigRoot).Any();
                if (hasNewContent)
                    return;

                DirectoryCopy(LegacyConfigRoot, ConfigRoot, true);
                MelonLogger.Msg("Pulled over your old SleddersTuner files into AlpineTuning.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Legacy config migration skipped: {ex}");
            }
        }

        private void DirectoryCopy(string sourceDir, string destDir, bool copySubDirs)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists)
                return;

            DirectoryInfo[] dirs = dir.GetDirectories();
            Directory.CreateDirectory(destDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetPath = Path.Combine(destDir, file.Name);
                file.CopyTo(targetPath, true);
            }

            if (!copySubDirs)
                return;

            foreach (DirectoryInfo subdir in dirs)
            {
                string targetSubDir = Path.Combine(destDir, subdir.Name);
                DirectoryCopy(subdir.FullName, targetSubDir, true);
            }
        }

        public override void OnGUI()
        {
            if (!_windowInit)
            {
                _windowInit = true;
                InitStyles();
            }

            DrawToggleBadge();

            if (!_showUI || ActiveSO == null)
                return;

            Color prevColor = GUI.color;
            Color prevBg = GUI.backgroundColor;

            if (_darkTheme)
            {
                GUI.color = Color.white;
                GUI.backgroundColor = new Color(0.09f, 0.11f, 0.14f, 0.97f);
            }
            else
            {
                GUI.color = Color.black;
                GUI.backgroundColor = new Color(0.94f, 0.96f, 0.99f, 0.97f);
            }

            AnchorWindowToTopRight();

            _windowRect = GUI.Window(
                726400,
                _windowRect,
                DrawWindow,
                "Alpine Tuning");

            GUI.color = prevColor;
            GUI.backgroundColor = prevBg;
        }

        private void DrawToggleBadge()
        {
            EnsureIconTexture();

            Rect badgeRect = _toggleIconRect;
            Rect backdropRect = new Rect(badgeRect.x - 4f, badgeRect.y - 4f, badgeRect.width + 8f, badgeRect.height + 8f);

            Color prevColor = GUI.color;
            GUI.color = _showUI
                ? new Color(0.16f, 0.52f, 0.86f, 0.9f)
                : new Color(0.16f, 0.16f, 0.16f, 0.7f);
            GUI.DrawTexture(backdropRect, Texture2D.whiteTexture);

            GUI.color = Color.white;
            if (_toggleIconTexture != null)
                GUI.DrawTexture(badgeRect, _toggleIconTexture);
            else
                GUI.Label(badgeRect, "AT", _titleStyle ?? GUI.skin.label);

            string tooltip = _showUI ? "Hide Alpine Tuning" : "Show Alpine Tuning";
            GUIStyle buttonStyle = _iconButtonStyle ?? GUIStyle.none;
            if (GUI.Button(badgeRect, new GUIContent(string.Empty, tooltip), buttonStyle))
                _showUI = !_showUI;

            GUI.color = prevColor;
        }

        private void EnsureIconTexture()
        {
            if (_toggleIconTexture != null)
                return;

            try
            {
                if (!IconByteArray.IsValid())
                {
                    MelonLogger.Warning("Alpine icon data invalid; using text badge instead.");
                    return;
                }

                _toggleIconTexture = new Texture2D(
                    IconByteArray.Width,
                    IconByteArray.Height,
                    TextureFormat.RGBA32,
                    false);

                _toggleIconTexture.LoadRawTextureData(IconByteArray.RGBA);
                _toggleIconTexture.Apply();
                _toggleIconTexture.filterMode = FilterMode.Bilinear;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Could not build Alpine toggle icon: {ex}");
                _toggleIconTexture = null;
            }
        }

        // ============================================================
        // WINDOW DRAW
        // ============================================================
        private void DrawWindow(int id)
        {
            if (ActiveSO == null)
            {
                GUILayout.Label("No sled detected.", _labelStyle);
                GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
                return;
            }

            bool selectionChanged = false;

            GUILayout.BeginVertical();
            {
                // Title + theme toggle
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label("Alpine Tuning", _titleStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(_darkTheme ? "Light" : "Dark", GUILayout.Width(60)))
                    {
                        _darkTheme = !_darkTheme;
                        InitStyles();
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                GUILayout.Label("Alpine = top-shelf mods without killing the stock feel.", _labelStyle);

                GUILayout.Space(2);

                // Current sled info
                GUILayout.Label(new GUIContent(
                    $"Current Sled: {ActiveSO.name}",
                    "Name of the currently controlled snowmobile."),
                    _labelStyle);

                GUILayout.Label(new GUIContent(
                    $"HP: {ActiveSO.horsePower:F1} | PF: {ActiveSO.powerFactor:F2} | Lug: {ActiveSO.lugHeight:F1}",
                    "These values come from either factory defaults, parts, or your applied tune."),
                    _labelStyle);

                GUILayout.Space(6);

                // Engine section
                GUILayout.Label(new GUIContent("Engine Package", "Choose an engine upgrade package."), _labelStyle);
                int prevEngine = _selectedEngineIndex;
                _selectedEngineIndex = DrawDropdown(
                    _selectedEngineIndex,
                    EngineParts.Select(p => p.Name).ToArray());
                selectionChanged |= _selectedEngineIndex != prevEngine;

                GUILayout.Space(4);

                // Track section
                GUILayout.Label(new GUIContent("Track Package", "Track choices now act as modifiers: we nudge lug height and friction instead of hard overrides."), _labelStyle);
                int prevTrack = _selectedTrackIndex;
                _selectedTrackIndex = DrawDropdown(
                    _selectedTrackIndex,
                    TrackParts.Select(p => p.Name).ToArray());
                selectionChanged |= _selectedTrackIndex != prevTrack;

                GUILayout.Space(4);

                // Handling section
                GUILayout.Label(new GUIContent("Handling Kit (COM/COG)", "Adjusts center of mass for handling behavior."), _labelStyle);
                int prevHandling = _selectedHandlingIndex;
                _selectedHandlingIndex = DrawDropdown(
                    _selectedHandlingIndex,
                    HandlingParts.Select(p => p.Name).ToArray());
                selectionChanged |= _selectedHandlingIndex != prevHandling;

                GUILayout.Space(8);

                // Engine swap
                GUILayout.Label(new GUIContent("Engine Swap (Donor Sled)",
                    "Copy the engine (horsepower & power factor) from another sled."), _labelStyle);

                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label("Donor:", GUILayout.Width(50));

                    string[] donorNames = BuildDonorNameArray();
                    int prevDonor = _selectedDonorIndex;
                    int newIndex = DrawDropdown(_selectedDonorIndex, donorNames);
                    _selectedDonorIndex = newIndex;
                    selectionChanged |= _selectedDonorIndex != prevDonor;
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(6);

                // How we treat saved tunes
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label(
                        new GUIContent("Tune Mode:",
                            "Default keeps factory data when you swap sleds. Auto Apply replays your saved Alpine tune whenever this sled loads and pushes changes instantly as you pick parts."),
                        _labelStyle,
                        GUILayout.Width(90));

                    bool isAuto = _tuneMode == TuneMode.Auto;
                    bool chooseDefault = GUILayout.Toggle(!isAuto, "Default / Disabled", GUILayout.Width(150));
                    bool chooseAuto = GUILayout.Toggle(isAuto, "Auto Apply", GUILayout.Width(100));

                    if (chooseDefault && _tuneMode != TuneMode.Default)
                    {
                        _tuneMode = TuneMode.Default;
                        TryApplyPresetForCurrentSled();
                    }
                    else if (chooseAuto && _tuneMode != TuneMode.Auto)
                    {
                        _tuneMode = TuneMode.Auto;
                        TryApplyPresetForCurrentSled();
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(8);

                // Actions
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label(
                        new GUIContent(
                            _hasPendingChanges ? "Pending apply" : "Live on sled",
                            _tuneMode == TuneMode.Auto
                                ? "Auto Apply is on: part changes immediately push to the sled and get saved."
                                : "Manual mode: click Apply after you pick parts."),
                        _labelStyle,
                        GUILayout.Width(150));

                    if (GUILayout.Button(new GUIContent("Apply", "Apply parts + engine swap to the current sled."), _buttonStyle))
                    {
                        ApplyPartsAndSwap();
                    }

                    if (GUILayout.Button(new GUIContent("Reload Sled", "Respawn the sled at its spawn position to apply physics changes cleanly."), _buttonStyle))
                    {
                        ReloadSled();
                    }

                    if (GUILayout.Button(new GUIContent("Reset to Factory", "Restore this sled's original factory tune."), _buttonStyle))
                    {
                        ResetToFactory();
                        ApplyPartsAndSwap(false);
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // Tooltip area
                string tip = GUI.tooltip;
                if (!string.IsNullOrEmpty(tip))
                {
                    GUILayout.Space(4);
                    GUILayout.Label(tip, _tooltipStyle);
                }

                GUILayout.Space(4);

                // Quick window resize controls
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label("Window Size:", GUILayout.Width(80));
                    if (GUILayout.Button("W+", GUILayout.Width(35))) _windowRect.width += 20;
                    if (GUILayout.Button("W-", GUILayout.Width(35))) _windowRect.width = Mathf.Max(350, _windowRect.width - 20);
                    if (GUILayout.Button("H+", GUILayout.Width(35))) _windowRect.height += 20;
                    if (GUILayout.Button("H-", GUILayout.Width(35))) _windowRect.height = Mathf.Max(380, _windowRect.height - 20);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            if (selectionChanged)
            {
                _hasPendingChanges = HasPendingSelectionChange();

                if (_tuneMode == TuneMode.Auto)
                    ApplyPartsAndSwap();
            }

            HandleResize();

            // Drag via title bar
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
        }

        private void HandleResize()
        {
            Rect resizeRect = new Rect(0, _windowRect.height - 16, 16, 16);
            GUI.DrawTexture(resizeRect, Texture2D.whiteTexture);

            var e = Event.current;
            if (e.type == EventType.MouseDown && resizeRect.Contains(e.mousePosition))
            {
                _resizing = true;
                _resizeStartMouse = e.mousePosition;
                _resizeStartSize = new Vector2(_windowRect.width, _windowRect.height);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _resizing)
            {
                var delta = e.mousePosition - _resizeStartMouse;
                _windowRect.width = Mathf.Max(350, _resizeStartSize.x + delta.x);
                _windowRect.height = Mathf.Max(380, _resizeStartSize.y + delta.y);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _resizing)
            {
                _resizing = false;
                e.Use();
            }
        }

        private void AnchorWindowToTopRight()
        {
            float margin = 14f;
            float screenWidth = Screen.width;
            float maxWidth = Mathf.Max(240f, screenWidth - (margin * 2));
            _windowRect.width = Mathf.Min(_windowRect.width, maxWidth);
            _windowRect.x = Mathf.Max(margin, screenWidth - _windowRect.width - margin);
            _windowRect.y = Mathf.Max(margin, _windowRect.y);
        }

        private int DrawDropdown(int selectedIndex, string[] options)
        {
            if (options == null || options.Length == 0)
            {
                GUILayout.Label("No options.", _labelStyle);
                return 0;
            }

            if (selectedIndex < 0 || selectedIndex >= options.Length)
                selectedIndex = 0;

            GUILayout.BeginHorizontal();
            GUILayout.Label(options[selectedIndex], _dropdownStyle);
            if (GUILayout.Button("Next", GUILayout.Width(50)))
            {
                // Simple cycling behavior instead of complex popup
                selectedIndex = (selectedIndex + 1) % options.Length;
            }
            GUILayout.EndHorizontal();

            return selectedIndex;
        }

        private string[] BuildDonorNameArray()
        {
            if (SelectableSleds == null || SelectableSleds.Count == 0)
                return new[] { "None" };

            string[] arr = new string[SelectableSleds.Count + 1];
            arr[0] = "None";
            for (int i = 0; i < SelectableSleds.Count; i++)
                arr[i + 1] = SelectableSleds[i].name;
            return arr;
        }

        // ============================================================
        // CORE LOGIC
        // ============================================================
        private void RecordAppliedSelectionState()
        {
            _lastAppliedEngineIndex = _selectedEngineIndex;
            _lastAppliedTrackIndex = _selectedTrackIndex;
            _lastAppliedHandlingIndex = _selectedHandlingIndex;
            _lastAppliedDonorIndex = _selectedDonorIndex;
            _hasPendingChanges = false;
        }

        private bool HasPendingSelectionChange()
        {
            return _selectedEngineIndex != _lastAppliedEngineIndex ||
                   _selectedTrackIndex != _lastAppliedTrackIndex ||
                   _selectedHandlingIndex != _lastAppliedHandlingIndex ||
                   _selectedDonorIndex != _lastAppliedDonorIndex;
        }

        private void ApplyPartsAndSwap(bool persistPreset = true)
        {
            if (ActiveSO == null)
                return;

            if (!DefaultsBuilt)
                TryBuildDefaults();

            _currentSledKey = GetSledKey(ActiveSO);
            if (!DefaultDatabase.TryGetValue(_currentSledKey, out var baseDefaults))
            {
                MelonLogger.Warning($"No defaults found for sled key '{_currentSledKey}'. Cannot apply tune.");
                return;
            }

            // Engine source: donor or self
            SledDefaults engineSource = baseDefaults;
            if (_selectedDonorIndex > 0 && SelectableSleds != null &&
                _selectedDonorIndex - 1 < SelectableSleds.Count)
            {
                var donor = SelectableSleds[_selectedDonorIndex - 1];
                string donorKey = GetSledKey(donor);
                if (DefaultDatabase.TryGetValue(donorKey, out var donorDefaults))
                    engineSource = donorDefaults;
                else
                    MelonLogger.Warning($"Engine swap donor '{donor.name}' has no defaults entry; using self instead.");
            }

            // Resolve parts
            EnginePart enginePart = EngineParts[Mathf.Clamp(_selectedEngineIndex, 0, EngineParts.Count - 1)];
            TrackPart trackPart = TrackParts[Mathf.Clamp(_selectedTrackIndex, 0, TrackParts.Count - 1)];
            HandlingPart handlingPart = HandlingParts[Mathf.Clamp(_selectedHandlingIndex, 0, HandlingParts.Count - 1)];

            // Compute final values
            float finalHP = engineSource.HorsePower * enginePart.HpMult;
            float finalPF = engineSource.PowerFactor * enginePart.PfMult;

            float finalLug = (baseDefaults.LugHeight * trackPart.LugHeightMultiplier) + trackPart.LugHeightOffset;
            float finalFriction = baseDefaults.Friction * trackPart.FrictionMultiplier;
            finalLug = Mathf.Max(1f, finalLug);
            finalFriction = Mathf.Max(0.05f, finalFriction);

            Vector3 finalCom = baseDefaults.CenterOfMassOffset + handlingPart.ComOffsetDelta;
            Vector3 finalDriverCom = baseDefaults.DriverComOffset + handlingPart.DriverComOffsetDelta;

            // Apply to active SO
            ActiveSO.horsePower = finalHP;
            ActiveSO.powerFactor = finalPF;
            ActiveSO.lugHeight = finalLug;
            ActiveSO.coefficientOfFriction = finalFriction;
            ActiveSO.centerOfMassOffset = finalCom;
            ActiveSO.driverCenterOfMassOffset = finalDriverCom;

            MelonLogger.Msg($"Applied tune to '{ActiveSO.name}': HP={finalHP:F1}, PF={finalPF:F2}, Lug={finalLug:F1}, Fric={finalFriction:F2}");

            // Save preset so Auto mode has the latest selection ready
            if (persistPreset)
            {
                var preset = new TunePreset
                {
                    SledKey = _currentSledKey,
                    EnginePartName = enginePart.Name,
                    TrackPartName = trackPart.Name,
                    HandlingPartName = handlingPart.Name,
                    DonorSledKey = engineSource == baseDefaults ? null : GetSledKeyFromDefaults(engineSource)
                };
                SavePreset(preset);
            }
            RecordAppliedSelectionState();
        }
        private void ReloadSled()
        {
            try
            {
                // Controller singleton
                var controllerType = typeof(Controller);
                var instanceProp = controllerType.GetProperty(
                    "PKMPAOKMHCB",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                var controllerInstance = instanceProp?.GetValue(null);
                if (controllerInstance == null)
                {
                    MelonLogger.Error("ReloadSled: Controller singleton not found.");
                    return;
                }

                // Method that actually destroys + recreates the sled
                var controllerInstanceType = controllerInstance.GetType();
                MethodInfo trySpawnMethod = controllerInstanceType.GetMethod(
                    "TrySpawnPlayer",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Transform), typeof(bool) },
                    null);

                // If overload resolution failed (e.g., extra overloads), fall back to manual filtering by name + parameter count.
                if (trySpawnMethod == null)
                {
                    trySpawnMethod = controllerInstanceType
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m =>
                            m.Name == "TrySpawnPlayer" &&
                            m.GetParameters().Length == 2 &&
                            m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(Transform)) &&
                            m.GetParameters()[1].ParameterType == typeof(bool));
                }

                if (trySpawnMethod == null)
                {
                    MelonLogger.Error("ReloadSled: TrySpawnPlayer method not found (overload not resolved).");
                    return;
                }

                // Use current sled transform as spawn anchor
                Transform spawnTransform = ActiveController != null
                    ? ActiveController.transform
                    : null;

                if (spawnTransform == null)
                {
                    MelonLogger.Error("ReloadSled: ActiveController transform is null.");
                    return;
                }

                // FORCE = true — destroy & recreate
                trySpawnMethod.Invoke(controllerInstance, new object[] { spawnTransform, true });

                MelonLogger.Msg("[Alpine Tuning] Full sled reload triggered (destroy + recreate).");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ReloadSled failed: {ex}");
            }
        }


        private void ResetToFactory()
        {
            if (ActiveSO == null)
                return;

            if (!DefaultsBuilt)
                TryBuildDefaults();

            _currentSledKey = GetSledKey(ActiveSO);
            if (!DefaultDatabase.TryGetValue(_currentSledKey, out var baseDefaults))
            {
                MelonLogger.Warning($"ResetToFactory: No defaults for '{_currentSledKey}'.");
                return;
            }

            ApplyDefaultsToSO(ActiveSO, baseDefaults);

            _selectedEngineIndex = 0;
            _selectedTrackIndex = 0;
            _selectedHandlingIndex = 0;
            _selectedDonorIndex = 0;

            // Remove any persistent preset on reset
            string presetPath = Path.Combine(PresetsDir, _currentSledKey + ".json");
            if (File.Exists(presetPath))
                File.Delete(presetPath);
            PersistentPresets.Remove(_currentSledKey);

            MelonLogger.Msg($"Reset sled '{ActiveSO.name}' to factory defaults.");
        }

        // ============================================================
        // DEFAULTS & PRESETS
        // ============================================================
        private void TryBuildDefaults()
        {
            if (DefaultsBuilt)
                return;

            try
            {
                BuildSelectableSledList();
                LoadDefaultsFromDisk();

                // Create defaults for any sled that doesn't have one yet
                foreach (var sled in SelectableSleds)
                {
                    string key = GetSledKey(sled);
                    if (!DefaultDatabase.ContainsKey(key))
                    {
                        var def = SledDefaults.FromSled(sled, key);
                        DefaultDatabase[key] = def;
                        SaveDefault(def);
                    }
                }

                // Load presets (if any)
                LoadPresetsFromDisk();

                DefaultsBuilt = true;
                MelonLogger.Msg($"Built defaults DB for {DefaultDatabase.Count} sleds.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"TryBuildDefaults error: {ex}");
            }
        }

        private void BuildSelectableSledList()
        {
            if (SelectableSleds != null && SelectableSleds.Count > 0)
                return;

            try
            {
                // Find a VehicleListScriptableObject instance
                var allLists = Resources.FindObjectsOfTypeAll<VehicleListScriptableObject>();
                if (allLists == null || allLists.Length == 0)
                {
                    MelonLogger.Warning("No VehicleListScriptableObject found; engine swap list will be empty.");
                    SelectableSleds = new List<VehicleScriptableObject>();
                    return;
                }

                var list = allLists[0];
                // Use property SelectableVehicles via reflection (since we have decompiled shape)
                var prop = typeof(VehicleListScriptableObject).GetProperty("SelectableVehicles",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                VehicleScriptableObject[] arr;
                if (prop != null)
                {
                    arr = prop.GetValue(list) as VehicleScriptableObject[];
                    if (arr == null)
                        arr = Array.Empty<VehicleScriptableObject>();
                }
                else
                {
                    // Fallback: use list.vehicles if property missing
                    FieldInfo vehiclesField = typeof(VehicleListScriptableObject).GetField("vehicles",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    arr = vehiclesField?.GetValue(list) as VehicleScriptableObject[] ?? Array.Empty<VehicleScriptableObject>();
                }

                SelectableSleds = arr.ToList();
                MelonLogger.Msg($"Selectable sled count: {SelectableSleds.Count}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"BuildSelectableSledList error: {ex}");
                SelectableSleds = new List<VehicleScriptableObject>();
            }
        }

        private static string GetSledKey(VehicleScriptableObject sled)
        {
            return sled == null
                ? "UNKNOWN"
                : sled.name.Trim().Replace(' ', '_');
        }

        private static string GetSledKeyFromDefaults(SledDefaults def)
        {
            return def.SledKey;
        }

        private void ApplyDefaultsToSO(VehicleScriptableObject so, SledDefaults def)
        {
            so.horsePower = def.HorsePower;
            so.powerFactor = def.PowerFactor;
            so.lugHeight = def.LugHeight;
            so.coefficientOfFriction = def.Friction;
            so.centerOfMassOffset = def.CenterOfMassOffset;
            so.driverCenterOfMassOffset = def.DriverComOffset;
        }

        // Simple JSON via Unity's JsonUtility (no arrays at root)
        private void SaveDefault(SledDefaults def)
        {
            try
            {
                string path = Path.Combine(DefaultsDir, def.SledKey + ".json");
                string json = JsonUtility.ToJson(def, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"SaveDefault error: {ex}");
            }
        }

        private void LoadDefaultsFromDisk()
        {
            if (!Directory.Exists(DefaultsDir))
                return;

            foreach (string file in Directory.GetFiles(DefaultsDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var def = JsonUtility.FromJson<SledDefaults>(json);
                    if (def != null && !string.IsNullOrEmpty(def.SledKey))
                    {
                        DefaultDatabase[def.SledKey] = def;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"LoadDefaultsFromDisk: {file} error: {ex}");
                }
            }
        }

        private void SavePreset(TunePreset preset)
        {
            try
            {
                string path = Path.Combine(PresetsDir, preset.SledKey + ".json");
                string json = JsonUtility.ToJson(preset, true);
                File.WriteAllText(path, json);
                PersistentPresets[preset.SledKey] = preset;
                MelonLogger.Msg($"Saved preset for {preset.SledKey}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"SavePreset error: {ex}");
            }
        }

        private void LoadPresetsFromDisk()
        {
            if (!Directory.Exists(PresetsDir))
                return;

            foreach (string file in Directory.GetFiles(PresetsDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var preset = JsonUtility.FromJson<TunePreset>(json);
                    if (preset != null && !string.IsNullOrEmpty(preset.SledKey))
                    {
                        PersistentPresets[preset.SledKey] = preset;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"LoadPresetsFromDisk: {file} error: {ex}");
                }
            }
        }

        private void TryApplyPresetForCurrentSled()
        {
            if (ActiveSO == null || !DefaultsBuilt)
                return;

            _currentSledKey = GetSledKey(ActiveSO);
            if (!DefaultDatabase.TryGetValue(_currentSledKey, out var baseDefaults))
                return;

            if (_tuneMode != TuneMode.Auto)
            {
                // Default mode: keep factory tune active and only apply manual changes.
                ApplyDefaultsToSO(ActiveSO, baseDefaults);
                _selectedEngineIndex = 0;
                _selectedTrackIndex = 0;
                _selectedHandlingIndex = 0;
                _selectedDonorIndex = 0;
                RecordAppliedSelectionState();
                return;
            }

            if (!PersistentPresets.TryGetValue(_currentSledKey, out var preset))
            {
                // No preset - use factory defaults and Stock parts
                ApplyDefaultsToSO(ActiveSO, baseDefaults);
                _selectedEngineIndex = 0;
                _selectedTrackIndex = 0;
                _selectedHandlingIndex = 0;
                _selectedDonorIndex = 0;
                RecordAppliedSelectionState();
                return;
            }

            // Map part names to indices
            _selectedEngineIndex = Math.Max(0, EngineParts.FindIndex(p => p.Name == preset.EnginePartName));
            _selectedTrackIndex = Math.Max(0, TrackParts.FindIndex(p => p.Name == preset.TrackPartName));
            _selectedHandlingIndex = Math.Max(0, HandlingParts.FindIndex(p => p.Name == preset.HandlingPartName));

            // Donor mapping
            if (!string.IsNullOrEmpty(preset.DonorSledKey) && SelectableSleds != null)
            {
                int donorIndex = SelectableSleds.FindIndex(s => GetSledKey(s) == preset.DonorSledKey);
                _selectedDonorIndex = donorIndex >= 0 ? donorIndex + 1 : 0;
            }
            else
            {
                _selectedDonorIndex = 0;
            }

            // Now apply parts and swap
            MelonLogger.Msg($"Auto applying your saved Alpine tune for '{ActiveSO.name}'.");
            ApplyPartsAndSwap();
        }

        // ============================================================
        // PART CATALOG
        // ============================================================
        private void BuildPartsCatalog()
        {
            EngineParts.Clear();
            TrackParts.Clear();
            HandlingParts.Clear();

            // Engine
            EngineParts.Add(new EnginePart("Stock", 1.00f, 1.00f));
            EngineParts.Add(new EnginePart("Stage 1 Kit", 1.10f, 1.05f));
            EngineParts.Add(new EnginePart("Stage 2 Kit", 1.25f, 1.15f));
            EngineParts.Add(new EnginePart("Performance Build", 1.40f, 1.25f));
            EngineParts.Add(new EnginePart("Turbo Kit", 1.60f, 1.40f));
            EngineParts.Add(new EnginePart("Extreme Turbo", 2.00f, 1.60f));

            // Track: multipliers and small offsets to keep each sled's DNA intact
            TrackParts.Add(new TrackPart("Trail Track", 0.94f, 0.94f, -1f));
            TrackParts.Add(new TrackPart("Mountain Track", 1.08f, 1.05f, 4f));
            TrackParts.Add(new TrackPart("Deep Powder Track", 1.18f, 1.08f, 8f));
            TrackParts.Add(new TrackPart("Racing Track", 0.86f, 0.90f, -2f));
            TrackParts.Add(new TrackPart("Ice Studded", 1.00f, 1.18f, 1f));
            TrackParts.Add(new TrackPart("Alpine Signature Track", 1.15f, 1.12f, 6f));

            // Handling
            HandlingParts.Add(new HandlingPart("Stock", Vector3.zero, Vector3.zero));
            HandlingParts.Add(new HandlingPart("Low COM Kit", new Vector3(0f, -0.10f, 0f), Vector3.zero));
            HandlingParts.Add(new HandlingPart("Front Bias", new Vector3(0f, 0f, 0.10f), Vector3.zero));
            HandlingParts.Add(new HandlingPart("Rear Bias", new Vector3(0f, 0f, -0.10f), Vector3.zero));
            HandlingParts.Add(new HandlingPart("Precision Kit", new Vector3(0f, -0.05f, 0.05f), Vector3.zero));
        }

        // ============================================================
        // UI STYLES
        // ============================================================
        private void InitStyles()
        {
            Color lightText = new Color(0.14f, 0.20f, 0.26f);

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = _darkTheme ? Color.white : lightText }
            };

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = _darkTheme ? _alpineAccent : new Color(0.10f, 0.26f, 0.38f) }
            };

            _tooltipStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 11,
                wordWrap = true,
                normal =
                {
                    textColor = _darkTheme ? _alpineAccent : lightText,
                    background = Texture2D.grayTexture
                }
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                normal = { textColor = _darkTheme ? _alpineAccent : new Color(0.15f, 0.30f, 0.40f) }
            };

            _dropdownStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12,
                normal = { textColor = _darkTheme ? Color.white : lightText }
            };

            _iconButtonStyle = new GUIStyle(GUIStyle.none)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
        }

        // ============================================================
        // HARMONY PATCH
        // ============================================================
        [HarmonyPatch(typeof(SnowmobileController), "LocalInit")]
        public static class Patch_LocalInit
        {
            // Signature must match original method:
            // public void LocalInit(CLPMKJKKJEE, OMBJMMDJNKM, Vector3, Quaternion)
            public static void Postfix(
                SnowmobileController __instance,
                Vector3 KMFHFHOFBFH,
                Quaternion LPNJFGKBIIC)
            {
                try
                {
                    if (Instance == null)
                        return;

                    ActiveController = __instance;

                    // Reflect KJFNKMCOKLL (VehicleScriptableObject)
                    FieldInfo soField = typeof(SnowmobileController).GetField("KJFNKMCOKLL",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (soField == null)
                    {
                        MelonLogger.Warning("LocalInit Postfix: Could not find field 'KJFNKMCOKLL' on SnowmobileController.");
                        return;
                    }

                    ActiveSO = soField.GetValue(__instance) as VehicleScriptableObject;
                    if (ActiveSO == null)
                    {
                        MelonLogger.Warning("LocalInit Postfix: VehicleScriptableObject is null.");
                        return;
                    }

                    // Capture respawnable and spawn pose
                    ActiveRespawn = __instance.GetComponent<Respawnable>();
                    ActiveSpawnPos = KMFHFHOFBFH;
                    ActiveSpawnRot = LPNJFGKBIIC;

                    // Build defaults DB if needed and apply preset if exists
                    Instance.TryBuildDefaults();
                    Instance.TryApplyPresetForCurrentSled();

                    MelonLogger.Msg($"Detected player sled '{ActiveSO.name}' and initialized tuner context.");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"LocalInit Postfix error: {ex}");
                }
            }
        }

        // ============================================================
        // DATA CLASSES
        // ============================================================
        [Serializable]
        private class SledDefaults
        {
            public string SledKey;
            public float HorsePower;
            public float PowerFactor;
            public float LugHeight;
            public float Friction;
            public Vector3 CenterOfMassOffset;
            public Vector3 DriverComOffset;

            public static SledDefaults FromSled(VehicleScriptableObject so, string key)
            {
                return new SledDefaults
                {
                    SledKey = key,
                    HorsePower = so.horsePower,
                    PowerFactor = so.powerFactor,
                    LugHeight = so.lugHeight,
                    Friction = so.coefficientOfFriction,
                    CenterOfMassOffset = so.centerOfMassOffset,
                    DriverComOffset = so.driverCenterOfMassOffset
                };
            }
        }

        [Serializable]
        private class TunePreset
        {
            public string SledKey;
            public string EnginePartName;
            public string TrackPartName;
            public string HandlingPartName;
            public string DonorSledKey;
        }

        private readonly struct EnginePart
        {
            public readonly string Name;
            public readonly float HpMult;
            public readonly float PfMult;

            public EnginePart(string name, float hpMult, float pfMult)
            {
                Name = name;
                HpMult = hpMult;
                PfMult = pfMult;
            }
        }

        private readonly struct TrackPart
        {
            public readonly string Name;
            public readonly float LugHeightMultiplier;
            public readonly float FrictionMultiplier;
            public readonly float LugHeightOffset;

            public TrackPart(string name, float lugHeightMultiplier, float frictionMultiplier, float lugHeightOffset = 0f)
            {
                Name = name;
                LugHeightMultiplier = lugHeightMultiplier;
                FrictionMultiplier = frictionMultiplier;
                LugHeightOffset = lugHeightOffset;
            }
        }

        private readonly struct HandlingPart
        {
            public readonly string Name;
            public readonly Vector3 ComOffsetDelta;
            public readonly Vector3 DriverComOffsetDelta;

            public HandlingPart(string name, Vector3 comDelta, Vector3 driverComDelta)
            {
                Name = name;
                ComOffsetDelta = comDelta;
                DriverComOffsetDelta = driverComDelta;
            }
        }
    }
}
