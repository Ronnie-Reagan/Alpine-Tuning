using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlpineTuning
{
    internal static class AlpineNativeUiConfig
    {
        // Element names / IDs.
        public const string RootName = "alpine-tuning-root";
        public const string GarageTuningButtonName = "AlpineTuningActionButton";
        public const string GarageStyleButtonName = "SUITuning";

        // Feature switches.
        // Reflection field/method names used by the native game UI.
        // These are intentionally centralized because obfuscated game updates may change them.
        public const string VehicleRootFieldName = "NPAACPBJNOL";

        // Default UI layout values.
        public const float DefaultButtonHeight = 28f;
        public const float DefaultButtonMarginRight = 4f;
        public const float DefaultButtonMarginTop = 2f;
        public const float DefaultButtonMarginBottom = 2f;
        public const float DefaultMutedLabelMarginTop = 2f;
        public const float DefaultButtonRowMarginTop = 8f;
        public const float DefaultTitleFontSize = 17f;
        public const float SectionGap = 12f;
        public const float RowGap = 6f;
        public const float InlineGap = 6f;
        public const float StatChipPaddingHorizontal = 6f;
        public const float StatChipPaddingVertical = 3f;

        // Fine tune clamp ranges.
        public const float PowerTrimMin = -10f;
        public const float PowerTrimMax = 10f;
        public const float TractionTrimMin = -10f;
        public const float TractionTrimMax = 10f;
        public const float WeightTrimMin = -8f;
        public const float WeightTrimMax = 8f;
        public const float ClutchTrimMin = -10f;
        public const float ClutchTrimMax = 10f;
        public const float CenterOfMassYMin = -0.08f;
        public const float CenterOfMassYMax = 0.08f;
        public const float CenterOfMassZMin = -0.12f;
        public const float CenterOfMassZMax = 0.12f;
        public const float SkiStanceMin = -0.08f;
        public const float SkiStanceMax = 0.08f;

        // Colors.
        public static readonly Color PanelBackgroundColor = new Color(0.07f, 0.09f, 0.11f, 0.92f);
        public static readonly Color StatusTextColor = new Color(0.74f, 0.88f, 1f, 1f);
        public static readonly Color MutedTextColor = new Color(0.72f, 0.78f, 0.84f, 1f);
        public static readonly Color TitleTextColor = Color.white;
        public static readonly Color RowTextColor = Color.white;
        public static readonly Color AccentColor = new Color(0.78f, 0.92f, 0.08f, 1f);
        public static readonly Color ButtonBackgroundColor = new Color(0.16f, 0.18f, 0.20f, 0.92f);
        public static readonly Color ActiveButtonTextColor = new Color(0.04f, 0.05f, 0.05f, 1f);
        public static readonly Color ChipBackgroundColor = new Color(0.18f, 0.21f, 0.23f, 0.88f);
        public static readonly Color DangerButtonColor = new Color(0.42f, 0.14f, 0.12f, 0.92f);
        public static readonly Color DangerTextColor = new Color(1f, 0.82f, 0.78f, 1f);

        // Text.
        public const string NoSavedProfilesText = "No Saved Tunes.";
        public const string ReloadRequiredHintText = "Ready for next ride";
        public const string ApplyFailedText = "Setup update failed.";
        public const string SaveFailedText = "Setup save failed.";
    }

    internal static class AlpineNativeUi
    {
        private sealed class GarageNavigationNode
        {
            public string Kind;
            public string Id;
            public string Title;
            public string FocusedElementName;
            public Vector2 ScrollOffset;
            public Vector2 DetailScrollOffset;

            public GarageNavigationNode(string kind, string id, string title)
            {
                Kind = kind;
                Id = id;
                Title = title;
            }
        }

        private sealed class GarageEngineCandidate
        {
            public readonly VehicleScriptableObject Vehicle;
            public readonly SledDefaults StockDefaults;
            public readonly string Signature;

            public GarageEngineCandidate(
                VehicleScriptableObject vehicle,
                SledDefaults stockDefaults)
            {
                Vehicle = vehicle;
                StockDefaults = stockDefaults;
                Signature = EngineSignature(stockDefaults, vehicle);
            }
        }

        private enum GarageMetricDirection
        {
            HigherIsBetter,
            LowerIsBetter,
            Preference
        }

        /// <summary>
        /// One comparison is used by the landing card, category cards, part
        /// previews and Dyno. Factory and Current always travel through
        /// PreviewProfile; Candidate is present only while another part is being
        /// inspected. This prevents an installed snapshot from accidentally
        /// becoming the stock baseline.
        /// </summary>
        private sealed class GarageComparisonSnapshot
        {
            public SledDefaults Defaults;
            public TuneProfile FactoryProfile;
            public TuneProfile CurrentProfile;
            public TuneProfile CandidateProfile;
            public ResolvedStats Factory => FactoryProfile?.resolvedStats;
            public ResolvedStats Current => CurrentProfile?.resolvedStats;
            public ResolvedStats Candidate => CandidateProfile?.resolvedStats;
            public PartEffect FactoryEffect;
            public PartEffect CurrentEffect;
            public PartEffect CandidateEffect;
        }

        private sealed class GarageMetricDescriptor
        {
            public string Label;
            public string Tooltip;
            public float Factory;
            public float Current;
            public float Candidate;
            public bool HasCandidate;
            public bool Available = true;
            public float? SafetyMinimum;
            public float? SafetyMaximum;
            public GarageMetricDirection Direction;
            public Func<float, string> Format;
        }

        private sealed class GaragePlotSeries
        {
            public string Name;
            public Color Color;
            public readonly List<Vector2> Points = new List<Vector2>();
        }

        private sealed class GarageElementState
        {
            public readonly VisualElement Element;
            public readonly StyleEnum<DisplayStyle> Display;
            public readonly bool Enabled;

            public GarageElementState(VisualElement element)
            {
                Element = element;
                Display = element.style.display;
                Enabled = element.enabledSelf;
            }

            public void Restore()
            {
                if (Element == null)
                    return;

                Element.style.display = Display;
                Element.SetEnabled(Enabled);
            }
        }

        /// <summary>
        /// Owns only the elements Alpine temporarily replaces in one live garage
        /// template. A VehicleSelectionUiController may outlive its pushed visual
        /// tree, so identity is the controller plus the exact current root.
        /// </summary>
        private sealed class GarageNativeSession
        {
            private readonly VehicleSelectionUiController _controller;
            private readonly VisualElement _nativeVehicleList;
            private readonly VisualElement _bottomDock;
            private readonly VisualElement _controlInfo;
            private readonly VisualElement _sidePanelBody;
            private readonly bool _usesNativeHost;
            private readonly VisualElement[] _nativeActions;
            private readonly List<GarageElementState> _nativeActionStates = new List<GarageElementState>();
            private readonly List<GarageElementState> _sidePanelStates = new List<GarageElementState>();
            private readonly List<ControlIndicatorButton> _contextActions = new List<ControlIndicatorButton>();

            private GarageElementState _nativeVehicleListState;
            private GarageElementState[] _pendingNativeActionRestoreStates;
            private VisualElement _previousFocus;
            private bool _sidePanelReplaced;

            public readonly VisualElement TemplateRoot;
            public VisualElement Surface { get; private set; }
            public VisualElement DetailHost { get; set; }
            public VisualElement DynoOverlay { get; set; }
            public ControlIndicatorButton EntryButton { get; set; }
            public ControlIndicatorButton StyleButton { get; set; }
            public ControlIndicatorButton ReadyButton { get; set; }
            public Action DirectTuningShortcut { get; set; }
            public string OriginalStyleText { get; set; }
            public bool IsOpen { get; private set; }
            public bool IsDisposed { get; private set; }
            public VehicleSelectionUiController Controller => _controller;
            public VisualElement ControlInfo => _controlInfo;
            public bool UsesNativeHost => _usesNativeHost;

            public GarageNativeSession(
                VehicleSelectionUiController controller,
                VisualElement templateRoot,
                VisualElement nativeVehicleList,
                VisualElement bottomDock,
                VisualElement controlInfo,
                VisualElement sidePanelBody,
                bool usesNativeHost,
                params VisualElement[] nativeActions)
            {
                _controller = controller;
                TemplateRoot = templateRoot;
                _nativeVehicleList = nativeVehicleList;
                _bottomDock = bottomDock;
                _controlInfo = controlInfo;
                _sidePanelBody = sidePanelBody;
                _usesNativeHost = usesNativeHost;
                _nativeActions = nativeActions ?? Array.Empty<VisualElement>();
            }

            public void Mount(VisualElement surface)
            {
                if (surface == null)
                    throw new ArgumentNullException(nameof(surface));

                Surface = surface;
                ApplyNativeGarageRailStyle(surface, !UsesNativeHost);
                if (DetailHost != null && _sidePanelBody == null)
                    ApplyFallbackGarageDetailStyle(DetailHost);
            }

            public void Open()
            {
                if (IsDisposed || IsOpen)
                    return;

                // A close normally restores these on the next UI tick so the
                // Cancel event cannot also reach the native garage actions. If
                // Alpine is reopened before that tick, restore the old snapshot
                // before taking a fresh one from the native controls.
                RestorePendingNativeActions();
                IsOpen = true;
                EnsureSurfaceMounted();
                _previousFocus = FocusedElement(TemplateRoot);
                _nativeVehicleListState = _nativeVehicleList != null
                    ? new GarageElementState(_nativeVehicleList)
                    : null;
                if (_nativeVehicleList != null)
                {
                    _nativeVehicleList.SetEnabled(false);
                    _nativeVehicleList.style.display = DisplayStyle.None;
                }

                _nativeActionStates.Clear();
                foreach (VisualElement action in _nativeActions.Concat(new VisualElement[] { EntryButton }))
                {
                    if (action == null || _nativeActionStates.Any(state => ReferenceEquals(state.Element, action)))
                        continue;

                    _nativeActionStates.Add(new GarageElementState(action));
                    action.SetEnabled(false);
                    action.style.display = DisplayStyle.None;
                }

                if (Surface != null)
                    Surface.style.display = DisplayStyle.Flex;
            }

            public void ShowDetails(bool show)
            {
                if (DetailHost == null)
                    return;

                if (!show)
                {
                    RestoreSidePanel();
                    return;
                }

                VisualElement expectedParent = _sidePanelBody ?? TemplateRoot;
                if (!ReferenceEquals(DetailHost.parent, expectedParent))
                {
                    _sidePanelStates.Clear();
                    _sidePanelReplaced = false;
                    expectedParent?.Add(DetailHost);
                }

                if (!_sidePanelReplaced)
                {
                    _sidePanelStates.Clear();
                    if (_sidePanelBody != null)
                    {
                        VisualElement vendorLogo = _sidePanelBody.Q<VisualElement>("VendorLogo");
                        VisualElement sledName = _sidePanelBody.Q<VisualElement>("SnowmobileName");
                        for (int i = 0; i < _sidePanelBody.childCount; i++)
                        {
                            VisualElement child = _sidePanelBody[i];
                            if (child == null || ReferenceEquals(child, DetailHost) ||
                                ContainsElement(child, vendorLogo) || ContainsElement(child, sledName))
                            {
                                continue;
                            }

                            _sidePanelStates.Add(new GarageElementState(child));
                            child.SetEnabled(false);
                            child.style.display = DisplayStyle.None;
                        }
                    }

                    _sidePanelReplaced = true;
                }

                DetailHost.style.display = DisplayStyle.Flex;
            }

            public void ReassertOpenOwnership()
            {
                if (!IsOpen || IsDisposed)
                    return;

                EnsureSurfaceMounted();
                if (_nativeVehicleList != null)
                {
                    _nativeVehicleList.SetEnabled(false);
                    _nativeVehicleList.style.display = DisplayStyle.None;
                }
                foreach (GarageElementState state in _nativeActionStates)
                {
                    if (state.Element == null)
                        continue;
                    state.Element.SetEnabled(false);
                    state.Element.style.display = DisplayStyle.None;
                }
                if (_sidePanelReplaced)
                {
                    foreach (GarageElementState state in _sidePanelStates)
                    {
                        if (state.Element == null)
                            continue;
                        state.Element.SetEnabled(false);
                        state.Element.style.display = DisplayStyle.None;
                    }
                    if (DetailHost != null)
                        DetailHost.style.display = DisplayStyle.Flex;
                }
                if (Surface != null)
                    Surface.style.display = DisplayStyle.Flex;
            }

            public void SetContextActions(
                string backLabel,
                Action back,
                string secondaryLabel,
                Action secondary,
                string tertiaryLabel,
                Action tertiary,
                ControlIndicatorButton classAnchor,
                ControlIndicatorButton readyButton,
                string utilityLabel = null,
                Action utility = null)
            {
                ClearContextActions();
                if (_controlInfo == null)
                    return;

                AddContextAction("Cancel", backLabel, "alpine-button-back", back, classAnchor, null);
                AddContextAction("Secondary", secondaryLabel, "alpine-button-save", secondary, classAnchor, null);
                AddContextAction("Tertiary", tertiaryLabel, "alpine-button-reset", tertiary, classAnchor, readyButton);
                AddContextAction("Utility", utilityLabel, "alpine-button-dyno", utility, classAnchor, null);
            }

            public VisualElement FindContextAction(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return null;
                return _contextActions.FirstOrDefault(action => action != null && action.name == name);
            }

            public VisualElement FirstContextAction()
            {
                return _contextActions.FirstOrDefault(CanFocus);
            }

            public void Close()
            {
                CancelHeadlightCaptureIfActive();
                if (!IsOpen)
                    return;

                IsOpen = false;
                ClearContextActions();
                RestoreSidePanel();
                DynoOverlay?.RemoveFromHierarchy();
                DynoOverlay = null;

                if (Surface != null)
                    Surface.style.display = DisplayStyle.None;
                Surface?.RemoveFromHierarchy();
                _nativeVehicleListState?.Restore();
                _nativeVehicleListState = null;
                GarageElementState[] actionStates = _nativeActionStates.ToArray();
                _nativeActionStates.Clear();
                _pendingNativeActionRestoreStates = actionStates;

                // Cancel is dispatched to every attached ControlIndicator. Keep
                // native actions disabled until this dispatch has fully unwound so
                // Alpine Back cannot also close the owning garage menu. Retain the
                // snapshot on this session: MenuController.Close may synchronously
                // detach TemplateRoot before this scheduled item can run, in which
                // case Dispose restores the same snapshot synchronously.
                TemplateRoot?.schedule.Execute(() =>
                {
                    if (IsOpen)
                        return;
                    RestorePendingNativeActions(actionStates);
                });

                VisualElement focus = _previousFocus;
                _previousFocus = null;
                TemplateRoot?.schedule.Execute(() =>
                {
                    if (CanFocus(focus) && focus.panel == TemplateRoot.panel)
                        focus.Focus();
                    else if (CanFocus(EntryButton) && EntryButton.panel == TemplateRoot.panel)
                        EntryButton.Focus();
                });
            }

            public void Dispose()
            {
                if (IsDisposed)
                    return;

                Close();
                // A detached template may never run scheduled items. Close keeps
                // its snapshot on the session, so a synchronous native detach can
                // restore every display/enabled state here exactly once.
                RestorePendingNativeActions();
                IsDisposed = true;
                ClearContextActions();
                RestoreSidePanel();
                DetailHost?.RemoveFromHierarchy();
                DynoOverlay?.RemoveFromHierarchy();
                DynoOverlay = null;
                Surface?.RemoveFromHierarchy();
                DirectTuningShortcut = null;
                EntryButton?.RemoveFromHierarchy();
                if (StyleButton != null && OriginalStyleText != null)
                    StyleButton.DisplayText = OriginalStyleText;
            }

            private void RestorePendingNativeActions(GarageElementState[] expectedStates = null)
            {
                GarageElementState[] states = _pendingNativeActionRestoreStates;
                if (states == null ||
                    (expectedStates != null && !ReferenceEquals(expectedStates, states)))
                {
                    return;
                }

                // Clear ownership before invoking UI Toolkit. A later scheduled
                // callback or a repeated Dispose therefore cannot restore twice,
                // even if a restore itself causes another lifecycle callback.
                _pendingNativeActionRestoreStates = null;
                foreach (GarageElementState state in states)
                    state.Restore();
            }

            private void AddContextAction(
                string actionName,
                string label,
                string elementName,
                Action clicked,
                ControlIndicatorButton classAnchor,
                ControlIndicatorButton conflictButton)
            {
                if (clicked == null || string.IsNullOrWhiteSpace(label))
                    return;

                var button = new ControlIndicatorButton
                {
                    name = elementName,
                    ActionName = actionName,
                    DisplayText = label.ToUpperInvariant(),
                    focusable = true
                };
                CopyClasses(classAnchor, button);
                if (string.Equals(actionName, "Utility", StringComparison.OrdinalIgnoreCase))
                {
                    // Sledders has no bindable Utility action, so it cannot supply
                    // a shortcut glyph. Keep the native indicator slot and its
                    // exact button geometry, then place a neutral graph mark in
                    // that slot. The button remains pointer/focus-submit only and
                    // therefore does not reintroduce the removed D shortcut.
                    VisualElement indicator = button.Q<VisualElement>(className: "control-indicator");
                    if (indicator != null)
                    {
                        indicator.Clear();
                        var graphMark = new Label("\u25A5")
                        {
                            name = "AlpineDynoIndicator",
                            pickingMode = PickingMode.Ignore
                        };
                        graphMark.style.width = 20f;
                        graphMark.style.height = 20f;
                        graphMark.style.fontSize = 13f;
                        graphMark.style.unityTextAlign = TextAnchor.MiddleCenter;
                        graphMark.style.color = AlpineNativeUiConfig.TitleTextColor;
                        graphMark.style.backgroundColor = Color.clear;
                        indicator.Add(graphMark);
                    }
                }
                button.clicked += () =>
                {
                    if (conflictButton != null && conflictButton.enabledInHierarchy &&
                        IsActuallyDisplayed(conflictButton))
                        return;
                    clicked();
                };
                _controlInfo.Add(button);
                _contextActions.Add(button);
            }

            private void ClearContextActions()
            {
                foreach (ControlIndicatorButton action in _contextActions)
                    action?.RemoveFromHierarchy();
                _contextActions.Clear();
            }

            private void RestoreSidePanel()
            {
                if (!_sidePanelReplaced)
                {
                    if (DetailHost != null)
                    {
                        DetailHost.style.display = DisplayStyle.None;
                        DetailHost.RemoveFromHierarchy();
                    }
                    return;
                }

                if (DetailHost != null)
                {
                    DetailHost.style.display = DisplayStyle.None;
                    DetailHost.RemoveFromHierarchy();
                }
                foreach (GarageElementState state in _sidePanelStates)
                    state.Restore();
                _sidePanelStates.Clear();
                _sidePanelReplaced = false;
            }

            private void EnsureSurfaceMounted()
            {
                if (Surface == null || Surface.parent != null)
                    return;

                if (_bottomDock != null)
                {
                    int index = UsesNativeHost &&
                                _nativeVehicleList != null &&
                                ReferenceEquals(_nativeVehicleList.parent, _bottomDock)
                        ? _bottomDock.IndexOf(_nativeVehicleList)
                        : (_controlInfo != null ? _bottomDock.IndexOf(_controlInfo) : _bottomDock.childCount);
                    _bottomDock.Insert(Mathf.Clamp(index, 0, _bottomDock.childCount), Surface);
                }
                else
                {
                    TemplateRoot?.Add(Surface);
                }
            }

            private static bool ContainsElement(VisualElement root, VisualElement candidate)
            {
                return candidate != null &&
                       (ReferenceEquals(root, candidate) || IsDescendantOf(candidate, root));
            }
        }

        private const string NavigationRoot = "root";
        private const string NavigationCategory = "category";
        private const string NavigationPart = "part";
        private const string NavigationPanel = "panel";

        private static readonly Dictionary<int, Action> GarageRenderActions = new Dictionary<int, Action>();
        private static readonly Dictionary<int, GarageNativeSession> GarageSessions = new Dictionary<int, GarageNativeSession>();
        private static readonly Dictionary<int, Func<bool>> GarageNativeCloseRequests = new Dictionary<int, Func<bool>>();
        private static object _rewiredGaragePlayer;
        private static MethodInfo _rewiredGetButtonDownByName;
        private static float _nextRewiredGarageResolveTime;
        public static bool HasAttachedMenus => HasAttachedNativeUiRoot();
        public static bool IsGarageTuningOpen =>
            GarageSessions.Values.Any(session => session != null && session.IsOpen && !session.IsDisposed);

        private static bool CancelHeadlightCaptureIfActive(AlpineTuningMod mod = null)
        {
            if (mod == null)
                mod = AlpineTuningMod.Instance;
            if (mod == null || !mod.IsCapturingHeadlightBinding)
                return false;

            mod.CancelHeadlightBindingCapture();
            mod.ConsumeHeadlightBindingCaptureResult();
            return true;
        }

        public static void DetachGarageSessions()
        {
            CancelHeadlightCaptureIfActive();
            foreach (GarageNativeSession session in GarageSessions.Values.ToArray())
                session?.Dispose();
            GarageSessions.Clear();
            GarageRenderActions.Clear();
            GarageNativeCloseRequests.Clear();
            GarageIconResources.Release();
        }

        public static bool AllowGarageControllerClose(VehicleSelectionUiController controller)
        {
            if (controller == null)
                return true;

            if (!GarageNativeCloseRequests.TryGetValue(controller.GetInstanceID(), out Func<bool> request) ||
                request == null)
                return true;

            try
            {
                return request();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Garage close guard failed open: {ex.GetType().Name}");
                return true;
            }
        }

        public static void NotifyGarageSelectionChanged(VehicleSelectionUiController controller)
        {
            if (controller == null)
                return;

            int controllerId = controller.GetInstanceID();
            if (!GarageSessions.TryGetValue(controllerId, out GarageNativeSession session) ||
                session.IsDisposed ||
                !ReferenceEquals(session.Controller, controller) ||
                !GarageRenderActions.TryGetValue(controllerId, out var render) ||
                render == null)
                return;

            try
            {
                render();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Garage setup refresh skipped: {ex.GetType().Name}");
            }
        }

        public static bool TryAttachOpenMenus(AlpineTuningMod mod)
        {
            if (mod == null)
                return false;

            try
            {
                bool attached = false;
                foreach (var vehicleMenu in Resources.FindObjectsOfTypeAll<VehicleSelectionUiController>())
                    attached |= AttachToVehicleSelection(mod, vehicleMenu);

                return attached;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Native UI scan skipped: {ex.GetType().Name}");
                return false;
            }
        }

        private static bool HasAttachedNativeUiRoot()
        {
            try
            {
                foreach (var vehicleMenu in Resources.FindObjectsOfTypeAll<VehicleSelectionUiController>())
                {
                    VisualElement root = FindVisualRoot(vehicleMenu);
                    if (root != null &&
                        (root.Q<VisualElement>(AlpineNativeUiConfig.RootName) != null ||
                         root.Q<VisualElement>(AlpineNativeUiConfig.GarageTuningButtonName) != null))
                        return true;
                }

            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Native UI attachment check skipped: {ex.GetType().Name}");
            }

            return false;
        }

        public static bool AttachToVehicleSelection(AlpineTuningMod mod, VehicleSelectionUiController controller)
        {
            return AttachToGarage(mod, controller);
        }

        private static bool AttachToGarage(AlpineTuningMod mod, VehicleSelectionUiController controller)
        {
            if (mod == null || controller == null)
                return false;

            VisualElement menuRoot = FindVisualRoot(controller);
            if (menuRoot == null)
                return false;

            int garageId = controller.GetInstanceID();
            if (GarageSessions.TryGetValue(garageId, out GarageNativeSession existingSession))
            {
                if (!existingSession.IsDisposed &&
                    ReferenceEquals(existingSession.Controller, controller) &&
                    ReferenceEquals(existingSession.TemplateRoot, menuRoot))
                    return false;

                existingSession.Dispose();
                GarageSessions.Remove(garageId);
                GarageRenderActions.Remove(garageId);
                GarageNativeCloseRequests.Remove(garageId);
            }

            if (menuRoot.Q<VisualElement>(AlpineNativeUiConfig.RootName) != null ||
                menuRoot.Q<VisualElement>(AlpineNativeUiConfig.GarageTuningButtonName) != null)
                return false;

            // The game's SUITuning action belongs to the selected sled, while its
            // top tab row is only a sled-class filter. Alpine therefore attaches
            // beside the selected-sled action instead of registering another class.
            ControlIndicatorButton styleButton =
                menuRoot.Q<ControlIndicatorButton>(AlpineNativeUiConfig.GarageStyleButtonName) ??
                SleddersGameBindings.GetFieldValue<ControlIndicatorButton>(controller, "IGCKHAEFCKN");
            ControlIndicatorButton nativeBackButton =
                menuRoot.Q<ControlIndicatorButton>("SUIBack") ??
                SleddersGameBindings.GetFieldValue<ControlIndicatorButton>(controller, "NLIHFELCHMJ");
            ControlIndicatorButton readyButton =
                menuRoot.Q<ControlIndicatorButton>("SUINetGameReady") ??
                SleddersGameBindings.GetFieldValue<ControlIndicatorButton>(controller, "FDLDGBGFFDH");

            // Do not commit a fallback control at the wrong hierarchy level while
            // the native selected-sled action bar is still being constructed. The
            // periodic attachment scan will retry once STYLE has a real parent.
            if (styleButton == null || styleButton.parent == null)
                return false;

            VisualElement actionParent = styleButton.parent;
            VisualElement controlInfo = menuRoot.Q<VisualElement>("ControlInfo") ?? actionParent;
            if (!ReferenceEquals(controlInfo, actionParent))
                controlInfo = actionParent;

            VisualElement nativeVehicleList = menuRoot.Q<VisualElement>("VehicleListContainer");
            VisualElement bottomDock = nativeVehicleList != null ? nativeVehicleList.parent : controlInfo.parent;
            bool nativeHostResolved = nativeVehicleList != null &&
                                      bottomDock != null &&
                                      ReferenceEquals(controlInfo.parent, bottomDock);
            if (!nativeHostResolved)
            {
                bottomDock = controlInfo != null ? controlInfo.parent : null;
                MelonLogger.Warning(
                    "Native garage rail host was not resolved; Alpine is using the transparent bottom-rail fallback.");
            }

            VisualElement selectionSidePanel = menuRoot.Q<VisualElement>("SelectionSidePanel");
            VisualElement selectionSidePanelBody = selectionSidePanel != null
                ? selectionSidePanel.Q<VisualElement>("CustomMenu") ?? selectionSidePanel
                : null;

            string originalStyleText = styleButton.DisplayText;
            styleButton.DisplayText = "STYLE";
            var nativeTuningButton = new ControlIndicatorButton
            {
                name = AlpineNativeUiConfig.GarageTuningButtonName,
                // Tertiary is the native spare action in ordinary garage use. The
                // activation handler below suppresses it dynamically whenever the
                // native Ready control is enabled.
                ActionName = "Tertiary",
                DisplayText = "TUNING",
                focusable = true
            };
            CopyClasses(styleButton, nativeTuningButton);
            Button tuningButton = nativeTuningButton;

            var session = new GarageNativeSession(
                controller,
                menuRoot,
                nativeVehicleList,
                bottomDock,
                controlInfo,
                selectionSidePanelBody,
                nativeHostResolved,
                nativeBackButton,
                styleButton,
                readyButton)
            {
                EntryButton = nativeTuningButton,
                StyleButton = styleButton,
                ReadyButton = readyButton,
                OriginalStyleText = originalStyleText
            };

            VisualElement surface = null;
            Action render = null;
            Action requestSurfaceClose = null;
            Func<bool> requestNativeClose = null;
            bool entryAvailable = mod.ResolveTargetSledContext(controller).HasSled;
            tuningButton.SetEnabled(entryAvailable);
            Action closeSurface = null;
            closeSurface = () =>
            {
                if (surface == null || !session.IsOpen)
                    return;

                session.Close();
                tuningButton.EnableInClassList("open", false);
                menuRoot.schedule.Execute(() =>
                {
                    if (!session.IsOpen && !session.IsDisposed)
                    {
                        entryAvailable = mod.ResolveTargetSledContext(controller).HasSled;
                        tuningButton.SetEnabled(entryAvailable);
                    }
                });
            };

            try
            {
                surface = CreateGarageTuningSurface(
                    mod,
                    controller,
                    session,
                    closeSurface,
                    readyButton,
                    nativeBackButton,
                    out render,
                    out requestSurfaceClose,
                    out requestNativeClose);
                surface.style.display = DisplayStyle.None;

                int actionIndex = Mathf.Clamp(actionParent.IndexOf(styleButton) + 1, 0, actionParent.childCount);
                actionParent.Insert(actionIndex, tuningButton);
                session.Mount(surface);
            }
            catch (Exception ex)
            {
                session.Dispose();
                styleButton.DisplayText = originalStyleText;
                MelonLogger.Warning($"Native garage tuning surface could not attach: {ex.GetType().Name}");
                return false;
            }

            Action renderSurface = render;
            render = () =>
            {
                entryAvailable = mod.ResolveTargetSledContext(controller).HasSled;
                if (!session.IsOpen)
                {
                    tuningButton.SetEnabled(entryAvailable);
                    return;
                }

                renderSurface?.Invoke();
            };

            GarageSessions[garageId] = session;
            GarageRenderActions[garageId] = render;
            GarageNativeCloseRequests[garageId] = requestNativeClose;
            menuRoot.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                bool ownedRegisteredSession = false;
                if (GarageSessions.TryGetValue(garageId, out GarageNativeSession registeredSession) &&
                    ReferenceEquals(registeredSession, session))
                {
                    GarageSessions.Remove(garageId);
                    ownedRegisteredSession = true;
                }
                if (GarageRenderActions.TryGetValue(garageId, out Action registered) && registered == render)
                    GarageRenderActions.Remove(garageId);
                if (GarageNativeCloseRequests.TryGetValue(garageId, out Func<bool> registeredClose) &&
                    registeredClose == requestNativeClose)
                {
                    GarageNativeCloseRequests.Remove(garageId);
                }
                session.Dispose();
                if (ownedRegisteredSession)
                    mod.ForgetGarageSelection(controller);
            });

            int lastToggleFrame = -1;
            Action toggleSurface = () =>
            {
                // A dynamically-created ControlIndicatorButton can receive both
                // the native UI callback and Alpine's explicit Y/Triangle fallback
                // in the same frame. Treat them as one activation.
                if (lastToggleFrame == Time.frameCount)
                    return;
                lastToggleFrame = Time.frameCount;

                bool open = !session.IsOpen;
                if (open)
                {
                    entryAvailable = mod.ResolveTargetSledContext(controller).HasSled;
                    if (!entryAvailable)
                    {
                        tuningButton.SetEnabled(false);
                        return;
                    }

                    tuningButton.EnableInClassList("open", true);
                    try
                    {
                        session.Open();
                        render?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Native garage tuning surface failed to open: {ex.GetType().Name}");
                        session.Close();
                        tuningButton.EnableInClassList("open", false);
                    }
                }
                else
                {
                    requestSurfaceClose?.Invoke();
                }
            };

            // Current Sledders ControlIndicatorButton inherits Button.clicked.
            // Its ControlIndicator constructor forwards the configured native action
            // (Tertiary = Y/Triangle) into that base click channel, so subscribing
            // once handles pointer, focus-submit, and controller input without
            // double-dispatching the same event.
            ((Button)nativeTuningButton).clicked += toggleSurface;
            session.DirectTuningShortcut = toggleSurface;

            return true;
        }

        /// <summary>
        /// Handles the garage-global controller shortcut explicitly. Sledders'
        /// dynamically-created ControlIndicatorButton shows the Tertiary glyph but
        /// does not reliably receive the global Tertiary action on every controller
        /// path. Unity's legacy joystick button 3 is Y on XInput and Triangle on
        /// PlayStation mappings, matching the game's displayed Tertiary control.
        /// Focused A/X submission still travels through Button.clicked normally.
        /// </summary>
        public static void UpdateGarageTuningShortcut()
        {
            GarageNativeSession target = null;
            foreach (GarageNativeSession session in GarageSessions.Values.ToArray())
            {
                if (session == null || session.IsDisposed || session.IsOpen ||
                    session.DirectTuningShortcut == null)
                {
                    continue;
                }

                ControlIndicatorButton entry = session.EntryButton;
                if (entry == null || entry.panel == null || !entry.enabledInHierarchy ||
                    !IsActuallyDisplayed(entry))
                {
                    continue;
                }

                // Multiplayer Ready also owns Tertiary when it is actually active.
                // Do not steal that native action.
                ControlIndicatorButton ready = session.ReadyButton;
                if (ready != null && ready.enabledInHierarchy && IsActuallyDisplayed(ready))
                    continue;

                target = session;
                break;
            }

            if (target == null || !GarageTertiaryPressed())
                return;

            target.DirectTuningShortcut();
        }

        private static bool GarageTertiaryPressed()
        {
            // Unity's generic joystick button 3 is the normal XInput Y /
            // PlayStation Triangle position and covers the common path directly.
            if (Input.GetKeyDown(KeyCode.JoystickButton3))
                return true;

            // Sledders routes its controller UI through Rewired. Dynamic UI
            // controls do not always get registered into Rewired's action dispatch,
            // so also ask the game's Player0 for the native Tertiary action. Keep
            // this reflection-only so Alpine does not gain a hard Rewired assembly
            // dependency and remains tolerant of loader/game packaging changes.
            try
            {
                if (_rewiredGaragePlayer == null || _rewiredGetButtonDownByName == null)
                {
                    if (Time.unscaledTime < _nextRewiredGarageResolveTime)
                        return false;

                    _nextRewiredGarageResolveTime = Time.unscaledTime + 1f;
                    Type reInputType = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(assembly => assembly.GetType("Rewired.ReInput", false))
                        .FirstOrDefault(type => type != null);
                    PropertyInfo playersProperty = reInputType?.GetProperty(
                        "players",
                        BindingFlags.Public | BindingFlags.Static);
                    object players = playersProperty?.GetValue(null, null);
                    MethodInfo getPlayer = players?.GetType().GetMethod(
                        "GetPlayer",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(int) },
                        null);
                    object player = getPlayer?.Invoke(players, new object[] { 0 });
                    MethodInfo getButtonDown = player?.GetType().GetMethod(
                        "GetButtonDown",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(string) },
                        null);

                    if (player == null || getButtonDown == null || getButtonDown.ReturnType != typeof(bool))
                        return false;

                    _rewiredGaragePlayer = player;
                    _rewiredGetButtonDownByName = getButtonDown;
                }

                object result = _rewiredGetButtonDownByName.Invoke(
                    _rewiredGaragePlayer,
                    new object[] { "Tertiary" });
                return result is bool pressed && pressed;
            }
            catch
            {
                _rewiredGaragePlayer = null;
                _rewiredGetButtonDownByName = null;
                _nextRewiredGarageResolveTime = Time.unscaledTime + 1f;
                return false;
            }
        }

        private static VisualElement CreateGarageTuningSurface(
            AlpineTuningMod mod,
            VehicleSelectionUiController controller,
            GarageNativeSession session,
            Action closeSurface,
            ControlIndicatorButton readyButton,
            ControlIndicatorButton actionClassAnchor,
            out Action renderAction,
            out Action requestSurfaceCloseAction,
            out Func<bool> requestNativeCloseAction)
        {
            var resolvedTarget = mod.ResolveTargetSledContext(controller);
            VehicleScriptableObject target = resolvedTarget.sled;
            TuneProfile working = target != null ? mod.CreateWorkingProfile(target) : null;
            TuneProfile installedReference = PreviewClone(mod, target, working);
            var navigation = new List<GarageNavigationNode>
            {
                new GarageNavigationNode(NavigationRoot, "tuning", "Tuning")
            };

            string librarySelectedProfileId = null;
            string pendingDeleteProfileId = null;
            string pendingLoadProfileId = null;
            bool factoryResetArmed = false;
            bool skipNavigationStateCapture = false;
            bool hasUnsavedChanges = working != null && working.setupEdited;
            bool exitPromptVisible = false;
            bool fuelOverflowPromptVisible = false;
            bool fuelOverflowAccepted = false;
            string fuelOverflowWarning = null;
            bool saveInProgress = false;
            bool closeGarageAfterPrompt = false;
            bool clearBindingArmed = false;
            int lastBackFrame = -1;

            var root = new VisualElement { name = AlpineNativeUiConfig.RootName };
            var chrome = new VisualElement { name = "AlpineGarageRailHeader" };
            chrome.AddToClassList("tab-buttons");
            chrome.AddToClassList("sledders-container");
            chrome.AddToClassList("sledders-container-no-padding");
            var breadcrumb = new Label { name = "AlpineBreadcrumb" };
            var status = new Label { name = "AlpineGarageStatus" };
            var rail = new SUIManagedList { name = "AlpineGarageHorizontalList" };
            var detailHost = new VisualElement { name = "AlpineGarageDetailHost" };
            var detailContent = new ScrollView(ScrollViewMode.Vertical) { name = "AlpineGarageDetailContent" };

            ApplyNativeGarageChromeStyle(root, chrome, breadcrumb, status, rail, detailHost, detailContent);
            chrome.Add(breadcrumb);
            chrome.Add(status);
            root.Add(chrome);
            root.Add(rail);
            detailHost.Add(detailContent);
            session.DetailHost = detailHost;

            Action render = null;
            Action refreshChrome = null;
            Action goBack = null;
            Action requestBack = null;
            Action refreshDyno = null;
            Action closeDyno = null;
            Action toggleDyno = null;
            ScrollView dynoContent = null;

            Action<string> setStatus = message => SetGarageStatus(status, message);
            setStatus(hasUnsavedChanges ? "Staged" : resolvedTarget.status);

            Action<TuneProfile> setWorking = profile =>
            {
                working = profile;
                pendingDeleteProfileId = null;
                pendingLoadProfileId = null;
                factoryResetArmed = false;
                if (working != null && target != null)
                    mod.PreviewProfile(working, target);
                hasUnsavedChanges = true;
                setStatus("Staged");
                refreshDyno?.Invoke();
            };

            Action setupChanged = () =>
            {
                if (working == null || target == null)
                    return;

                mod.PreviewProfile(working, target);
                hasUnsavedChanges = true;
                fuelOverflowAccepted = false;
                fuelOverflowPromptVisible = false;
                fuelOverflowWarning = null;
                factoryResetArmed = false;
                pendingDeleteProfileId = null;
                pendingLoadProfileId = null;
                setStatus("Staged");
                refreshDyno?.Invoke();
            };

            refreshDyno = () =>
            {
                if (session.DynoOverlay == null || session.DynoOverlay.panel == null)
                    return;
                PopulateGarageDyno(mod, target, working, dynoContent);
            };

            closeDyno = () =>
            {
                session.DynoOverlay?.RemoveFromHierarchy();
                session.DynoOverlay = null;
                dynoContent = null;
            };

            toggleDyno = () =>
            {
                if (session.DynoOverlay == null)
                {
                    session.DynoOverlay = CreateGarageDynoOverlay(
                        session.TemplateRoot,
                        () =>
                        {
                            closeDyno?.Invoke();
                        },
                        out dynoContent);
                    refreshDyno();
                }
                else
                {
                    closeDyno?.Invoke();
                }
            };

            Func<bool> saveSetup = () =>
            {
                if (target == null || working == null)
                    return false;

                if (saveInProgress)
                {
                    setStatus("Saving");
                    return false;
                }

                if (!hasUnsavedChanges)
                {
                    setStatus("Saved");
                    return true;
                }

                if (!fuelOverflowAccepted &&
                    mod.TryGetFuelCapacityOverflowWarning(working, target, out string overflowWarning))
                {
                    fuelOverflowWarning = overflowWarning;
                    fuelOverflowPromptVisible = true;
                    setStatus("Confirm fuel overflow");
                    return false;
                }

                saveInProgress = true;
                try
                {
                    factoryResetArmed = false;
                    pendingDeleteProfileId = null;
                    pendingLoadProfileId = null;
                    string message;
                    bool saved = mod.SaveCurrentSetupAsSlot(working, target, out message);
                    if (!saved)
                    {
                        if (string.IsNullOrWhiteSpace(message))
                            message = AlpineNativeUiConfig.ApplyFailedText;
                        setStatus(message);
                        return false;
                    }

                    // Persistence succeeds before the native recreation path runs.
                    // From this point the draft is clean even if recreation fails.
                    hasUnsavedChanges = false;
                    working.setupEdited = false;
                    installedReference = PreviewClone(mod, target, working);
                    if (!mod.Settings.alpineTuningEnabled)
                    {
                        message = "Saved · Alpine disabled · Vanilla runtime unchanged";
                    }
                    else if (mod.HasRuntimeInstanceForSled(target))
                    {
                        string reloadStatus;
                        bool reloaded = mod.ReloadSled(out reloadStatus);
                        message = reloaded
                            ? "Saved"
                            : (string.IsNullOrWhiteSpace(reloadStatus)
                                ? "Setup saved, but the sled could not be reloaded."
                                : "Setup saved. " + reloadStatus);
                    }
                    else
                    {
                        message = "Saved · Next ride";
                    }

                    if (string.IsNullOrWhiteSpace(message))
                        message = AlpineNativeUiConfig.ApplyFailedText;
                    fuelOverflowAccepted = false;
                    fuelOverflowPromptVisible = false;
                    fuelOverflowWarning = null;
                    setStatus(message);
                    refreshChrome?.Invoke();
                    refreshDyno?.Invoke();
                    return true;
                }
                finally
                {
                    saveInProgress = false;
                }
            };

            Func<bool> saveAsNewSetup = () =>
            {
                if (target == null || working == null || saveInProgress)
                    return false;

                saveInProgress = true;
                try
                {
                    factoryResetArmed = false;
                    pendingDeleteProfileId = null;
                    pendingLoadProfileId = null;
                    string message;
                    if (!mod.SaveCurrentSetupAsNewSlot(working, target, out message))
                    {
                        setStatus(string.IsNullOrWhiteSpace(message) ? AlpineNativeUiConfig.SaveFailedText : message);
                        return false;
                    }

                    hasUnsavedChanges = false;
                    working.setupEdited = false;
                    installedReference = PreviewClone(mod, target, working);
                    if (!mod.Settings.alpineTuningEnabled)
                    {
                        message = "Saved as new · Alpine disabled · Vanilla runtime unchanged";
                    }
                    else if (mod.HasRuntimeInstanceForSled(target))
                    {
                        string reloadStatus;
                        if (!mod.ReloadSled(out reloadStatus) && !string.IsNullOrWhiteSpace(reloadStatus))
                            message = reloadStatus;
                    }
                    setStatus(string.IsNullOrWhiteSpace(message) ? "Saved as new" : message);
                    refreshChrome?.Invoke();
                    refreshDyno?.Invoke();
                    return true;
                }
                finally
                {
                    saveInProgress = false;
                }
            };

            Action<TuneProfile, string, bool> acceptLoadedSetup = (equipped, successStatus, persisted) =>
            {
                if (equipped == null)
                    return;

                working = equipped;
                working.setupEdited = !persisted;
                hasUnsavedChanges = !persisted;
                exitPromptVisible = false;
                pendingDeleteProfileId = null;
                pendingLoadProfileId = null;
                factoryResetArmed = false;
                mod.PreviewProfile(working, target);
                installedReference = PreviewClone(mod, target, working);

                string message = successStatus;
                if (mod.HasRuntimeInstanceForSled(target) && working.requiresReload)
                {
                    string reloadStatus;
                    if (!mod.ReloadSled(out reloadStatus))
                    {
                        message = string.IsNullOrWhiteSpace(reloadStatus)
                            ? "Loaded · Reload failed"
                            : reloadStatus;
                    }
                }
                else if (!mod.HasRuntimeInstanceForSled(target))
                {
                    message += " · Next ride";
                }

                setStatus(message);
                refreshChrome?.Invoke();
                refreshDyno?.Invoke();
            };

            Func<TuneProfile, bool> loadSetupSlot = profile =>
            {
                TuneProfile equipped;
                string message;
                bool loaded = mod.EquipSetupSlot(profile, target, out equipped, out message);
                if (equipped != null)
                    acceptLoadedSetup(equipped, loaded ? "Loaded" : message, loaded);
                else
                    setStatus(string.IsNullOrWhiteSpace(message) ? "Load failed" : message);
                return loaded;
            };

            Func<TuneProfile, bool> setDefaultSetupSlot = profile =>
            {
                TuneProfile equipped;
                string message;
                bool saved = mod.SetDefaultSetup(profile, target, out equipped, out message);
                if (equipped != null)
                    acceptLoadedSetup(equipped, saved ? "Default set" : message, saved);
                else
                    setStatus(string.IsNullOrWhiteSpace(message) ? "Default failed" : message);
                return saved;
            };

            Action captureCurrentState = () =>
            {
                if (navigation.Count == 0)
                    return;

                GarageNavigationNode current = navigation[navigation.Count - 1];
                string focusedName = GarageFocusedElementName(root, detailHost, session);
                if (!string.IsNullOrWhiteSpace(focusedName))
                    current.FocusedElementName = focusedName;
                current.ScrollOffset = rail.scrollOffset;
                current.DetailScrollOffset = detailContent.scrollOffset;
            };

            Action refreshTarget = () =>
            {
                var refreshed = mod.ResolveTargetSledContext(controller);
                string previousKey = SledIdentity.StableIdentityKey(target);
                string nextKey = refreshed.identity != null ? refreshed.identity.StableKey : null;

                resolvedTarget = refreshed;
                if (refreshed.sled == null)
                {
                    closeDyno?.Invoke();
                    CancelHeadlightCaptureIfActive(mod);
                    if (hasUnsavedChanges && target != null && working != null)
                    {
                        setStatus("The native sled selection is temporarily unavailable. This unsaved draft is still preserved.");
                        return;
                    }

                    target = null;
                    working = null;
                    installedReference = null;
                    hasUnsavedChanges = false;
                    exitPromptVisible = false;
                    navigation.Clear();
                    navigation.Add(new GarageNavigationNode(NavigationRoot, "tuning", "Tuning"));
                    setStatus(refreshed.status);
                    return;
                }

                if (working != null && hasUnsavedChanges &&
                    !string.Equals(previousKey, nextKey, StringComparison.OrdinalIgnoreCase))
                {
                    closeDyno?.Invoke();
                    // The hidden native sled list should normally prevent this. If a
                    // rebuild notification races the garage, keep the draft bound to
                    // its original sled instead of silently applying it to another one.
                    setStatus("The selected sled changed while this setup has unsaved changes. Save or discard it before switching.");
                    return;
                }

                if (working == null || !string.Equals(previousKey, nextKey, StringComparison.OrdinalIgnoreCase))
                {
                    closeDyno?.Invoke();
                    target = refreshed.sled;
                    working = mod.CreateWorkingProfile(target);
                    installedReference = PreviewClone(mod, target, working);
                    navigation.Clear();
                    navigation.Add(new GarageNavigationNode(NavigationRoot, "tuning", "Tuning"));
                    librarySelectedProfileId = null;
                    pendingDeleteProfileId = null;
                    pendingLoadProfileId = null;
                    factoryResetArmed = false;
                    hasUnsavedChanges = false;
                    exitPromptVisible = false;
                    setStatus(refreshed.status);
                }
                else
                {
                    target = refreshed.sled;
                }
            };

            Action<string, string, string> navigate = (kind, id, title) =>
            {
                captureCurrentState();
                navigation.Add(new GarageNavigationNode(kind, id, title));
                pendingDeleteProfileId = null;
                pendingLoadProfileId = null;
                factoryResetArmed = false;
                clearBindingArmed = false;
                skipNavigationStateCapture = true;
                render?.Invoke();
            };

            Action<string, string> selectPart = (partCategory, partId) =>
            {
                if (working == null || string.IsNullOrWhiteSpace(partCategory) || string.IsNullOrWhiteSpace(partId))
                    return;

                string previousPartId = working.GetPartId(partCategory);
                if (string.Equals(previousPartId, partId, StringComparison.OrdinalIgnoreCase))
                {
                    setStatus("Selected");
                    return;
                }

                captureCurrentState();
                working.SetPartId(partCategory, partId);
                setupChanged();
                skipNavigationStateCapture = true;
                render?.Invoke();
            };

            goBack = () =>
            {
                if (session.DynoOverlay != null)
                {
                    toggleDyno?.Invoke();
                    return;
                }

                pendingDeleteProfileId = null;
                pendingLoadProfileId = null;
                factoryResetArmed = false;
                if (fuelOverflowPromptVisible)
                {
                    fuelOverflowPromptVisible = false;
                    fuelOverflowAccepted = false;
                    fuelOverflowWarning = null;
                    setStatus("Capacity change cancelled");
                    skipNavigationStateCapture = true;
                    render?.Invoke();
                    return;
                }
                if (exitPromptVisible)
                {
                    exitPromptVisible = false;
                    closeGarageAfterPrompt = false;
                    skipNavigationStateCapture = true;
                    render?.Invoke();
                    return;
                }

                if (navigation.Count > 1)
                {
                    navigation.RemoveAt(navigation.Count - 1);
                    skipNavigationStateCapture = true;
                    render?.Invoke();
                }
                else if (hasUnsavedChanges)
                {
                    captureCurrentState();
                    closeGarageAfterPrompt = false;
                    exitPromptVisible = true;
                    skipNavigationStateCapture = true;
                    render?.Invoke();
                }
                else
                {
                    captureCurrentState();
                    closeSurface?.Invoke();
                }
            };

            requestBack = () =>
            {
                // Cancel, NavigationCancel and Escape may all be raised for one
                // physical press. One frame is one navigation pop.
                if (lastBackFrame == Time.frameCount)
                    return;

                lastBackFrame = Time.frameCount;
                if (mod.IsCapturingHeadlightBinding ||
                    mod.WasHeadlightBindingCancelHandledThisFrame)
                {
                    if (mod.IsCapturingHeadlightBinding)
                        mod.CancelHeadlightBindingCapture();
                    mod.ConsumeHeadlightBindingCaptureResult();
                    setStatus("Binding cancelled");
                    skipNavigationStateCapture = true;
                    render?.Invoke();
                    return;
                }
                if (clearBindingArmed)
                {
                    clearBindingArmed = false;
                    setStatus("Clear cancelled");
                    skipNavigationStateCapture = true;
                    render?.Invoke();
                    return;
                }
                goBack();
            };

            render = () =>
            {
                if (!session.IsOpen || session.IsDisposed)
                    return;

                session.ReassertOpenOwnership();

                if (!skipNavigationStateCapture)
                    captureCurrentState();
                skipNavigationStateCapture = false;

                refreshTarget();
                if (exitPromptVisible || fuelOverflowPromptVisible || factoryResetArmed || clearBindingArmed ||
                    !string.IsNullOrWhiteSpace(pendingDeleteProfileId) ||
                    !string.IsNullOrWhiteSpace(pendingLoadProfileId) ||
                    mod.IsCapturingHeadlightBinding)
                {
                    closeDyno?.Invoke();
                }
                rail.Clear();
                detailContent.Clear();

                if (target == null || working == null)
                {
                    breadcrumb.text = "TUNING";
                    setStatus(resolvedTarget.status);
                    Button unavailable = GarageTile(
                        "SELECT A SLED",
                        "Alpine tunes the sled selected by the native garage.",
                        false,
                        null,
                        "action.unavailable");
                    unavailable.SetEnabled(false);
                    rail.Add(unavailable);
                    session.ShowDetails(false);
                    session.SetContextActions(
                        "Back", requestBack,
                        null, null,
                        null, null,
                        actionClassAnchor, readyButton);
                    RestoreNativeGarageNavigationState(
                        controller, session, root, detailHost, rail, detailContent,
                        navigation.Count > 0 ? navigation[navigation.Count - 1] : null,
                        Array.Empty<Button>());
                    return;
                }

                GarageNavigationNode current = navigation[navigation.Count - 1];
                if (fuelOverflowPromptVisible)
                {
                    closeDyno?.Invoke();
                    breadcrumb.text = "TUNING  >  FUEL CAPACITY WARNING";
                    var promptButtons = new List<Button>();
                    Action cancelOverflow = () =>
                    {
                        fuelOverflowPromptVisible = false;
                        fuelOverflowAccepted = false;
                        fuelOverflowWarning = null;
                        setStatus("Capacity change cancelled");
                        skipNavigationStateCapture = true;
                        render?.Invoke();
                    };
                    Action confirmOverflow = () =>
                    {
                        fuelOverflowAccepted = true;
                        fuelOverflowPromptVisible = false;
                        bool saved = saveSetup();
                        if (!saved)
                        {
                            fuelOverflowAccepted = false;
                            render?.Invoke();
                            return;
                        }

                        if (exitPromptVisible)
                        {
                            exitPromptVisible = false;
                            bool closeNativeGarage = closeGarageAfterPrompt;
                            closeGarageAfterPrompt = false;
                            closeSurface?.Invoke();
                            if (closeNativeGarage)
                                controller.Close();
                            return;
                        }
                        skipNavigationStateCapture = true;
                        render?.Invoke();
                    };

                    Button confirmTile = GarageTile(
                        "CONFIRM CAPACITY CHANGE",
                        "Keep the litres that fit in the new tank and permanently lose overflow.",
                        false, confirmOverflow, "action.save");
                    Button cancelTile = GarageTile(
                        "CANCEL",
                        "Return without changing or losing fuel.",
                        false, cancelOverflow, "action.continue");
                    confirmTile.name = "AlpineFuelOverflow-Confirm";
                    cancelTile.name = "AlpineFuelOverflow-Cancel";
                    rail.Add(confirmTile);
                    rail.Add(cancelTile);
                    promptButtons.Add(confirmTile);
                    promptButtons.Add(cancelTile);

                    var prompt = Section("Fuel Overflow");
                    prompt.Add(MutedLabel(fuelOverflowWarning ?? "The selected tank cannot hold all current fuel."));
                    detailContent.Add(prompt);
                    session.ShowDetails(true);
                    session.SetContextActions(
                        "Cancel", cancelOverflow,
                        "Confirm", confirmOverflow,
                        null, null,
                        actionClassAnchor, readyButton);
                    var promptNavigation = new GarageNavigationNode(
                        NavigationPanel, "fuel-overflow-prompt", "Fuel Capacity Warning");
                    RestoreNativeGarageNavigationState(
                        controller, session, root, detailHost, rail, detailContent,
                        promptNavigation, promptButtons);
                    return;
                }

                if (exitPromptVisible)
                {
                    closeDyno?.Invoke();
                    breadcrumb.text = "TUNING  >  UNSAVED CHANGES";
                    var promptButtons = new List<Button>();
                    Action continueTuning = () =>
                    {
                        exitPromptVisible = false;
                        closeGarageAfterPrompt = false;
                        skipNavigationStateCapture = true;
                        render?.Invoke();
                    };
                    Action saveAndExit = () =>
                    {
                        if (!saveSetup())
                        {
                            render?.Invoke();
                            return;
                        }

                        exitPromptVisible = false;
                        bool closeNativeGarage = closeGarageAfterPrompt;
                        closeGarageAfterPrompt = false;
                        closeSurface?.Invoke();
                        if (closeNativeGarage)
                            controller.Close();
                    };
                    Action exitWithoutSaving = () =>
                    {
                        // The surface and its closures survive while the native
                        // garage remains open. Recreate the draft now so discarded
                        // choices cannot reappear (or be saved by a later edit) when
                        // TUNING is opened again.
                        try
                        {
                            working = target != null ? mod.CreateWorkingProfile(target) : null;
                            installedReference = PreviewClone(mod, target, working);
                        }
                        catch (Exception ex)
                        {
                            working = null;
                            MelonLogger.Warning($"Discarded garage draft could not be reloaded immediately: {ex.GetType().Name}");
                        }
                        librarySelectedProfileId = null;
                        pendingDeleteProfileId = null;
                        pendingLoadProfileId = null;
                        factoryResetArmed = false;
                        navigation.Clear();
                        navigation.Add(new GarageNavigationNode(NavigationRoot, "tuning", "Tuning"));
                        skipNavigationStateCapture = true;
                        hasUnsavedChanges = false;
                        exitPromptVisible = false;
                        bool closeNativeGarage = closeGarageAfterPrompt;
                        closeGarageAfterPrompt = false;
                        closeSurface?.Invoke();
                        if (closeNativeGarage)
                            controller.Close();
                    };

                    Button saveTile = GarageTile(
                        "SAVE AND EXIT",
                        "Install this setup, reload the live sled when present, and close tuning.",
                        false,
                        saveAndExit,
                        "action.save");
                    saveTile.name = "AlpineExit-Save";
                    rail.Add(saveTile);
                    promptButtons.Add(saveTile);

                    Button continueTile = GarageTile(
                        "CONTINUE TUNING",
                        "Return to the setup without saving or discarding it.",
                        false,
                        continueTuning,
                        "action.continue");
                    continueTile.name = "AlpineExit-Continue";
                    rail.Add(continueTile);
                    promptButtons.Add(continueTile);

                    Button discardTile = GarageTile(
                        "EXIT WITHOUT SAVING",
                        "Discard the draft and keep the installed setup.",
                        false,
                        exitWithoutSaving,
                        "action.discard");
                    discardTile.name = "AlpineExit-Discard";
                    rail.Add(discardTile);
                    promptButtons.Add(discardTile);

                    var prompt = Section("Unsaved Changes");
                    prompt.Add(MutedLabel("Save, keep editing, or discard."));
                    detailContent.Add(prompt);
                    session.ShowDetails(true);
                    session.SetContextActions(
                        "Continue", continueTuning,
                        "Save & Exit", saveAndExit,
                        "Exit Without Saving", exitWithoutSaving,
                        actionClassAnchor, readyButton);
                    var promptNavigation = new GarageNavigationNode(
                        NavigationPanel, "exit-prompt", "Unsaved Changes");
                    RestoreNativeGarageNavigationState(
                        controller, session, root, detailHost, rail, detailContent,
                        promptNavigation, promptButtons);
                    return;
                }

                mod.PreviewProfile(working, target);
                breadcrumb.text = string.Join("  >  ", navigation.Select(node => node.Title.ToUpperInvariant()).ToArray());

                var tileButtons = new List<Button>();
                // Alpine owns the native information card for every tuning node.
                // Restoring the native card inside a category lets Sledders redraw
                // unrelated vehicle information over the tuning context.
                bool showDetails = true;
                try
                {
                    switch (current.Kind)
                    {
                        case NavigationCategory:
                            BuildGarageCategory(
                                mod, rail, detailContent, target, working, installedReference,
                                current.Id, navigate, tileButtons);
                            break;

                        case NavigationPart:
                            if (string.Equals(current.Id, "engine.donor", StringComparison.OrdinalIgnoreCase))
                            {
                                BuildGarageEnginePicker(
                                    mod, rail, target, working, installedReference, setupChanged, render,
                                    setStatus, tileButtons, detailContent);
                            }
                            else
                            {
                                BuildGaragePartPicker(
                                    mod, rail, target, working, installedReference, current.Id, selectPart, setupChanged,
                                    render, setStatus, tileButtons, detailContent);
                            }
                            break;

                        case NavigationPanel:
                            BuildGarageFocusedPanel(
                                mod,
                                detailContent,
                                target,
                                working,
                                current.Id,
                                setWorking,
                                render,
                                setupChanged,
                                setStatus,
                                saveAsNewSetup,
                                () => hasUnsavedChanges,
                                loadSetupSlot,
                                setDefaultSetupSlot,
                                () => librarySelectedProfileId,
                                id => librarySelectedProfileId = id,
                                () => pendingDeleteProfileId,
                                id => pendingDeleteProfileId = id,
                                () => pendingLoadProfileId,
                                value => pendingLoadProfileId = value,
                                () => factoryResetArmed,
                                value => factoryResetArmed = value,
                                tileButtons,
                                rail,
                                navigate,
                                () => clearBindingArmed,
                                value => clearBindingArmed = value,
                                closeDyno);
                            break;

                        case NavigationRoot:
                        default:
                            BuildGarageRoot(rail, navigate, tileButtons);
                            BuildGarageLandingSummary(mod, detailContent, target, working);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    closeDyno?.Invoke();
                    MelonLogger.Warning($"Garage tuning node '{current.Id}' render skipped: {ex.GetType().Name}");
                    rail.Clear();
                    detailContent.Clear();
                    Button unavailable = GarageTile(
                        "PANEL UNAVAILABLE",
                        "This view could not be rendered. Back remains available.",
                        false,
                        null,
                        "action.unavailable");
                    unavailable.name = "AlpinePanel-Unavailable";
                    unavailable.SetEnabled(false);
                    rail.Add(unavailable);
                    detailContent.Add(MutedLabel("This view is unavailable. Use Back."));
                    showDetails = true;
                    setStatus("Unavailable");
                }

                session.ShowDetails(showDetails);
                refreshDyno?.Invoke();
                if (clearBindingArmed)
                {
                    session.SetContextActions(
                        "Cancel Clear", requestBack,
                        null, null,
                        null, null,
                        actionClassAnchor,
                        readyButton);
                }
                else
                {
                    BuildNativeGarageContextActions(
                        mod,
                        session,
                        target,
                        working,
                        current,
                        navigation.Count,
                        requestBack,
                        saveSetup,
                        setupChanged,
                        () => navigate(NavigationPanel, "setups", "Setups"),
                        render,
                        setStatus,
                        actionClassAnchor,
                        readyButton,
                        toggleDyno,
                        session.DynoOverlay != null);
                }
                RestoreNativeGarageNavigationState(
                    controller, session, root, detailHost, rail, detailContent,
                    current, tileButtons);
            };

            refreshChrome = () =>
            {
                if (target == null || working == null)
                    return;
                try
                {
                    mod.PreviewProfile(working, target);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Garage tuning preview refresh skipped: {ex.GetType().Name}");
                }
            };

            root.RegisterCallback<FocusInEvent>(evt =>
            {
                if (!session.IsOpen)
                    return;
                VisualElement focused = evt.target as VisualElement;
                if (focused != null && IsDescendantOf(focused, rail.contentContainer))
                    CenterNativeGarageTile(controller, rail, focused);
            });
            detailHost.RegisterCallback<FocusInEvent>(evt =>
            {
                if (!session.IsOpen)
                    return;
                VisualElement focused = evt.target as VisualElement;
                if (focused == null || !IsDescendantOf(focused, detailContent.contentContainer))
                    return;
                detailHost.schedule.Execute(() =>
                {
                    if (session.IsOpen && focused.panel == detailHost.panel)
                        detailContent.ScrollTo(focused);
                });
            });

            RegisterGarageBackHandlers(root, session, requestBack);
            RegisterGarageBackHandlers(detailHost, session, requestBack);
            RegisterGarageBackHandlers(session.ControlInfo, session, requestBack);

            renderAction = render;
            requestSurfaceCloseAction = () =>
            {
                if (!session.IsOpen)
                    return;
                if (CancelHeadlightCaptureIfActive(mod))
                    setStatus("Binding cancelled");
                if (!hasUnsavedChanges)
                {
                    closeSurface?.Invoke();
                    return;
                }

                captureCurrentState();
                closeGarageAfterPrompt = false;
                exitPromptVisible = true;
                skipNavigationStateCapture = true;
                render?.Invoke();
            };
            requestNativeCloseAction = () =>
            {
                if (CancelHeadlightCaptureIfActive(mod))
                    setStatus("Binding cancelled");
                if (!session.IsOpen || !hasUnsavedChanges)
                {
                    // A native Close raised synchronously during Save/reload has
                    // already consumed the pending continuation. Clearing this
                    // prevents Save & Exit from issuing a second native Close.
                    closeGarageAfterPrompt = false;
                    closeSurface?.Invoke();
                    return true;
                }

                captureCurrentState();
                closeGarageAfterPrompt = true;
                exitPromptVisible = true;
                skipNavigationStateCapture = true;
                render?.Invoke();
                return false;
            };
            return root;
        }

        private static void ApplyNativeGarageRailStyle(VisualElement root, bool fallback)
        {
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexShrink = 0f;
            root.style.minWidth = 0f;
            root.style.backgroundColor = Color.clear;

            if (fallback)
            {
                root.style.position = Position.Absolute;
                root.style.left = 32f;
                root.style.right = 32f;
                root.style.bottom = 15f;
                return;
            }

            root.style.marginLeft = 32f;
            root.style.marginRight = 32f;
            root.style.marginBottom = 15f;
        }

        private static void ApplyFallbackGarageDetailStyle(VisualElement detailHost)
        {
            detailHost.style.position = Position.Absolute;
            detailHost.style.left = 32f;
            detailHost.style.top = 32f;
            detailHost.style.width = Length.Percent(36f);
            detailHost.style.height = Length.Percent(60f);
            detailHost.style.backgroundColor = Color.clear;
        }

        private static void ApplyNativeGarageChromeStyle(
            VisualElement root,
            VisualElement chrome,
            Label breadcrumb,
            Label status,
            SUIManagedList rail,
            VisualElement detailHost,
            ScrollView detailContent)
        {
            root.style.flexDirection = FlexDirection.Column;
            root.style.backgroundColor = Color.clear;

            chrome.style.flexDirection = FlexDirection.Row;
            chrome.style.alignItems = Align.Center;
            chrome.style.alignSelf = Align.Stretch;
            chrome.style.width = Length.Percent(100f);
            chrome.style.maxWidth = Length.Percent(100f);
            chrome.style.flexShrink = 0f;
            chrome.style.minWidth = 0f;
            chrome.style.marginLeft = 0f;
            chrome.style.marginRight = 0f;
            chrome.style.marginTop = 0f;
            chrome.style.marginBottom = 0f;

            breadcrumb.style.flexGrow = 1f;
            breadcrumb.style.flexShrink = 1f;
            breadcrumb.style.minWidth = 0f;
            breadcrumb.style.maxWidth = Length.Percent(100f);
            breadcrumb.style.color = AlpineNativeUiConfig.AccentColor;
            breadcrumb.style.unityFontStyleAndWeight = FontStyle.Bold;

            // The native rail can be much wider than its intrinsic header
            // content. A shrinking, percentage-capped status therefore collapsed
            // to a few right-aligned letters on the landing page. Reserve enough
            // room for every intentionally compact state while allowing the
            // breadcrumb to yield the remaining width.
            status.style.flexGrow = 0f;
            status.style.flexShrink = 0f;
            status.style.width = 132f;
            status.style.minWidth = 132f;
            status.style.maxWidth = 132f;
            status.style.marginLeft = AlpineNativeUiConfig.InlineGap;
            status.style.color = AlpineNativeUiConfig.StatusTextColor;
            status.style.unityTextAlign = TextAnchor.MiddleRight;
            status.style.whiteSpace = WhiteSpace.NoWrap;
            status.style.overflow = Overflow.Hidden;

            rail.style.flexShrink = 0f;
            rail.style.minWidth = 0f;
            rail.style.backgroundColor = Color.clear;

            detailHost.style.flexDirection = FlexDirection.Column;
            detailHost.style.flexGrow = 1f;
            detailHost.style.flexShrink = 1f;
            detailHost.style.alignSelf = Align.Stretch;
            detailHost.style.width = Length.Percent(100f);
            detailHost.style.maxWidth = Length.Percent(100f);
            detailHost.style.minWidth = 0f;
            detailHost.style.minHeight = 0f;
            detailHost.style.backgroundColor = Color.clear;
            detailHost.style.overflow = Overflow.Hidden;

            detailContent.style.flexGrow = 1f;
            detailContent.style.flexShrink = 1f;
            detailContent.style.alignSelf = Align.Stretch;
            detailContent.style.width = Length.Percent(100f);
            detailContent.style.maxWidth = Length.Percent(100f);
            detailContent.style.minWidth = 0f;
            detailContent.style.minHeight = 0f;
            detailContent.style.backgroundColor = Color.clear;
            detailContent.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            detailContent.verticalScrollerVisibility = ScrollerVisibility.Auto;
            detailContent.contentContainer.style.flexDirection = FlexDirection.Column;
            detailContent.contentContainer.style.width = Length.Percent(100f);
            detailContent.contentContainer.style.maxWidth = Length.Percent(100f);
            detailContent.contentContainer.style.minWidth = 0f;
        }

        private static void BuildNativeGarageContextActions(
            AlpineTuningMod mod,
            GarageNativeSession session,
            VehicleScriptableObject target,
            TuneProfile working,
            GarageNavigationNode current,
            int depth,
            Action goBack,
            Func<bool> saveSetup,
            Action setupChanged,
            Action openSetups,
            Action render,
            Action<string> setStatus,
            ControlIndicatorButton classAnchor,
            ControlIndicatorButton readyButton,
            Action toggleDyno,
            bool dynoOpen)
        {
            if (mod != null && mod.IsCapturingHeadlightBinding)
            {
                Action cancelBinding = () =>
                {
                    mod.CancelHeadlightBindingCapture();
                    mod.ConsumeHeadlightBindingCaptureResult();
                    setStatus?.Invoke("Binding cancelled");
                    render?.Invoke();
                };
                session.SetContextActions(
                    "Cancel Binding", cancelBinding,
                    null, null,
                    null, null,
                    classAnchor,
                    readyButton);
                return;
            }

            string secondaryLabel = "Save";
            Action secondary = () =>
            {
                saveSetup?.Invoke();
                render?.Invoke();
            };

            string tertiaryLabel = null;
            Action tertiary = null;
            if (depth <= 1)
            {
                tertiaryLabel = "Setups";
                tertiary = openSetups;
            }
            else if (CanResetGarageNode(current))
            {
                tertiaryLabel = "Reset";
                tertiary = () =>
                {
                    string message;
                    if (!ResetGarageNode(mod, current, working, out message))
                    {
                        setStatus?.Invoke(message);
                        render?.Invoke();
                        return;
                    }

                    setupChanged?.Invoke();
                    setStatus?.Invoke("Reset staged");
                    render?.Invoke();
                };
            }

            session.SetContextActions(
                "Back",
                goBack,
                secondaryLabel,
                secondary,
                tertiaryLabel,
                tertiary,
                classAnchor,
                readyButton,
                dynoOpen ? "Close Dyno" : "Dyno",
                toggleDyno);
        }

        private static void RestoreNativeGarageNavigationState(
            VehicleSelectionUiController controller,
            GarageNativeSession session,
            VisualElement root,
            VisualElement detailHost,
            SUIManagedList rail,
            ScrollView detailContent,
            GarageNavigationNode node,
            IReadOnlyList<Button> preferredTiles)
        {
            if (root == null || rail == null)
                return;

            string focusedElementName = node != null ? node.FocusedElementName : null;
            Vector2 railOffset = node != null ? node.ScrollOffset : Vector2.zero;
            Vector2 detailOffset = node != null ? node.DetailScrollOffset : Vector2.zero;
            root.schedule.Execute(() =>
            {
                if (!session.IsOpen || root.panel == null)
                    return;

                rail.scrollOffset = railOffset;
                if (detailContent != null)
                    detailContent.scrollOffset = detailOffset;

                VisualElement focusTarget = !string.IsNullOrWhiteSpace(focusedElementName)
                    ? root.Q<VisualElement>(focusedElementName) ??
                      detailHost?.Q<VisualElement>(focusedElementName) ??
                      session.FindContextAction(focusedElementName)
                    : null;
                if (!CanFocus(focusTarget) && preferredTiles != null)
                    focusTarget = preferredTiles.FirstOrDefault(CanFocus);
                if (!CanFocus(focusTarget))
                    focusTarget = FindFirstFocusable(detailContent?.contentContainer);
                if (!CanFocus(focusTarget))
                    focusTarget = session.FirstContextAction();

                if (!CanFocus(focusTarget))
                    return;

                focusTarget.Focus();
                if (IsDescendantOf(focusTarget, rail.contentContainer))
                    CenterNativeGarageTile(controller, rail, focusTarget);
                else if (detailContent != null &&
                         IsDescendantOf(focusTarget, detailContent.contentContainer))
                    detailContent.ScrollTo(focusTarget);
            });
        }

        private static void CenterNativeGarageTile(
            VehicleSelectionUiController controller,
            ScrollView rail,
            VisualElement tile)
        {
            if (rail == null || tile == null)
                return;

            try
            {
                Type extensions = typeof(VehicleSelectionUiController).Assembly.GetType("AODCFNNMNDL", false);
                MethodInfo centering = extensions?.GetMethod(
                    "GLINNFGELJD",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(ScrollView), typeof(VisualElement) },
                    null);
                IEnumerator routine = centering?.Invoke(null, new object[] { rail, tile }) as IEnumerator;
                if (routine != null && controller != null)
                {
                    controller.StartCoroutine(routine);
                    return;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Native garage tile centering fell back to ScrollTo: {ex.GetType().Name}");
            }

            rail.schedule.Execute(() =>
            {
                if (rail.panel != null && tile.panel == rail.panel)
                    rail.ScrollTo(tile);
            });
        }

        private static void RegisterGarageBackHandlers(
            VisualElement host,
            GarageNativeSession session,
            Action requestBack)
        {
            if (host == null)
                return;

            host.RegisterCallback<NavigationCancelEvent>(evt =>
            {
                if (!session.IsOpen)
                    return;
                requestBack?.Invoke();
                evt.StopImmediatePropagation();
            });
            host.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (!session.IsOpen ||
                    (evt.keyCode != KeyCode.Escape && evt.keyCode != KeyCode.Backspace))
                {
                    return;
                }
                if (evt.keyCode == KeyCode.Backspace &&
                    IsInsideTextField(evt.target as VisualElement))
                {
                    return;
                }

                requestBack?.Invoke();
                evt.StopImmediatePropagation();
            });
        }

        private static VisualElement FocusedElement(VisualElement root)
        {
            try
            {
                return root != null && root.panel != null
                    ? root.panel.focusController.focusedElement as VisualElement
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static string GarageFocusedElementName(
            VisualElement root,
            VisualElement detailHost,
            GarageNativeSession session)
        {
            VisualElement focused = FocusedElement(root);
            if (focused == null || string.IsNullOrWhiteSpace(focused.name))
                return null;
            if (IsDescendantOf(focused, root) || IsDescendantOf(focused, detailHost) ||
                ReferenceEquals(session.FindContextAction(focused.name), focused))
            {
                return focused.name;
            }
            return null;
        }

        private static void BuildGarageRoot(
            SUIManagedList rail,
            Action<string, string, string> navigate,
            List<Button> tileButtons)
        {
            AddGarageNavigationTile(rail, tileButtons, "Engine", "Engine internals, intake, induction and engine swaps.",
                NavigationCategory, "engine", "Engine", navigate, "root.engine");
            AddGarageNavigationTile(rail, tileButtons, "Drivetrain", "Clutch calibration, weights and gearing.",
                NavigationCategory, "drivetrain", "Drivetrain", navigate, "root.drivetrain");
            AddGarageNavigationTile(rail, tileButtons, "Suspension", "Shocks, springs, limiter, chassis and balance.",
                NavigationCategory, "suspension", "Suspension", navigate, "root.suspension");
            AddGarageNavigationTile(rail, tileButtons, "Track", "Choose the installed track package and snow-bite profile.",
                NavigationPart, PartCatalog.Track, "Track", navigate, "root.track");
            AddGarageNavigationTile(rail, tileButtons, "Steering", "Skis, stance and conservative steering geometry.",
                NavigationCategory, "steering", "Steering", navigate, "root.steering");
            AddGarageNavigationTile(rail, tileButtons, "Lighting", "Color, output, beam, aim and operating mode.",
                NavigationCategory, "lighting", "Lighting", navigate, "root.lighting");
            AddGarageNavigationTile(rail, tileButtons, "Fuel", "Tank capacity, backpack reserve and expedition range.",
                NavigationCategory, "fuel", "Fuel", navigate, "root.fuel");
            AddGarageNavigationTile(rail, tileButtons, "Settings", "Runtime, fuel, display and headlight options.",
                NavigationPanel, "settings", "Settings", navigate, "action.settings", null, false);
        }

        private static void BuildGarageLandingSummary(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working)
        {
            content.Clear();
            var section = Section("Setup Comparison");
            var header = GarageComparisonRow("STOCK", string.Empty, "CURRENT", true);
            section.Add(header);

            GarageComparisonSnapshot comparison = BuildGarageComparisonSnapshot(mod, target, working, null);
            ResolvedStats stock = comparison?.Factory;
            ResolvedStats current = comparison?.Current;
            AlpineDisplayUnits units = mod != null ? mod.Settings.units : AlpineDisplayUnits.Metric;

            string missing = "—";
            string powerUnit = units == AlpineDisplayUnits.Imperial ? "HP" : "KW";
            string stockPower = stock != null
                ? (units == AlpineDisplayUnits.Imperial
                    ? stock.horsePower.ToString("F0")
                    : UnitConversion.HorsepowerToKilowatts(stock.horsePower).ToString("F0"))
                : missing;
            string currentPower = current != null
                ? (units == AlpineDisplayUnits.Imperial
                    ? current.horsePower.ToString("F0")
                    : UnitConversion.HorsepowerToKilowatts(current.horsePower).ToString("F0"))
                : missing;
            section.Add(GarageComparisonRow(stockPower, powerUnit, currentPower));

            string paddleUnit = units == AlpineDisplayUnits.Imperial ? "LUGS IN" : "LUGS MM";
            string stockPaddle = stock != null
                ? (units == AlpineDisplayUnits.Imperial
                    ? UnitConversion.MillimetersToInches(stock.lugHeight).ToString("F2")
                    : stock.lugHeight.ToString("F0"))
                : missing;
            string currentPaddle = current != null
                ? (units == AlpineDisplayUnits.Imperial
                    ? UnitConversion.MillimetersToInches(current.lugHeight).ToString("F2")
                    : current.lugHeight.ToString("F0"))
                : missing;
            section.Add(GarageComparisonRow(stockPaddle, paddleUnit, currentPaddle));

            string weightUnit = units == AlpineDisplayUnits.Imperial ? "LB" : "KG";
            string stockWeight = stock != null
                ? (units == AlpineDisplayUnits.Imperial
                    ? UnitConversion.KilogramsToPounds(stock.weight).ToString("F0")
                    : stock.weight.ToString("F0"))
                : missing;
            string currentWeight = current != null
                ? (units == AlpineDisplayUnits.Imperial
                    ? UnitConversion.KilogramsToPounds(current.weight).ToString("F0")
                    : current.weight.ToString("F0"))
                : missing;
            section.Add(GarageComparisonRow(stockWeight, weightUnit, currentWeight));

            string stanceUnit = units == AlpineDisplayUnits.Imperial ? "STANCE IN" : "STANCE MM";
            string stockStance = stock != null
                ? (units == AlpineDisplayUnits.Imperial
                    ? UnitConversion.MillimetersToInches(stock.skiStance).ToString("F1")
                    : stock.skiStance.ToString("F0"))
                : missing;
            string currentStance = current != null
                ? (units == AlpineDisplayUnits.Imperial
                    ? UnitConversion.MillimetersToInches(current.skiStance).ToString("F1")
                    : current.skiStance.ToString("F0"))
                : missing;
            section.Add(GarageComparisonRow(stockStance, stanceUnit, currentStance));

            content.Add(section);
        }

        private static VisualElement GarageComparisonRow(
            string stock,
            string label,
            string current,
            bool header = false)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.alignSelf = Align.Stretch;
            row.style.width = Length.Percent(100f);
            row.style.maxWidth = Length.Percent(100f);
            row.style.minWidth = 0f;
            row.style.minHeight = header ? 22f : 26f;

            Label left = GarageComparisonCell(stock, TextAnchor.MiddleLeft, header);
            Label center = GarageComparisonCell(label, TextAnchor.MiddleCenter, header);
            Label right = GarageComparisonCell(current, TextAnchor.MiddleRight, header);
            left.style.width = Length.Percent(33f);
            center.style.width = Length.Percent(34f);
            right.style.width = Length.Percent(33f);
            row.Add(left);
            row.Add(center);
            row.Add(right);
            return row;
        }

        private static Label GarageComparisonCell(string text, TextAnchor alignment, bool header)
        {
            var value = new Label(text ?? string.Empty);
            value.style.flexShrink = 1f;
            value.style.minWidth = 0f;
            value.style.unityTextAlign = alignment;
            value.style.whiteSpace = WhiteSpace.NoWrap;
            value.style.overflow = Overflow.Hidden;
            value.style.color = header
                ? AlpineNativeUiConfig.MutedTextColor
                : AlpineNativeUiConfig.RowTextColor;
            if (header)
                value.style.unityFontStyleAndWeight = FontStyle.Bold;
            SetTooltip(value, text);
            return value;
        }

        private static void ApplyGarageDynoButtonStyle(Button button)
        {
            if (button == null)
                return;
            button.style.height = 22f;
            button.style.minWidth = 58f;
            button.style.marginLeft = AlpineNativeUiConfig.InlineGap;
            button.style.marginTop = 0f;
            button.style.marginBottom = 0f;
            button.style.paddingLeft = 6f;
            button.style.paddingRight = 6f;
            button.style.flexShrink = 0f;
            button.style.backgroundColor = AlpineNativeUiConfig.ButtonBackgroundColor;
            button.style.color = AlpineNativeUiConfig.RowTextColor;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.fontSize = 11f;
            button.style.whiteSpace = WhiteSpace.NoWrap;
            button.style.overflow = Overflow.Hidden;
        }

        private static VisualElement GarageDynoSection(
            string title,
            string badgeText,
            string disclosure,
            bool estimated)
        {
            var section = new VisualElement();
            section.style.flexDirection = FlexDirection.Column;
            section.style.alignSelf = Align.Stretch;
            section.style.width = Length.Percent(100f);
            section.style.maxWidth = Length.Percent(100f);
            section.style.minWidth = 0f;
            section.style.marginTop = 5f;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.width = Length.Percent(100f);
            header.style.minWidth = 0f;

            var heading = new Label(title ?? string.Empty);
            heading.style.flexGrow = 1f;
            heading.style.flexShrink = 1f;
            heading.style.minWidth = 0f;
            heading.style.color = AlpineNativeUiConfig.TitleTextColor;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.fontSize = 11f;
            heading.style.whiteSpace = WhiteSpace.NoWrap;
            heading.style.overflow = Overflow.Hidden;

            var badge = new Label(badgeText ?? string.Empty);
            badge.style.flexShrink = 0f;
            badge.style.marginLeft = AlpineNativeUiConfig.InlineGap;
            badge.style.paddingLeft = 4f;
            badge.style.paddingRight = 4f;
            badge.style.paddingTop = 1f;
            badge.style.paddingBottom = 1f;
            badge.style.backgroundColor = estimated
                ? new Color(0.96f, 0.47f, 0.12f, 0.96f)
                : AlpineNativeUiConfig.AccentColor;
            badge.style.color = AlpineNativeUiConfig.ActiveButtonTextColor;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.fontSize = 9f;
            badge.style.whiteSpace = WhiteSpace.NoWrap;

            SetTooltip(heading, disclosure);
            SetTooltip(badge, disclosure);
            header.Add(heading);
            header.Add(badge);
            section.Add(header);
            return section;
        }

        private static VisualElement CreateGarageDynoOverlay(
            VisualElement host,
            Action close,
            out ScrollView content)
        {
            var panel = new VisualElement { name = "AlpineGarageDynoOverlay" };
            panel.style.position = Position.Absolute;
            panel.style.top = Length.Percent(4f);
            panel.style.right = Length.Percent(3f);
            panel.style.width = Length.Percent(33f);
            panel.style.height = Length.Percent(33f);
            panel.style.minWidth = 320f;
            panel.style.minHeight = 210f;
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.overflow = Overflow.Hidden;
            panel.style.paddingLeft = 10f;
            panel.style.paddingRight = 10f;
            panel.style.paddingTop = 6f;
            // Reserve the lower corner for the resize target. It must never sit
            // underneath scrolling metrics.
            panel.style.paddingBottom = 22f;
            panel.style.backgroundColor = new Color(0.045f, 0.055f, 0.065f, 0.97f);
            panel.style.borderLeftColor = AlpineNativeUiConfig.AccentColor;
            panel.style.borderRightColor = AlpineNativeUiConfig.AccentColor;
            panel.style.borderTopColor = AlpineNativeUiConfig.AccentColor;
            panel.style.borderBottomColor = AlpineNativeUiConfig.AccentColor;
            panel.style.borderLeftWidth = 1f;
            panel.style.borderRightWidth = 1f;
            panel.style.borderTopWidth = 1f;
            panel.style.borderBottomWidth = 1f;

            var header = new VisualElement { name = "AlpineDynoDragHandle" };
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.flexShrink = 0f;
            header.style.height = 24f;
            var title = new Label("DYNO");
            title.style.flexGrow = 1f;
            title.style.flexShrink = 1f;
            title.style.minWidth = 0f;
            title.style.color = AlpineNativeUiConfig.AccentColor;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 12f;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.overflow = Overflow.Hidden;
            SetTooltip(title, "Exact Sledders drive-force model plus a separately labelled estimated engine curve.");
            var reset = new Button { text = "FIT" };
            var closeButton = new Button { text = "X" };
            ApplyGarageDynoButtonStyle(reset);
            ApplyGarageDynoButtonStyle(closeButton);
            reset.style.minWidth = 48f;
            closeButton.style.minWidth = 24f;
            header.Add(title);
            header.Add(reset);
            header.Add(closeButton);
            panel.Add(header);

            content = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "AlpineDynoContent",
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Auto
            };
            content.style.flexGrow = 1f;
            content.style.flexShrink = 1f;
            content.style.width = Length.Percent(100f);
            content.style.minWidth = 0f;
            content.style.minHeight = 0f;
            content.style.paddingRight = 3f;
            content.contentContainer.style.flexDirection = FlexDirection.Column;
            content.contentContainer.style.width = Length.Percent(100f);
            content.contentContainer.style.minWidth = 0f;
            panel.Add(content);

            var resize = new Label("\u2198") { name = "AlpineDynoResizeHandle" };
            resize.style.position = Position.Absolute;
            resize.style.right = 1f;
            resize.style.bottom = 0f;
            resize.style.width = 22f;
            resize.style.height = 22f;
            resize.style.unityTextAlign = TextAnchor.MiddleCenter;
            resize.style.color = AlpineNativeUiConfig.AccentColor;
            resize.pickingMode = PickingMode.Position;
            panel.Add(resize);

            closeButton.clicked += () => close?.Invoke();
            reset.clicked += () => ResetGarageDynoBounds(host, panel);
            RegisterGarageDynoDrag(host, panel, header);
            RegisterGarageDynoResize(host, panel, resize);
            host?.Add(panel);
            panel.BringToFront();
            host?.RegisterCallback<GeometryChangedEvent>(_ => ClampGarageDynoToViewport(host, panel));
            panel.schedule.Execute(() => ClampGarageDynoToViewport(host, panel));
            return panel;
        }

        private static void ResetGarageDynoBounds(VisualElement host, VisualElement panel)
        {
            if (panel == null)
                return;
            panel.style.left = StyleKeyword.Auto;
            panel.style.top = Length.Percent(4f);
            panel.style.right = Length.Percent(3f);
            panel.style.width = Length.Percent(33f);
            panel.style.height = Length.Percent(33f);
            panel.schedule.Execute(() => ClampGarageDynoToViewport(host, panel));
        }

        private static void ClampGarageDynoToViewport(VisualElement host, VisualElement panel)
        {
            if (host == null || panel == null || panel.panel == null)
                return;

            float hostWidth = host.resolvedStyle.width;
            float hostHeight = host.resolvedStyle.height;
            if (hostWidth <= 1f || hostHeight <= 1f)
                return;

            float safeWidth = Mathf.Max(240f, hostWidth);
            float safeHeight = Mathf.Max(180f, hostHeight);
            float width = Mathf.Clamp(panel.resolvedStyle.width, Mathf.Min(320f, safeWidth), safeWidth);
            float height = Mathf.Clamp(panel.resolvedStyle.height, Mathf.Min(210f, safeHeight), safeHeight);
            float left = panel.worldBound.xMin - host.worldBound.xMin;
            float top = panel.worldBound.yMin - host.worldBound.yMin;

            panel.style.left = Mathf.Clamp(left, 0f, Mathf.Max(0f, hostWidth - width));
            panel.style.top = Mathf.Clamp(top, 0f, Mathf.Max(0f, hostHeight - height));
            panel.style.right = StyleKeyword.Auto;
            panel.style.width = width;
            panel.style.height = height;
        }

        private static void RegisterGarageDynoDrag(
            VisualElement host,
            VisualElement panel,
            VisualElement handle)
        {
            bool dragging = false;
            int pointerId = -1;
            Vector2 pointerStart = Vector2.zero;
            Vector2 panelStart = Vector2.zero;
            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || host == null || panel == null ||
                    IsInsideButton(evt.target as VisualElement))
                    return;
                dragging = true;
                pointerId = evt.pointerId;
                pointerStart = new Vector2(evt.position.x, evt.position.y);
                panelStart = new Vector2(
                    panel.worldBound.xMin - host.worldBound.xMin,
                    panel.worldBound.yMin - host.worldBound.yMin);
                panel.style.left = panelStart.x;
                panel.style.top = panelStart.y;
                panel.style.right = StyleKeyword.Auto;
                handle.CapturePointer(pointerId);
                evt.StopImmediatePropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging || evt.pointerId != pointerId)
                    return;
                Vector2 pointer = new Vector2(evt.position.x, evt.position.y);
                Vector2 next = panelStart + pointer - pointerStart;
                float maxX = Mathf.Max(0f, host.resolvedStyle.width - panel.resolvedStyle.width);
                float maxY = Mathf.Max(0f, host.resolvedStyle.height - panel.resolvedStyle.height);
                panel.style.left = Mathf.Clamp(next.x, 0f, maxX);
                panel.style.top = Mathf.Clamp(next.y, 0f, maxY);
                evt.StopImmediatePropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging || evt.pointerId != pointerId)
                    return;
                dragging = false;
                if (handle.HasPointerCapture(pointerId))
                    handle.ReleasePointer(pointerId);
                evt.StopImmediatePropagation();
            });
        }

        private static void RegisterGarageDynoResize(
            VisualElement host,
            VisualElement panel,
            VisualElement handle)
        {
            bool resizing = false;
            int pointerId = -1;
            Vector2 pointerStart = Vector2.zero;
            Vector2 sizeStart = Vector2.zero;
            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || host == null || panel == null)
                    return;
                resizing = true;
                pointerId = evt.pointerId;
                pointerStart = new Vector2(evt.position.x, evt.position.y);
                sizeStart = new Vector2(panel.resolvedStyle.width, panel.resolvedStyle.height);
                handle.CapturePointer(pointerId);
                evt.StopImmediatePropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing || evt.pointerId != pointerId)
                    return;
                Vector2 pointer = new Vector2(evt.position.x, evt.position.y);
                Vector2 size = sizeStart + pointer - pointerStart;
                float panelLeft = panel.worldBound.xMin - host.worldBound.xMin;
                float panelTop = panel.worldBound.yMin - host.worldBound.yMin;
                float maxWidth = Mathf.Max(320f, host.resolvedStyle.width - panelLeft);
                float maxHeight = Mathf.Max(210f, host.resolvedStyle.height - panelTop);
                panel.style.width = Mathf.Clamp(size.x, 320f, maxWidth);
                panel.style.height = Mathf.Clamp(size.y, 210f, maxHeight);
                evt.StopImmediatePropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!resizing || evt.pointerId != pointerId)
                    return;
                resizing = false;
                if (handle.HasPointerCapture(pointerId))
                    handle.ReleasePointer(pointerId);
                panel.schedule.Execute(() => ClampGarageDynoToViewport(host, panel));
                evt.StopImmediatePropagation();
            });
        }

        private static void PopulateGarageDyno(
            AlpineTuningMod mod,
            VehicleScriptableObject target,
            TuneProfile working,
            ScrollView content)
        {
            if (content == null)
                return;
            content.Clear();

            AlpineDisplayUnits units = mod != null ? mod.Settings.units : AlpineDisplayUnits.Metric;
            GarageComparisonSnapshot snapshot = BuildGarageComparisonSnapshot(mod, target, working, null);
            if (snapshot?.Factory == null || snapshot.Current == null)
            {
                content.Add(MutedLabel("Comparison unavailable for this sled."));
                return;
            }

            const string nativeDisclosure =
                "Exact Sledders constant-power track model at full drive input. These plots are delivered track output, not a crankshaft torque curve.";
            var gameModel = GarageDynoSection(
                "GAME MODEL",
                "EXACT MODEL",
                nativeDisclosure,
                false);

            NativePhysicsDefaults nativeDrive = snapshot.Defaults?.nativePhysics;
            float efficiency = nativeDrive?.powerEfficiency ?? 0f;
            float minimumSpeed = nativeDrive?.drivetrainMinSpeed ?? 0f;
            float taperStart = nativeDrive?.drivetrainMaxSpeed1 ?? 0f;
            float taperEnd = nativeDrive?.drivetrainMaxSpeed2 ?? 0f;
            bool hasNativeDrive = nativeDrive != null &&
                nativeDrive.hasPowerEfficiency && nativeDrive.hasDrivetrainMinSpeed &&
                nativeDrive.hasDrivetrainMaxSpeed1 && nativeDrive.hasDrivetrainMaxSpeed2 &&
                IsFinitePositive(efficiency) && IsFinitePositive(minimumSpeed) &&
                IsFinitePositive(taperStart) && IsFinitePositive(taperEnd) &&
                taperEnd > taperStart;

            if (hasNativeDrive)
            {
                float currentEfficiency = efficiency * PositiveEffectMultiplier(
                    snapshot.CurrentEffect, "nativePowerEfficiencyMultiplier");
                float speedMultiplier = PositiveEffectMultiplier(
                    snapshot.CurrentEffect, "nativeDrivetrainSpeedMultiplier");
                float currentMinimum = minimumSpeed * speedMultiplier;
                float currentTaperStart = taperStart * speedMultiplier;
                float currentTaperEnd = taperEnd * speedMultiplier;
                float graphEnd = Mathf.Max(taperEnd, currentTaperEnd);

                var factoryPower = new GaragePlotSeries
                {
                    Name = "Factory",
                    Color = new Color(0.64f, 0.69f, 0.74f, 0.90f)
                };
                var currentPower = new GaragePlotSeries
                {
                    Name = "Current",
                    Color = AlpineNativeUiConfig.AccentColor
                };
                var factoryForce = new GaragePlotSeries
                {
                    Name = "Factory",
                    Color = new Color(0.64f, 0.69f, 0.74f, 0.90f)
                };
                var currentForce = new GaragePlotSeries
                {
                    Name = "Current",
                    Color = AlpineNativeUiConfig.AccentColor
                };

                for (int i = 0; i <= 48; i++)
                {
                    float speed = graphEnd * i / 48f;
                    float factoryWatts = AlpineTuneMath.NativeDeliveredTrackPower(
                        snapshot.Factory.horsePower, efficiency, 1f, speed, taperStart, taperEnd);
                    float currentWatts = AlpineTuneMath.NativeDeliveredTrackPower(
                        snapshot.Current.horsePower, currentEfficiency, 1f, speed, currentTaperStart, currentTaperEnd);
                    float displaySpeed = units == AlpineDisplayUnits.Imperial
                        ? speed * 2.2369363f
                        : speed * 3.6f;
                    float factoryDisplayPower = units == AlpineDisplayUnits.Imperial
                        ? factoryWatts / 745.6999f
                        : factoryWatts / 1000f;
                    float currentDisplayPower = units == AlpineDisplayUnits.Imperial
                        ? currentWatts / 745.6999f
                        : currentWatts / 1000f;
                    float factoryNewtons = AlpineTuneMath.NativeTrackForce(factoryWatts, speed, minimumSpeed);
                    float currentNewtons = AlpineTuneMath.NativeTrackForce(currentWatts, speed, currentMinimum);
                    float factoryDisplayForce = units == AlpineDisplayUnits.Imperial
                        ? factoryNewtons * 0.22480894f
                        : factoryNewtons;
                    float currentDisplayForce = units == AlpineDisplayUnits.Imperial
                        ? currentNewtons * 0.22480894f
                        : currentNewtons;
                    factoryPower.Points.Add(new Vector2(displaySpeed, factoryDisplayPower));
                    currentPower.Points.Add(new Vector2(displaySpeed, currentDisplayPower));
                    factoryForce.Points.Add(new Vector2(displaySpeed, factoryDisplayForce));
                    currentForce.Points.Add(new Vector2(displaySpeed, currentDisplayForce));
                }

                string speedUnit = units == AlpineDisplayUnits.Imperial ? "MPH" : "KM/H";
                gameModel.Add(GarageLineGraph(
                    "DELIVERED TRACK POWER",
                    speedUnit,
                    units == AlpineDisplayUnits.Imperial ? "HP" : "KW",
                    factoryPower,
                    currentPower));
                gameModel.Add(GarageLineGraph(
                    "DRIVE FORCE",
                    speedUnit,
                    units == AlpineDisplayUnits.Imperial ? "LBF" : "N",
                    factoryForce,
                    currentForce));

            }
            else
            {
                gameModel.Add(MutedLabel(
                    "Native drive defaults have not been captured for this sled. No substitute curve is shown."));
            }
            content.Add(gameModel);

            const string estimatedDisclosure =
                "Sledders exposes no crank-torque curve. The family-shaped power curve and torque derived from it are estimates, never native telemetry.";
            var estimated = GarageDynoSection(
                "ESTIMATED ENGINE",
                "ESTIMATED · NO NATIVE TORQUE CURVE",
                estimatedDisclosure,
                true);

            GaragePlotSeries factoryHp;
            GaragePlotSeries factoryTorque;
            GaragePlotSeries currentHp;
            GaragePlotSeries currentTorque;
            string factoryReason;
            string currentReason;
            bool hasFactoryEstimate = TryBuildEstimatedEngineSeries(
                mod, target, snapshot.FactoryProfile, snapshot.FactoryEffect, units,
                "Factory", new Color(0.64f, 0.69f, 0.74f, 0.90f),
                out factoryHp, out factoryTorque, out factoryReason);
            bool hasCurrentEstimate = TryBuildEstimatedEngineSeries(
                mod, target, snapshot.CurrentProfile, snapshot.CurrentEffect, units,
                "Current", AlpineNativeUiConfig.AccentColor,
                out currentHp, out currentTorque, out currentReason);
            if (hasFactoryEstimate || hasCurrentEstimate)
            {
                estimated.Add(GarageLineGraph(
                    "POWER / RPM",
                    "RPM",
                    units == AlpineDisplayUnits.Imperial ? "HP" : "KW",
                    hasFactoryEstimate ? factoryHp : null,
                    hasCurrentEstimate ? currentHp : null));
                estimated.Add(GarageLineGraph(
                    "DERIVED TORQUE / RPM",
                    "RPM",
                    units == AlpineDisplayUnits.Imperial ? "LB-FT" : "NM",
                    hasFactoryEstimate ? factoryTorque : null,
                    hasCurrentEstimate ? currentTorque : null));
                if (!hasFactoryEstimate)
                    estimated.Add(MutedLabel(factoryReason ?? "Factory estimate unavailable."));
                if (!hasCurrentEstimate)
                    estimated.Add(MutedLabel(currentReason ?? "Current estimate unavailable."));
            }
            else
            {
                string reason = !hasCurrentEstimate ? currentReason : factoryReason;
                estimated.Add(MutedLabel(string.IsNullOrWhiteSpace(reason)
                    ? "Estimated curve unavailable."
                    : reason));
            }
            content.Add(estimated);

            var summary = Section("FACTORY / CURRENT");
            AddGarageComparisonMetrics(
                summary,
                GarageMetricsForSection(snapshot, "dyno", units),
                false);
            content.Add(summary);
        }

        private static VisualElement GarageLineGraph(
            string title,
            string xUnit,
            string yUnit,
            params GaragePlotSeries[] series)
        {
            var group = new VisualElement();
            group.style.flexDirection = FlexDirection.Column;
            group.style.flexShrink = 0f;
            group.style.width = Length.Percent(100f);
            group.style.minWidth = 0f;
            group.style.marginTop = 5f;

            Func<Vector2, bool> isFinite = point =>
                !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
                !float.IsNaN(point.y) && !float.IsInfinity(point.y);
            GaragePlotSeries[] available = (series ?? new GaragePlotSeries[0])
                .Where(item => item != null && item.Points.Any(isFinite))
                .ToArray();
            List<Vector2> plottedPoints = available
                .SelectMany(item => item.Points.Where(isFinite))
                .ToList();
            if (plottedPoints.Count == 0)
            {
                group.Add(MutedLabel((title ?? "Graph") + " unavailable."));
                return group;
            }

            float minX = plottedPoints.Min(point => point.x);
            float maxX = plottedPoints.Max(point => point.x);
            float rawMinY = Mathf.Min(0f, plottedPoints.Min(point => point.y));
            float rawMaxY = Mathf.Max(0f, plottedPoints.Max(point => point.y));
            float minY = rawMinY < 0f ? -NiceGarageGraphMaximum(Mathf.Abs(rawMinY)) : 0f;
            float maxY = NiceGarageGraphMaximum(rawMaxY);

            Label heading = MutedLabel(title);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.whiteSpace = WhiteSpace.NoWrap;
            heading.style.overflow = Overflow.Hidden;
            SetTooltip(
                heading,
                title + " plotted from " + FormatGarageGraphValue(minX) + " to " +
                FormatGarageGraphValue(maxX) + " " + xUnit + ", in " + yUnit + ".");
            group.Add(heading);

            var legend = new VisualElement();
            legend.style.flexDirection = FlexDirection.Row;
            legend.style.flexWrap = Wrap.NoWrap;
            legend.style.width = Length.Percent(100f);
            legend.style.minWidth = 0f;
            legend.style.overflow = Overflow.Hidden;
            foreach (GaragePlotSeries item in available)
            {
                float peak = item.Points.Where(isFinite).Max(point => point.y);
                string legendText =
                    (item.Name ?? string.Empty) + "  " +
                    FormatGarageGraphValue(peak) + " " + yUnit;
                Label entry = new Label(legendText);
                entry.style.color = item.Color;
                entry.style.marginRight = 10f;
                entry.style.fontSize = 9f;
                entry.style.unityFontStyleAndWeight = FontStyle.Bold;
                entry.style.whiteSpace = WhiteSpace.NoWrap;
                entry.style.flexShrink = 1f;
                entry.style.minWidth = 0f;
                entry.style.overflow = Overflow.Hidden;
                SetTooltip(entry, legendText + " peak");
                legend.Add(entry);
            }
            group.Add(legend);

            var graph = new VisualElement();
            graph.style.position = Position.Relative;
            graph.style.height = 154f;
            graph.style.flexShrink = 0f;
            graph.style.width = Length.Percent(100f);
            graph.style.minWidth = 0f;
            graph.style.overflow = Overflow.Hidden;
            graph.style.backgroundColor = new Color(0.025f, 0.032f, 0.04f, 0.96f);
            graph.generateVisualContent += context =>
            {
                if (maxX <= minX + 0.0001f || maxY <= minY + 0.0001f)
                    return;

                Rect bounds = graph.contentRect;
                const float left = 42f;
                const float right = 7f;
                const float top = 8f;
                const float bottom = 24f;
                float width = Mathf.Max(1f, bounds.width - left - right);
                float height = Mathf.Max(1f, bounds.height - top - bottom);
                var painter = context.painter2D;
                painter.lineWidth = 1f;
                painter.strokeColor = new Color(0.24f, 0.29f, 0.34f, 0.70f);
                for (int i = 0; i <= 4; i++)
                {
                    float x = left + width * i / 4f;
                    float y = top + height * i / 4f;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x, top));
                    painter.LineTo(new Vector2(x, top + height));
                    painter.Stroke();
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(left, y));
                    painter.LineTo(new Vector2(left + width, y));
                    painter.Stroke();
                }

                foreach (GaragePlotSeries item in available)
                {
                    painter.lineWidth = string.Equals(item.Name, "Current", StringComparison.OrdinalIgnoreCase)
                        ? 2.75f
                        : 2f;
                    painter.strokeColor = item.Color;
                    painter.BeginPath();
                    bool started = false;
                    foreach (Vector2 value in item.Points)
                    {
                        if (!isFinite(value))
                            continue;
                        float x = left + Mathf.InverseLerp(minX, maxX, value.x) * width;
                        float y = top + (1f - Mathf.InverseLerp(minY, maxY, value.y)) * height;
                        if (!started)
                        {
                            painter.MoveTo(new Vector2(x, y));
                            started = true;
                        }
                        else
                            painter.LineTo(new Vector2(x, y));
                    }
                    if (started)
                        painter.Stroke();
                }
            };

            const float plotTop = 8f;
            const float plotHeight = 122f;
            for (int tick = 0; tick <= 4; tick++)
            {
                float fraction = tick / 4f;
                float value = Mathf.Lerp(minY, maxY, fraction);
                string tickText = FormatGarageGraphValue(value) +
                                  (tick == 4 ? " " + yUnit : string.Empty);
                Label yTick = GarageGraphScaleLabel(tickText, TextAnchor.MiddleRight);
                yTick.style.left = 0f;
                yTick.style.top = plotTop + plotHeight * (1f - fraction) - 6f;
                yTick.style.width = 39f;
                yTick.style.height = 13f;
                graph.Add(yTick);
            }

            for (int tick = 0; tick <= 4; tick++)
            {
                int tickIndex = tick;
                float fraction = tickIndex / 4f;
                float value = Mathf.Lerp(minX, maxX, fraction);
                string tickText = FormatGarageGraphValue(value) +
                                  (tickIndex == 4 ? " " + xUnit : string.Empty);
                TextAnchor alignment = tickIndex == 0
                    ? TextAnchor.LowerLeft
                    : tickIndex == 4 ? TextAnchor.LowerRight : TextAnchor.LowerCenter;
                Label xTick = GarageGraphScaleLabel(tickText, alignment);
                xTick.style.bottom = 0f;
                xTick.style.width = 64f;
                xTick.style.height = 18f;
                Action placeTick = () =>
                {
                    float plotWidth = Mathf.Max(1f, graph.resolvedStyle.width - 49f);
                    float center = 42f + plotWidth * fraction;
                    float offset = tickIndex == 0 ? 0f : tickIndex == 4 ? 64f : 32f;
                    xTick.style.left = center - offset;
                };
                graph.RegisterCallback<GeometryChangedEvent>(_ => placeTick());
                graph.Add(xTick);
            }
            group.Add(graph);
            return group;
        }

        private static Label GarageGraphScaleLabel(string text, TextAnchor alignment)
        {
            var label = new Label(text ?? string.Empty);
            label.style.position = Position.Absolute;
            label.style.paddingLeft = 2f;
            label.style.paddingRight = 2f;
            label.style.backgroundColor = new Color(0.025f, 0.032f, 0.04f, 0.84f);
            label.style.color = AlpineNativeUiConfig.MutedTextColor;
            label.style.fontSize = 9f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = alignment;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        private static float NiceGarageGraphMaximum(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                return 1f;

            // Choose a conventional 1/2/5 tick step for roughly four vertical
            // divisions, then round only to the next step. Rounding the entire
            // maximum to 1/2/5 can turn 277 into 500 and visually flatten useful
            // differences between factory and current curves.
            float roughStep = value / 4f;
            float magnitude = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(roughStep)));
            float normalized = roughStep / magnitude;
            float step = normalized <= 1f
                ? 1f
                : normalized <= 2f
                    ? 2f
                    : normalized <= 5f
                        ? 5f
                        : 10f;
            step *= magnitude;
            return Mathf.Max(step, Mathf.Ceil(value / step) * step);
        }

        private static string FormatGarageGraphValue(float value)
        {
            float absolute = Mathf.Abs(value);
            if (absolute >= 100f)
                return value.ToString("F0");
            if (absolute >= 10f)
                return value.ToString("F1");
            return value.ToString("F2");
        }

        private static bool TryBuildEstimatedEngineSeries(
            AlpineTuningMod mod,
            VehicleScriptableObject target,
            TuneProfile profile,
            PartEffect effect,
            AlpineDisplayUnits units,
            string name,
            Color color,
            out GaragePlotSeries power,
            out GaragePlotSeries torque,
            out string unavailableReason)
        {
            power = null;
            torque = null;
            unavailableReason = null;
            ResolvedStats stats = profile?.resolvedStats;
            if (stats == null || stats.horsePower <= 0f)
            {
                unavailableReason = "Estimated curve unavailable: configured output is missing.";
                return false;
            }

            SledDefaults engineDefaults = EngineDefaultsForProfile(mod, target, profile);
            string engineName = !string.IsNullOrWhiteSpace(engineDefaults?.engineText)
                ? engineDefaults.engineText
                : stats.engineText;
            bool turbo = stats.isTurboOn;
            Vector2[] anchors;
            AlpineTuneMath.EstimatedEngineArchetype archetype;
            if (!AlpineTuneMath.TryGetEstimatedEngineCurve(engineName, turbo, out archetype, out anchors) ||
                archetype == AlpineTuneMath.EstimatedEngineArchetype.Unknown)
            {
                unavailableReason = "Estimated curve unavailable: engine family is unknown.";
                return false;
            }

            float redline = AlpineTuneMath.ResolveEstimatedRedline(stats);
            ControllerDefaults recipientController = StockDefaultsFor(mod, target)?.controller;
            float clutchStart = AlpineTuneMath.ResolveEstimatedCurveStartRpm(
                redline, recipientController, effect, profile?.fineTune);

            power = new GaragePlotSeries { Name = name, Color = color };
            torque = new GaragePlotSeries { Name = name, Color = color };
            float peakFraction = anchors
                .OrderByDescending(anchor => anchor.y)
                .ThenBy(anchor => anchor.x)
                .First().x;
            // Preserve the captured clutch start whenever it is below the
            // archetype peak. If an unusual clutch calibration engages after
            // that peak, include the peak itself so the chart still conveys the
            // configured output rather than silently scaling it down.
            float startFraction = Mathf.Min(clutchStart / redline, peakFraction);
            List<float> sampleFractions = Enumerable.Range(0, 49)
                .Select(i => Mathf.Lerp(startFraction, 1f, i / 48f))
                .Concat(anchors.Select(anchor => anchor.x))
                .Where(fraction => fraction >= startFraction && fraction <= 1f)
                .GroupBy(fraction => Mathf.RoundToInt(fraction * 100000f))
                .Select(group => group.First())
                .OrderBy(fraction => fraction)
                .ToList();
            foreach (float normalizedRpm in sampleFractions)
            {
                float rpm = normalizedRpm * redline;
                float horsepower = stats.horsePower *
                                   AlpineTuneMath.InterpolateEstimatedEngineCurve(anchors, normalizedRpm);
                float displayPower = units == AlpineDisplayUnits.Imperial
                    ? horsepower
                    : UnitConversion.HorsepowerToKilowatts(horsepower);
                float displayTorque = units == AlpineDisplayUnits.Imperial
                    ? horsepower * 5252.113f / Mathf.Max(1f, rpm)
                    : UnitConversion.HorsepowerToKilowatts(horsepower) * 9549.2966f / Mathf.Max(1f, rpm);
                power.Points.Add(new Vector2(rpm, displayPower));
                torque.Points.Add(new Vector2(rpm, displayTorque));
            }
            return true;
        }

        private static GarageComparisonSnapshot BuildGarageComparisonSnapshot(
            AlpineTuningMod mod,
            VehicleScriptableObject target,
            TuneProfile current,
            TuneProfile candidate)
        {
            if (mod == null || target == null || current == null)
                return null;

            TuneProfile factory = mod.Catalog.CreateDefaultProfile(
                target,
                AlpineConstants.DefaultProfileAuthor);
            TuneProfile currentPreview = TuneStore.Clone(current);
            TuneProfile candidatePreview = TuneStore.Clone(candidate);
            try
            {
                mod.PreviewProfilesWithSharedEnvironment(
                    target,
                    factory,
                    currentPreview,
                    candidatePreview);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Garage comparison could not be resolved: {ex.GetType().Name}");
            }

            return new GarageComparisonSnapshot
            {
                Defaults = StockDefaultsFor(mod, target),
                FactoryProfile = factory,
                CurrentProfile = currentPreview,
                CandidateProfile = candidatePreview,
                FactoryEffect = GarageMergedEffect(mod, factory),
                CurrentEffect = GarageMergedEffect(mod, currentPreview),
                CandidateEffect = candidatePreview != null ? GarageMergedEffect(mod, candidatePreview) : null
            };
        }

        private static PartEffect GarageMergedEffect(AlpineTuningMod mod, TuneProfile profile)
        {
            var merged = new PartEffect();
            if (mod?.Catalog == null || profile == null)
                return merged;

            foreach (string category in PartCatalog.OrderedCategories)
            {
                TunePart part = mod.Catalog.Find(profile.GetPartId(category));
                if (part?.effect != null)
                    AlpineTuneMath.MergeEffect(merged, part.effect);
            }
            return merged;
        }

        private static SledDefaults EngineDefaultsForProfile(
            AlpineTuningMod mod,
            VehicleScriptableObject target,
            TuneProfile profile)
        {
            if (profile != null &&
                (!string.IsNullOrWhiteSpace(profile.donorSledKey) ||
                 !string.IsNullOrWhiteSpace(profile.donorVehicleId)))
            {
                VehicleScriptableObject donor = mod?.FindSledByIdentity(
                    profile.donorSledKey,
                    profile.donorVehicleId);
                SledDefaults donorDefaults = StockDefaultsFor(mod, donor);
                if (donorDefaults != null)
                    return donorDefaults;
                return null;
            }
            return StockDefaultsFor(mod, target);
        }

        private static List<GarageMetricDescriptor> GarageMetricsForSection(
            GarageComparisonSnapshot snapshot,
            string section,
            AlpineDisplayUnits units)
        {
            var metrics = new List<GarageMetricDescriptor>();
            if (snapshot?.Factory == null || snapshot.Current == null)
                return metrics;

            ResolvedStats factory = snapshot.Factory;
            ResolvedStats current = snapshot.Current;
            ResolvedStats candidate = snapshot.Candidate;
            bool hasCandidate = candidate != null;
            string normalizedSection = (section ?? string.Empty).ToLowerInvariant();
            NativePhysicsDefaults native = snapshot.Defaults?.nativePhysics;
            ControllerDefaults controller = snapshot.Defaults?.controller;

            Action<string, string, float, float, float, bool, GarageMetricDirection, Func<float, string>, float?, float?> add =
                (label, tooltip, factoryValue, currentValue, candidateValue, available, direction, formatter, minimum, maximum) =>
                {
                    metrics.Add(new GarageMetricDescriptor
                    {
                        Label = label,
                        Tooltip = tooltip,
                        Factory = factoryValue,
                        Current = currentValue,
                        Candidate = candidateValue,
                        HasCandidate = hasCandidate,
                        Available = available,
                        Direction = direction,
                        Format = formatter,
                        SafetyMinimum = minimum,
                        SafetyMaximum = maximum
                    });
                };

            Func<float, string> percent = value => value.ToString("F0") + "%";
            Func<float, string> rpm = value => value.ToString("F0") + " rpm";
            Func<float, string> power = value => UnitConversion.FormatPower(value, units);
            Func<float, string> weight = value => UnitConversion.FormatWeight(value, units);
            Func<float, string> lug = value => units == AlpineDisplayUnits.Imperial
                ? UnitConversion.MillimetersToInches(value).ToString("F2") + " in"
                : value.ToString("F0") + " mm";
            Func<float, string> stance = value => units == AlpineDisplayUnits.Imperial
                ? UnitConversion.MillimetersToInches(value).ToString("F1") + " in"
                : value.ToString("F0") + " mm";
            Func<float, string> metres = value => UnitConversion.FormatLengthFromMeters(value, units);

            if (normalizedSection == "engine" || normalizedSection == "dyno")
            {
                add("Configured output", "Resolved configured engine output.",
                    factory.horsePower, current.horsePower, candidate?.horsePower ?? 0f,
                    true, GarageMetricDirection.HigherIsBetter, power, 0f, null);
                add("Setup weight", "Complete configured sled weight.",
                    factory.weight, current.weight, candidate?.weight ?? 0f,
                    true, GarageMetricDirection.LowerIsBetter, weight, 0f, null);
            }

            if (normalizedSection == "drivetrain" || normalizedSection == "dyno")
            {
                AddAvailableEffectPercentMetric(metrics, "Drive efficiency", "Native powerEfficiency multiplier.",
                    snapshot, native?.hasPowerEfficiency == true,
                    GarageMetricDirection.HigherIsBetter, "nativePowerEfficiencyMultiplier");
                AddClutchMetrics(metrics, snapshot, rpm);
                AddRpmResponseMetrics(metrics, snapshot);
                AddAvailableEffectPercentMetric(metrics, "Speed taper", "Native drivetrain speed-envelope multiplier.",
                    snapshot, native?.hasDrivetrainMaxSpeed1 == true && native.hasDrivetrainMaxSpeed2,
                    GarageMetricDirection.Preference, "nativeDrivetrainSpeedMultiplier");
                AddAvailableEffectPercentMetric(metrics, "Track inertia", "Native simulated track-mass multiplier.",
                    snapshot, native?.hasTrackMass == true,
                    GarageMetricDirection.LowerIsBetter, "nativeTrackMassMultiplier");
                AddAvailableEffectPercentMetric(metrics, "Brake calibration", "Native breakForce multiplier relative to factory.",
                    snapshot, native?.hasBrakeForce == true,
                    GarageMetricDirection.Preference, "nativeBrakeForceMultiplier", "brakeForceMultiplier");
            }

            if (normalizedSection == "track" || normalizedSection == "dyno")
            {
                add("Lug height", "Resolved physical lug height.",
                    factory.lugHeight, current.lugHeight, candidate?.lugHeight ?? 0f,
                    true, GarageMetricDirection.Preference, lug, 0f, 100f);
                add("Snow bite", "Resolved snow-friction coefficient.",
                    factory.friction, current.friction, candidate?.friction ?? 0f,
                    true, GarageMetricDirection.HigherIsBetter,
                    value => value.ToString("F2"), 0f, 3f);
                AddAvailableEffectPercentMetric(metrics, "Hard-surface grip", "Per-track hard-surface contact grip.",
                    snapshot, native?.hasTrackGrip == true,
                    GarageMetricDirection.HigherIsBetter, "nativeTrackGripMultiplier", "trackGripMultiplier");
                if (normalizedSection != "dyno")
                {
                    AddAvailableEffectPercentMetric(metrics, "Track inertia", "Native simulated track-mass multiplier.",
                        snapshot, native?.hasTrackMass == true,
                        GarageMetricDirection.LowerIsBetter, "nativeTrackMassMultiplier");
                }
            }

            if (normalizedSection == "steering")
            {
                add("Ski stance", "Native skiStance is stored and displayed in millimetres.",
                    factory.skiStance, current.skiStance, candidate?.skiStance ?? 0f,
                    true, GarageMetricDirection.Preference, stance,
                    Mathf.Max(0f, factory.skiStance - 180f),
                    Mathf.Min(4000f, factory.skiStance + 180f));
                add("Physical ski offset", "Native skisXDistanceOffset remains metres internally.",
                    factory.skisXDistanceOffset, current.skisXDistanceOffset,
                    candidate?.skisXDistanceOffset ?? 0f,
                    true, GarageMetricDirection.Preference, metres,
                    Mathf.Max(-1f, factory.skisXDistanceOffset - 0.12f),
                    Mathf.Min(1f, factory.skisXDistanceOffset + 0.12f));
                AddAvailableEffectPercentMetric(metrics, "Ski grip", "Per-ski hard-surface contact grip.",
                    snapshot, native?.hasSkiGrip == true,
                    GarageMetricDirection.HigherIsBetter, "nativeSkiGripMultiplier", "skiGripMultiplier");
                AddAvailableEffectPercentMetric(metrics, "Steering angle", "Native maximum ski-angle multiplier.",
                    snapshot, native?.hasSkisMaxAngle == true,
                    GarageMetricDirection.Preference, "nativeSkisMaxAngleMultiplier", "skisMaxAngleMultiplier");
                if (native?.hasToeAngle == true && Mathf.Abs(native.toeAngle) < 0.0001f)
                {
                    metrics.Add(new GarageMetricDescriptor
                    {
                        Label = "Toe",
                        Tooltip = "Factory toe is zero, so multiplier presets correctly leave the resolved toe at zero.",
                        Factory = 0f,
                        Current = 0f,
                        Candidate = 0f,
                        HasCandidate = snapshot.Candidate != null,
                        Direction = GarageMetricDirection.Preference,
                        Format = value => "0 (factory zero)",
                        SafetyMinimum = -1f,
                        SafetyMaximum = 1f
                    });
                }
                else if (native?.hasToeAngle == true)
                {
                    AddAvailableEffectPercentMetric(metrics, "Toe", "Native toe-angle multiplier.",
                        snapshot, true, GarageMetricDirection.Preference,
                        "nativeToeAngleMultiplier", "toeAngleMultiplier");
                }
                AddAvailableEffectPercentMetric(metrics, "Camber response", "Native ski camber-response multiplier.",
                    snapshot, native?.hasLeftCamberFactor == true || native?.hasRightCamberFactor == true,
                    GarageMetricDirection.Preference, "nativeCamberFactorMultiplier", "camberFactorMultiplier");
            }

            if (normalizedSection == "suspension")
            {
                AddAvailableEffectPercentMetric(metrics, "Front spring", "Native front spring factor.",
                    snapshot, native?.hasFrontSpring == true, GarageMetricDirection.Preference, "nativeFrontSpringMultiplier");
                AddAvailableEffectPercentMetric(metrics, "Front damper", "Native front damper factor.",
                    snapshot, native?.hasFrontDamper == true, GarageMetricDirection.Preference, "nativeFrontDamperMultiplier");
                AddAvailableEffectPercentMetric(metrics, "Front compression", "Native front compression damping.",
                    snapshot, native?.hasFrontCompressionDamping == true, GarageMetricDirection.Preference, "nativeFrontCompressionDampingMultiplier");
                AddAvailableEffectPercentMetric(metrics, "Front rebound", "Native front rebound damping.",
                    snapshot, native?.hasFrontReboundDamping == true, GarageMetricDirection.Preference, "nativeFrontReboundDampingMultiplier");
                AddAvailableEffectPercentMetric(metrics, "Rear spring", "Native rear spring factor.",
                    snapshot, native?.hasRearSpring == true, GarageMetricDirection.Preference, "nativeRearSpringMultiplier");
                AddAvailableEffectPercentMetric(metrics, "Rear damper", "Native rear damper factor.",
                    snapshot, native?.hasRearDamper == true, GarageMetricDirection.Preference, "nativeRearDamperMultiplier");
                AddAvailableEffectPercentMetric(metrics, "Rear compression", "Native rear compression damping.",
                    snapshot, native?.hasRearCompressionDamping == true, GarageMetricDirection.Preference, "nativeRearCompressionDampingMultiplier");
                AddAvailableEffectPercentMetric(metrics, "Rear rebound", "Native rear rebound damping.",
                    snapshot, native?.hasRearReboundDamping == true, GarageMetricDirection.Preference, "nativeRearReboundDampingMultiplier");
                AddAvailableEffectPercentMetric(metrics, "Anti-roll", "Native anti-roll-bar factor.",
                    snapshot, native?.hasAntiRollBar == true, GarageMetricDirection.Preference, "nativeAntiRollBarMultiplier");
                AddAvailableEffectPercentMetric(metrics, "Front rigidity", "Native front track rigidity.",
                    snapshot, native?.hasTrackRigidityFront == true, GarageMetricDirection.Preference, "nativeTrackRigidityFrontMultiplier");
                AddAvailableEffectPercentMetric(metrics, "Rear rigidity", "Native rear track rigidity.",
                    snapshot, native?.hasTrackRigidityRear == true, GarageMetricDirection.Preference, "nativeTrackRigidityRearMultiplier");
                add("COM height", "Resolved centre-of-mass vertical offset.",
                    factory.centerOfMassOffset?.y ?? 0f,
                    current.centerOfMassOffset?.y ?? 0f,
                    candidate?.centerOfMassOffset?.y ?? 0f,
                    true, GarageMetricDirection.Preference, metres, null, null);
                add("COM fore/aft", "Resolved centre-of-mass longitudinal offset.",
                    factory.centerOfMassOffset?.z ?? 0f,
                    current.centerOfMassOffset?.z ?? 0f,
                    candidate?.centerOfMassOffset?.z ?? 0f,
                    true, GarageMetricDirection.Preference, metres, null, null);
            }

            if (normalizedSection == "fuel" || normalizedSection == "dyno")
            {
                Func<float, string> liters = value => value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " L";
                Func<float, string> lPer100 = value => value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " L/100 km";
                add("Tank capacity", "Physical main-tank capacity. Engine swaps do not change the recipient chassis tank.",
                    factory.fuelCapacity, current.fuelCapacity, candidate?.fuelCapacity ?? 0f,
                    true, GarageMetricDirection.Preference, liters, 0f, 100f);
                add("Nominal consumption", "Native engine fuel-consumption calibration. Engine swaps inherit the donor engine value.",
                    factory.fuelConsumption, current.fuelConsumption, candidate?.fuelConsumption ?? 0f,
                    true, GarageMetricDirection.LowerIsBetter, lPer100, 0f, null);
                add("Backpack reserve", "Additional transferable fuel carried by the rider.",
                    factory.backpackFuelCapacityLiters, current.backpackFuelCapacityLiters, candidate?.backpackFuelCapacityLiters ?? 0f,
                    true, GarageMetricDirection.Preference, liters, 0f, 22f);
            }

            if (normalizedSection == "lighting")
            {
                AddEffectPercentMetric(metrics, "Intensity", "Headlight intensity relative to factory.",
                    snapshot, "headlightIntensityMultiplier", GarageMetricDirection.Preference);
                AddEffectPercentMetric(metrics, "Range", "Headlight range relative to factory.",
                    snapshot, "headlightRangeMultiplier", GarageMetricDirection.Preference);
                AddEffectPercentMetric(metrics, "Beam angle", "Headlight spot angle relative to factory.",
                    snapshot, "headlightSpotAngleMultiplier", GarageMetricDirection.Preference);
                add("Vertical aim", "Headlight pitch offset.",
                    snapshot.FactoryEffect?.headlightPitchOffsetDegrees ?? 0f,
                    snapshot.CurrentEffect?.headlightPitchOffsetDegrees ?? 0f,
                    snapshot.CandidateEffect?.headlightPitchOffsetDegrees ?? 0f,
                    true, GarageMetricDirection.Preference,
                    value => value.ToString("+0.0;-0.0;0.0") + " deg", -15f, 15f);
            }

            return metrics;
        }

        private static void AddEffectPercentMetric(
            List<GarageMetricDescriptor> metrics,
            string label,
            string tooltip,
            GarageComparisonSnapshot snapshot,
            string member,
            GarageMetricDirection direction)
        {
            AddEffectPercentMetric(metrics, label, tooltip, snapshot, direction, member);
        }

        private static void AddAvailableEffectPercentMetric(
            List<GarageMetricDescriptor> metrics,
            string label,
            string tooltip,
            GarageComparisonSnapshot snapshot,
            bool available,
            GarageMetricDirection direction,
            params string[] members)
        {
            if (!available)
                return;
            AddEffectPercentMetric(metrics, label, tooltip, snapshot, direction, members);
        }

        private static void AddEffectPercentMetric(
            List<GarageMetricDescriptor> metrics,
            string label,
            string tooltip,
            GarageComparisonSnapshot snapshot,
            GarageMetricDirection direction,
            params string[] members)
        {
            if (metrics == null || snapshot == null || members == null || members.Length == 0)
                return;
            metrics.Add(new GarageMetricDescriptor
            {
                Label = label,
                Tooltip = tooltip,
                Factory = 100f,
                Current = PositiveEffectMultiplier(snapshot.CurrentEffect, members) * 100f,
                Candidate = PositiveEffectMultiplier(snapshot.CandidateEffect, members) * 100f,
                HasCandidate = snapshot.Candidate != null,
                Available = true,
                SafetyMinimum = 60f,
                SafetyMaximum = 140f,
                Direction = direction,
                Format = value => value.ToString("F0") + "%"
            });
        }

        private static void AddClutchMetrics(
            List<GarageMetricDescriptor> metrics,
            GarageComparisonSnapshot snapshot,
            Func<float, string> formatter)
        {
            ControllerDefaults defaults = snapshot?.Defaults?.controller;
            if (metrics == null || defaults == null)
                return;

            AlpineTuneMath.ResolvedClutchRange factory = AlpineTuneMath.ResolveClutchRange(
                defaults, snapshot.FactoryEffect, snapshot.FactoryProfile?.fineTune);
            AlpineTuneMath.ResolvedClutchRange current = AlpineTuneMath.ResolveClutchRange(
                defaults, snapshot.CurrentEffect, snapshot.CurrentProfile?.fineTune);
            AlpineTuneMath.ResolvedClutchRange candidate = AlpineTuneMath.ResolveClutchRange(
                defaults, snapshot.CandidateEffect, snapshot.CandidateProfile?.fineTune);

            if (factory.HasMinimum)
            {
                metrics.Add(new GarageMetricDescriptor
                {
                    Label = "Clutch engagement",
                    Tooltip = "Resolved native clutchRpmMin after the same offsets, trim, and safety clamp used at runtime.",
                    Factory = factory.Minimum,
                    Current = current.Minimum,
                    Candidate = candidate.Minimum,
                    HasCandidate = snapshot.Candidate != null,
                    Direction = GarageMetricDirection.Preference,
                    Format = formatter,
                    SafetyMinimum = Mathf.Max(0f, defaults.clutchRpmMin * 0.75f),
                    SafetyMaximum = Mathf.Min(14000f, defaults.clutchRpmMin * 1.35f)
                });
            }
            if (factory.HasMaximum)
            {
                metrics.Add(new GarageMetricDescriptor
                {
                    Label = "Clutch lock",
                    Tooltip = "Resolved native clutchRpmMax after the same offsets, trim, ordering, and safety clamp used at runtime.",
                    Factory = factory.Maximum,
                    Current = current.Maximum,
                    Candidate = candidate.Maximum,
                    HasCandidate = snapshot.Candidate != null,
                    Direction = GarageMetricDirection.Preference,
                    Format = formatter,
                    SafetyMinimum = Mathf.Max(0f, defaults.clutchRpmMax * 0.75f),
                    SafetyMaximum = Mathf.Min(14000f, defaults.clutchRpmMax * 1.35f)
                });
            }
        }

        private static void AddRpmResponseMetrics(
            List<GarageMetricDescriptor> metrics,
            GarageComparisonSnapshot snapshot)
        {
            ControllerDefaults defaults = snapshot?.Defaults?.controller;
            if (metrics == null || defaults == null)
                return;

            if (defaults.hasRpmSensitivity)
            {
                Func<PartEffect, float> percent = effect =>
                    AlpineTuneMath.SafeRatio(
                        AlpineTuneMath.ResolveRpmSensitivity(defaults.rpmSensitivity, effect),
                        defaults.rpmSensitivity) * 100f;
                metrics.Add(new GarageMetricDescriptor
                {
                    Label = "RPM rise",
                    Tooltip = "Resolved native RPM-up sensitivity after the same turbo-response composition and safety clamp used at runtime.",
                    Factory = percent(snapshot.FactoryEffect),
                    Current = percent(snapshot.CurrentEffect),
                    Candidate = percent(snapshot.CandidateEffect),
                    HasCandidate = snapshot.Candidate != null,
                    Direction = GarageMetricDirection.Preference,
                    Format = value => value.ToString("F0") + "%",
                    SafetyMinimum = 50f,
                    SafetyMaximum = 170f
                });
            }

            if (defaults.hasRpmSensitivityDown)
            {
                Func<PartEffect, float> percent = effect =>
                    AlpineTuneMath.SafeRatio(
                        AlpineTuneMath.ResolveRpmSensitivityDown(defaults.rpmSensitivityDown, effect),
                        defaults.rpmSensitivityDown) * 100f;
                metrics.Add(new GarageMetricDescriptor
                {
                    Label = "Backshift",
                    Tooltip = "Resolved native RPM-down sensitivity after the same safety clamp used at runtime.",
                    Factory = percent(snapshot.FactoryEffect),
                    Current = percent(snapshot.CurrentEffect),
                    Candidate = percent(snapshot.CandidateEffect),
                    HasCandidate = snapshot.Candidate != null,
                    Direction = GarageMetricDirection.Preference,
                    Format = value => value.ToString("F0") + "%",
                    SafetyMinimum = 50f,
                    SafetyMaximum = 170f
                });
            }
        }

        private static void AddGarageComparisonMetrics(
            VisualElement content,
            IEnumerable<GarageMetricDescriptor> descriptors,
            bool candidateContext)
        {
            if (content == null)
                return;
            foreach (GarageMetricDescriptor descriptor in descriptors ?? Enumerable.Empty<GarageMetricDescriptor>())
            {
                if (descriptor == null || !descriptor.Available)
                    continue;
                content.Add(GarageComparisonMetric(descriptor, candidateContext));
            }
        }

        private static void AddGarageCategoricalReference(
            VisualElement content,
            string label,
            string factoryValue,
            string currentValue,
            string candidateValue,
            bool candidateContext)
        {
            if (content == null)
                return;

            string factory = string.IsNullOrWhiteSpace(factoryValue) ? "UNAVAILABLE" : factoryValue;
            string current = string.IsNullOrWhiteSpace(currentValue) ? "UNAVAILABLE" : currentValue;
            string candidate = string.IsNullOrWhiteSpace(candidateValue) ? current : candidateValue;
            string displayed = candidateContext ? candidate : current;

            var group = new VisualElement();
            group.style.flexDirection = FlexDirection.Column;
            group.style.flexShrink = 0f;
            group.style.width = Length.Percent(100f);
            group.style.minWidth = 0f;
            group.style.marginBottom = 7f;

            Label title = MutedLabel(label ?? string.Empty);
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.overflow = Overflow.Hidden;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            SetTooltip(title, label);
            group.Add(title);

            Label factoryLine = MutedLabel("FACTORY  " + factory);
            factoryLine.style.fontSize = 10f;
            factoryLine.style.whiteSpace = WhiteSpace.Normal;
            SetTooltip(factoryLine, "Factory: " + factory);
            group.Add(factoryLine);

            string prefix = candidateContext ? "PROJECTED  " : "CURRENT  ";
            Label displayedLine = MutedLabel(prefix + displayed);
            displayedLine.style.fontSize = 10f;
            displayedLine.style.whiteSpace = WhiteSpace.Normal;
            SetTooltip(displayedLine, prefix.Trim() + ": " + displayed);
            group.Add(displayedLine);

            if (candidateContext)
            {
                bool same = string.Equals(current, candidate, StringComparison.OrdinalIgnoreCase);
                Label versusCurrent = MutedLabel(same
                    ? "VS CURRENT  SAME"
                    : "VS CURRENT  " + current + " → " + candidate);
                versusCurrent.style.fontSize = 9f;
                versusCurrent.style.whiteSpace = WhiteSpace.Normal;
                SetTooltip(versusCurrent, same
                    ? "No categorical change versus the current draft."
                    : "Current: " + current + "; projected: " + candidate + ".");
                group.Add(versusCurrent);
            }

            content.Add(group);
        }

        private static void AddGarageEngineReferences(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            GarageComparisonSnapshot snapshot,
            bool candidateContext)
        {
            if (snapshot == null)
                return;

            SledDefaults factory = EngineDefaultsForProfile(mod, target, snapshot.FactoryProfile);
            SledDefaults current = EngineDefaultsForProfile(mod, target, snapshot.CurrentProfile);
            SledDefaults candidate = EngineDefaultsForProfile(mod, target, snapshot.CandidateProfile);

            AddGarageCategoricalReference(
                content,
                "Engine family",
                factory != null ? EngineDisplayName(factory) : null,
                current != null ? EngineDisplayName(current) : null,
                candidate != null ? EngineDisplayName(candidate) : null,
                candidateContext);
            AddGarageCategoricalReference(
                content,
                "Induction family",
                snapshot.Factory != null
                    ? (snapshot.Factory.isTurboOn ? "TURBO" : "NATURALLY ASPIRATED")
                    : null,
                snapshot.Current != null
                    ? (snapshot.Current.isTurboOn ? "TURBO" : "NATURALLY ASPIRATED")
                    : null,
                snapshot.Candidate != null
                    ? (snapshot.Candidate.isTurboOn ? "TURBO" : "NATURALLY ASPIRATED")
                    : null,
                candidateContext);
        }

        private static void AddGarageLightingReferences(
            VisualElement content,
            GarageComparisonSnapshot snapshot,
            bool candidateContext)
        {
            if (snapshot == null)
                return;

            AddGarageCategoricalReference(
                content,
                "Operating mode",
                FormatHeadlightMode(snapshot.FactoryProfile).ToUpperInvariant(),
                FormatHeadlightMode(snapshot.CurrentProfile).ToUpperInvariant(),
                FormatHeadlightMode(snapshot.CandidateProfile).ToUpperInvariant(),
                candidateContext);
            AddGarageCategoricalReference(
                content,
                "RGB colour",
                FormatGarageHeadlightColor(snapshot.FactoryEffect),
                FormatGarageHeadlightColor(snapshot.CurrentEffect),
                FormatGarageHeadlightColor(snapshot.CandidateEffect),
                candidateContext);
        }

        private static string FormatGarageHeadlightColor(PartEffect effect)
        {
            if (effect == null || !effect.hasHeadlightColor)
                return "NATIVE";
            Color color = effect.headlightColor;
            return "RGB " + Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f) + ", " +
                   Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f) + ", " +
                   Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f);
        }

        private static VisualElement GarageComparisonMetric(
            GarageMetricDescriptor metric,
            bool candidateContext)
        {
            var group = new VisualElement();
            group.style.flexDirection = FlexDirection.Column;
            group.style.flexShrink = 0f;
            group.style.width = Length.Percent(100f);
            group.style.minWidth = 0f;
            group.style.marginBottom = 7f;

            Label title = MutedLabel(metric.Label);
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.overflow = Overflow.Hidden;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            SetTooltip(title, string.IsNullOrWhiteSpace(metric.Tooltip) ? metric.Label : metric.Tooltip);
            group.Add(title);

            float displayed = candidateContext && metric.HasCandidate
                ? metric.Candidate
                : metric.Current;
            Func<float, string> format = metric.Format ?? (value => value.ToString("F2"));

            var references = new VisualElement();
            references.style.flexDirection = FlexDirection.Row;
            references.style.width = Length.Percent(100f);
            references.style.minWidth = 0f;
            references.style.overflow = Overflow.Hidden;
            string factoryText = "FACTORY  " + format(metric.Factory);
            Label factory = MutedLabel(factoryText);
            factory.style.flexGrow = 1f;
            factory.style.flexShrink = 1f;
            factory.style.width = Length.Percent(50f);
            factory.style.minWidth = 0f;
            factory.style.whiteSpace = WhiteSpace.NoWrap;
            factory.style.overflow = Overflow.Hidden;
            factory.style.fontSize = 10f;
            string selectedText =
                (candidateContext && metric.HasCandidate ? "PROJECTED  " : "CURRENT  ") +
                format(displayed) + "  " + FormatGarageMetricDelta(displayed, metric.Factory, format);
            Label selected = MutedLabel(selectedText);
            selected.style.flexGrow = 1f;
            selected.style.flexShrink = 1f;
            selected.style.width = Length.Percent(50f);
            selected.style.minWidth = 0f;
            selected.style.whiteSpace = WhiteSpace.NoWrap;
            selected.style.overflow = Overflow.Hidden;
            selected.style.unityTextAlign = TextAnchor.MiddleRight;
            selected.style.fontSize = 10f;
            SetTooltip(factory, factoryText);
            SetTooltip(selected, selectedText);
            references.Add(factory);
            references.Add(selected);
            group.Add(references);

            float minimum = metric.SafetyMinimum ?? Mathf.Min(metric.Factory, Mathf.Min(metric.Current, metric.HasCandidate ? metric.Candidate : metric.Current));
            float maximum = metric.SafetyMaximum ?? Mathf.Max(metric.Factory, Mathf.Max(metric.Current, metric.HasCandidate ? metric.Candidate : metric.Current));
            if (!metric.SafetyMinimum.HasValue || !metric.SafetyMaximum.HasValue)
            {
                float pad = Mathf.Max(0.001f, (maximum - minimum) * 0.12f);
                if (maximum <= minimum + 0.0001f)
                    pad = Mathf.Max(1f, Mathf.Abs(maximum) * 0.10f);
                if (!metric.SafetyMinimum.HasValue)
                    minimum -= pad;
                if (!metric.SafetyMaximum.HasValue)
                    maximum += pad;
            }
            if (maximum <= minimum + 0.0001f)
                maximum = minimum + 1f;

            float factoryPosition = Mathf.InverseLerp(minimum, maximum, metric.Factory);
            float displayedPosition = Mathf.InverseLerp(minimum, maximum, displayed);
            var track = new VisualElement();
            track.style.position = Position.Relative;
            track.style.height = 9f;
            track.style.flexShrink = 0f;
            track.style.width = Length.Percent(100f);
            track.style.backgroundColor = new Color(0.10f, 0.12f, 0.14f, 0.98f);
            track.style.overflow = Overflow.Hidden;

            var factoryExtent = new VisualElement();
            factoryExtent.style.position = Position.Absolute;
            factoryExtent.style.left = 0f;
            factoryExtent.style.top = 0f;
            factoryExtent.style.bottom = 0f;
            factoryExtent.style.width = Length.Percent(factoryPosition * 100f);
            factoryExtent.style.backgroundColor = new Color(0.46f, 0.50f, 0.54f, 0.72f);
            track.Add(factoryExtent);

            float changeLeft = Mathf.Min(factoryPosition, displayedPosition);
            float changeWidth = Mathf.Abs(displayedPosition - factoryPosition);
            if (changeWidth > 0.0001f)
            {
                var change = new VisualElement();
                change.style.position = Position.Absolute;
                change.style.left = Length.Percent(changeLeft * 100f);
                change.style.top = 1f;
                change.style.bottom = 1f;
                change.style.width = Length.Percent(Mathf.Max(0.8f, changeWidth * 100f));
                change.style.backgroundColor = GarageMetricChangeColor(metric, displayed);
                track.Add(change);
            }

            var marker = new VisualElement();
            marker.style.position = Position.Absolute;
            marker.style.left = Length.Percent(factoryPosition * 100f);
            marker.style.top = 0f;
            marker.style.bottom = 0f;
            marker.style.width = 2f;
            marker.style.backgroundColor = new Color(0.82f, 0.86f, 0.90f, 1f);
            track.Add(marker);
            group.Add(track);

            if (candidateContext && metric.HasCandidate)
            {
                Label versusCurrent = MutedLabel(
                    "VS CURRENT  " + FormatGarageMetricDelta(metric.Candidate, metric.Current, format));
                versusCurrent.style.fontSize = 9f;
                versusCurrent.style.unityTextAlign = TextAnchor.MiddleRight;
                versusCurrent.style.whiteSpace = WhiteSpace.NoWrap;
                versusCurrent.style.overflow = Overflow.Hidden;
                SetTooltip(versusCurrent, versusCurrent.text);
                group.Add(versusCurrent);
            }
            return group;
        }

        private static string FormatGarageMetricDelta(
            float value,
            float baseline,
            Func<float, string> format)
        {
            float delta = value - baseline;
            if (Mathf.Abs(delta) < 0.0001f)
                return "+0";
            string formatted = format != null ? format(Mathf.Abs(delta)) : Mathf.Abs(delta).ToString("F2");
            return (delta > 0f ? "+" : "-") + formatted;
        }

        private static Color GarageMetricChangeColor(GarageMetricDescriptor metric, float displayed)
        {
            if (metric.Direction == GarageMetricDirection.Preference)
                return new Color(0.18f, 0.62f, 0.94f, 0.95f);
            float delta = displayed - metric.Factory;
            bool beneficial = metric.Direction == GarageMetricDirection.HigherIsBetter
                ? delta >= 0f
                : delta <= 0f;
            return beneficial
                ? AlpineNativeUiConfig.AccentColor
                : new Color(0.96f, 0.47f, 0.12f, 0.96f);
        }

        private static float PositiveEffectMultiplier(PartEffect effect, params string[] names)
        {
            if (effect == null || names == null)
                return 1f;
            float value;
            return TryReadFloatMember(effect, out value, names) && value > 0f &&
                   !float.IsNaN(value) && !float.IsInfinity(value)
                ? value
                : 1f;
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool TryReadFloatMember(object source, out float value, params string[] names)
        {
            value = 0f;
            if (source == null || names == null)
                return false;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            Type type = source.GetType();
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                object raw = null;
                try
                {
                    FieldInfo field = type.GetField(name, flags);
                    raw = field != null ? field.GetValue(source) : null;
                    if (field == null)
                    {
                        PropertyInfo property = type.GetProperty(name, flags);
                        raw = property != null && property.CanRead ? property.GetValue(source, null) : null;
                    }
                }
                catch
                {
                    raw = null;
                }
                if (raw == null)
                    continue;
                try
                {
                    value = Convert.ToSingle(raw, System.Globalization.CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    // Continue to another alias.
                }
            }
            return false;
        }

        private static void BuildGarageCategory(
            AlpineTuningMod mod,
            SUIManagedList rail,
            VisualElement detailContent,
            VehicleScriptableObject target,
            TuneProfile working,
            TuneProfile installedReference,
            string category,
            Action<string, string, string> navigate,
            List<Button> tileButtons)
        {
            VisualElement grid = rail;
            BuildGarageCategorySummary(mod, detailContent, target, working, category);
            string[] partCategories = PartCategoriesForGarageSection(category).ToArray();
            if (partCategories.Length == 0)
            {
                Button unavailable = GarageTile(
                    "UNAVAILABLE",
                    "This category is unavailable in the current game build.",
                    false,
                    null,
                    "action.unavailable");
                unavailable.SetEnabled(false);
                grid.Add(unavailable);
                return;
            }

            foreach (string partCategory in partCategories)
            {
                AddGaragePartTile(
                    mod, grid, tileButtons, target, working, installedReference,
                    detailContent, partCategory, navigate);
            }

            if (string.Equals(category, "engine", StringComparison.OrdinalIgnoreCase))
            {
                AddGarageNavigationTile(
                    grid, tileButtons, "Engine Swap", DonorDisplayName(mod, working),
                    NavigationPart, "engine.donor", "Engine Swap", navigate, "type.engine-swap");
            }
        }

        private static string DonorDisplayName(AlpineTuningMod mod, TuneProfile working)
        {
            if (working == null ||
                (string.IsNullOrWhiteSpace(working.donorSledKey) &&
                 string.IsNullOrWhiteSpace(working.donorVehicleId)))
                return "Selected sled's stock engine";

            VehicleScriptableObject donor = mod.FindSledByIdentity(
                working.donorSledKey,
                working.donorVehicleId);
            SledDefaults donorDefaults = donor != null
                ? StockDefaultsFor(mod, donor)
                : null;
            return donorDefaults != null
                ? EngineDisplayName(donorDefaults)
                : "Unavailable saved engine";
        }

        private static void AddGaragePartTile(
            AlpineTuningMod mod,
            VisualElement grid,
            List<Button> tileButtons,
            VehicleScriptableObject target,
            TuneProfile working,
            TuneProfile installedReference,
            VisualElement detailContent,
            string partCategory,
            Action<string, string, string> navigate)
        {
            string label = mod.Catalog.LabelForCategory(partCategory);
            TunePart selected = mod.Catalog.Find(working.GetPartId(partCategory));
            string subtitle = selected != null ? selected.name : "Choose a part";
            Button tile = AddGarageNavigationTile(
                grid,
                tileButtons,
                label,
                subtitle,
                NavigationPart,
                partCategory,
                label,
                navigate,
                GaragePartTypeIconKey(partCategory));
            Action showDetails = () => ShowGarageSelectedPart(
                mod, detailContent, target, working, installedReference, partCategory);
            tile.RegisterCallback<FocusInEvent>(_ => showDetails());
            tile.RegisterCallback<PointerEnterEvent>(_ => showDetails());
        }

        private static void BuildGarageCategorySummary(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            string category)
        {
            if (content == null)
                return;
            content.Clear();
            string title = string.IsNullOrWhiteSpace(category)
                ? "CURRENT BUILD"
                : category.ToUpperInvariant() + " / BUILD VS FACTORY";
            var section = Section(title);
            GarageComparisonSnapshot snapshot = BuildGarageComparisonSnapshot(mod, target, working, null);
            AddGarageComparisonMetrics(
                section,
                GarageMetricsForSection(snapshot, category, mod.Settings.units),
                false);
            if (string.Equals(category, "engine", StringComparison.OrdinalIgnoreCase))
                AddGarageEngineReferences(mod, section, target, snapshot, false);
            if (string.Equals(category, "lighting", StringComparison.OrdinalIgnoreCase))
                AddGarageLightingReferences(section, snapshot, false);

            var parts = new VisualElement();
            parts.style.flexDirection = FlexDirection.Column;
            parts.style.marginTop = AlpineNativeUiConfig.RowGap;
            foreach (string partCategory in PartCategoriesForGarageSection(category))
            {
                string label = mod.Catalog.LabelForCategory(partCategory);
                TunePart selected = mod.Catalog.Find(working?.GetPartId(partCategory));
                parts.Add(MutedLabel(label + ": " + (selected != null ? selected.name : "Stock")));
            }
            if (string.Equals(category, "engine", StringComparison.OrdinalIgnoreCase))
                parts.Add(MutedLabel("Engine Swap: " + DonorDisplayName(mod, working)));
            section.Add(parts);
            content.Add(section);
        }

        private static void ShowGarageSelectedPart(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            TuneProfile installedReference,
            string partCategory)
        {
            if (content == null)
                return;
            content.Clear();
            TunePart selected = mod.Catalog.Find(working?.GetPartId(partCategory));
            var section = Section(mod.Catalog.LabelForCategory(partCategory));
            if (selected == null)
            {
                section.Add(CardTitle("Stock"));
                content.Add(section);
                return;
            }

            section.Add(CardTitle(selected.name));
            if (!string.IsNullOrWhiteSpace(selected.description))
                section.Add(MutedLabel(selected.description));
            GarageComparisonSnapshot snapshot = BuildGarageComparisonSnapshot(mod, target, working, null);
            section.Add(MutedLabel("SELECTED BUILD"));
            string garageSection = GarageSectionForPartCategory(partCategory);
            AddGarageComparisonMetrics(
                section,
                GarageMetricsForSection(snapshot, garageSection, mod.Settings.units),
                false);
            if (string.Equals(garageSection, "engine", StringComparison.OrdinalIgnoreCase))
                AddGarageEngineReferences(mod, section, target, snapshot, false);
            if (string.Equals(garageSection, "lighting", StringComparison.OrdinalIgnoreCase))
                AddGarageLightingReferences(section, snapshot, false);
            if (selected.requiresReload)
                section.Add(Badge("REBUILD"));
            content.Add(section);
        }

        private static Button AddGarageNavigationTile(
            VisualElement grid,
            List<Button> tileButtons,
            string title,
            string subtitle,
            string kind,
            string id,
            string navigationTitle,
            Action<string, string, string> navigate,
            string iconKey = null,
            string fallbackIconKey = null,
            bool showBrandMark = true)
        {
            Button tile = GarageTile(
                title,
                subtitle,
                false,
                () => navigate(kind, id, navigationTitle),
                iconKey,
                fallbackIconKey,
                showBrandMark);
            tile.name = "AlpineTile-" + SafeElementName(kind + "-" + id);
            grid.Add(tile);
            tileButtons.Add(tile);
            return tile;
        }

        private static Button GarageTile(
            string title,
            string subtitle,
            bool selected,
            Action clicked,
            string iconKey = null,
            string fallbackIconKey = null,
            bool showBrandMark = true)
        {
            var tile = new SUIButtonWithLabel { focusable = true };
            tile.SetText(title ?? string.Empty);
            Texture2D artwork = GarageIconTexture(iconKey, fallbackIconKey);
            if (artwork != null)
                tile.SetImage(artwork);
            if (showBrandMark &&
                string.Equals(iconKey, "action.setups", StringComparison.OrdinalIgnoreCase))
            {
                Texture2D brandMark = GarageIconResources.LoadBrandMark();
                if (brandMark != null)
                    tile.SetLogoImage(brandMark);
            }

            Label titleLabel = tile.Q<Label>();
            if (titleLabel != null)
            {
                // Keep the native label hierarchy and typography. The card itself
                // already supplies the faded backing treatment; a second opaque
                // strip competes with the artwork and overlaps adjacent cards.
                titleLabel.style.color = Color.white;
                titleLabel.style.backgroundColor = Color.clear;
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.overflow = Overflow.Hidden;
            }

            // Sledders' vehicle cards reserve an inset for photos with their own
            // framing. Alpine artwork already includes safe transparent margins,
            // so use the complete image field and scale to fit instead of drawing
            // it as a small image beneath the title treatment.
            Image background = tile.Q<Image>(className: "bg-image");
            if (background != null)
            {
                background.scaleMode = ScaleMode.ScaleToFit;
                background.style.top = 0f;
                background.style.left = 0f;
                background.style.right = 0f;
                background.style.bottom = 0f;
                background.style.width = Length.Percent(100f);
                background.style.height = Length.Percent(100f);
            }
            Image ringBase = tile.Q<Image>(className: "checkmark-base");
            if (ringBase != null)
            {
                // Native SUIButtonWithLabel stores the artwork on its parent
                // bg-image. The base-image/checkmark-base child is only the
                // circular selection plate, so it can be hidden independently.
                ringBase.style.display = DisplayStyle.None;
            }

            // Keep the separate native top-right checkmark. Select/Deselect
            // controls its visibility without restoring the circular artwork
            // backplate removed above.

            Color idleBorder = new Color(0.72f, 0.82f, 0.9f, 0.72f);
            Color activeBorder = AlpineNativeUiConfig.AccentColor;
            bool hasKeyboardFocus = false;
            bool hasPointerHover = false;
            Action updateFocusBorder = () =>
            {
                bool emphasized = selected || hasKeyboardFocus || hasPointerHover;
                Color color = emphasized ? activeBorder : idleBorder;
                float width = emphasized ? 2f : 1f;
                tile.style.borderLeftColor = color;
                tile.style.borderRightColor = color;
                tile.style.borderTopColor = color;
                tile.style.borderBottomColor = color;
                tile.style.borderLeftWidth = width;
                tile.style.borderRightWidth = width;
                tile.style.borderTopWidth = width;
                tile.style.borderBottomWidth = width;
            };
            updateFocusBorder();
            tile.RegisterCallback<FocusInEvent>(_ =>
            {
                hasKeyboardFocus = true;
                updateFocusBorder();
            });
            tile.RegisterCallback<FocusOutEvent>(_ =>
            {
                hasKeyboardFocus = false;
                updateFocusBorder();
            });
            tile.RegisterCallback<PointerEnterEvent>(_ =>
            {
                hasPointerHover = true;
                updateFocusBorder();
            });
            tile.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                hasPointerHover = false;
                updateFocusBorder();
            });
            if (selected)
                tile.Select();
            else
                tile.Deselect();
            if (clicked != null)
                tile.clicked += clicked;
            SetTooltip(tile, subtitle);
            return tile;
        }

        private static Texture2D GarageIconTexture(string iconKey, string fallbackIconKey = null)
        {
            Texture2D artwork = GarageIconResources.LoadGarageIcon(iconKey);
            if (artwork == null &&
                !string.IsNullOrWhiteSpace(fallbackIconKey) &&
                !string.Equals(iconKey, fallbackIconKey, StringComparison.OrdinalIgnoreCase))
            {
                artwork = GarageIconResources.LoadGarageIcon(fallbackIconKey);
            }

            return artwork;
        }

        private static string GaragePartTypeIconKey(string partCategory)
        {
            switch (partCategory)
            {
                case PartCatalog.EngineCore: return "type.engine-core";
                case PartCatalog.EnginePiston: return "type.pistons";
                case PartCatalog.EngineCrank: return "type.crankshaft";
                case PartCatalog.Intake: return "type.intake-exhaust";
                case PartCatalog.Turbo: return "type.turbo";
                case PartCatalog.Clutch: return "type.clutch-calibration";
                case PartCatalog.ClutchWeights: return "type.clutch-weights";
                case PartCatalog.RatioFeel: return "type.gearing";
                case "brakeCalibration": return "type.brake-calibration";
                case PartCatalog.Suspension: return "type.suspension";
                case PartCatalog.Chassis: return "type.chassis";
                case PartCatalog.TrackLimiter: return "type.limiter-strap";
                case PartCatalog.RearShock: return "type.rear-shock";
                case PartCatalog.RearSpring: return "type.rear-spring";
                case PartCatalog.Accessories: return "part.accessory.utility";
                case PartCatalog.Track: return "type.track";
                case PartCatalog.Skis: return "type.skis";
                case "steeringGeometry": return "type.steering-geometry";
                case PartCatalog.HeadlightColor: return "type.headlight-color";
                case PartCatalog.HeadlightBrightness: return "type.headlight-output";
                case PartCatalog.HeadlightBeam: return "type.headlight-beam";
                case PartCatalog.HeadlightAim: return "type.headlight-aim";
                case PartCatalog.FuelTank: return "part.fuel.tank.stock";
                case PartCatalog.BackpackFuel: return "part.fuel.backpack.none";
                default: return null;
            }
        }

        private static VisualElement FindFirstFocusable(VisualElement parent)
        {
            if (parent == null)
                return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                VisualElement child = parent[i];
                if (child == null || child.resolvedStyle.display == DisplayStyle.None)
                    continue;

                if (CanFocus(child))
                    return child;

                VisualElement nested = FindFirstFocusable(child);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static bool CanFocus(VisualElement element)
        {
            return element != null &&
                   element.focusable &&
                   element.canGrabFocus &&
                   element.enabledInHierarchy &&
                   IsActuallyDisplayed(element);
        }

        private static bool IsActuallyDisplayed(VisualElement element)
        {
            if (element == null)
                return false;
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current.resolvedStyle.display == DisplayStyle.None)
                    return false;
            }
            return true;
        }

        private static bool IsDescendantOf(VisualElement element, VisualElement ancestor)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current == ancestor)
                    return true;
            }

            return false;
        }

        private static bool IsInsideTextField(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current is TextField)
                    return true;
            }

            return false;
        }

        private static bool IsInsideButton(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current is Button)
                    return true;
            }
            return false;
        }

        private static void BuildGaragePartPicker(
            AlpineTuningMod mod,
            SUIManagedList rail,
            VehicleScriptableObject target,
            TuneProfile working,
            TuneProfile installedReference,
            string partCategory,
            Action<string, string> selectPart,
            Action setupChanged,
            Action render,
            Action<string> setStatus,
            List<Button> tileButtons,
            VisualElement detailContent)
        {
            string label = mod.Catalog.LabelForCategory(partCategory);
            string selectedId = working.GetPartId(partCategory);
            List<TunePart> parts = mod.Catalog.PartsForCategory(partCategory).ToList();
            TunePart selectedPart = mod.Catalog.Find(selectedId) ?? parts.FirstOrDefault();
            int detailRevision = 0;

            Action<TunePart> showPartDetails = part =>
            {
                detailRevision++;
                detailContent.Clear();
                if (part == null)
                {
                    detailContent.Add(MutedLabel("No compatible parts are available for this sled."));
                    return;
                }

                bool installedInDraft = string.Equals(
                    part.id,
                    working.GetPartId(partCategory),
                    StringComparison.OrdinalIgnoreCase);
                var detail = Section(installedInDraft ? "Selected Part" : "Part Preview");
                detail.Add(CardTitle(part.name));
                detail.Add(MutedLabel(part.description));
                detail.Add(MutedLabel(installedInDraft ? "CURRENT BUILD" : "PROJECTED BUILD"));

                TuneProfile preview = TuneStore.Clone(working);
                preview.SetPartId(partCategory, part.id);
                GarageComparisonSnapshot snapshot = BuildGarageComparisonSnapshot(
                    mod,
                    target,
                    working,
                    installedInDraft ? null : preview);
                string garageSection = GarageSectionForPartCategory(partCategory);
                AddGarageComparisonMetrics(
                    detail,
                    GarageMetricsForSection(snapshot, garageSection, mod.Settings.units),
                    !installedInDraft);
                if (string.Equals(garageSection, "engine", StringComparison.OrdinalIgnoreCase))
                    AddGarageEngineReferences(mod, detail, target, snapshot, !installedInDraft);
                if (string.Equals(garageSection, "lighting", StringComparison.OrdinalIgnoreCase))
                    AddGarageLightingReferences(detail, snapshot, !installedInDraft);
                if (part.effect != null && part.effect.requiresCosmeticBackpack)
                    detail.Add(Badge(mod.FuelSystem != null && mod.FuelSystem.HasWornCosmeticBackpack()
                        ? "RIDER FUEL OK"
                        : "REQUIRES WORN BACKPACK"));
                if (part.requiresReload)
                    detail.Add(Badge("REBUILD"));
                detailContent.Add(detail);
                BuildGaragePartAdjustments(
                    mod, detailContent, target, working, partCategory,
                    setupChanged, render, setStatus);
            };

            foreach (TunePart part in parts)
            {
                TunePart captured = part;
                bool selected = string.Equals(captured.id, selectedId, StringComparison.OrdinalIgnoreCase);
                string subtitle = captured.description ?? string.Empty;
                if (captured.requiresReload)
                    subtitle += "  Native spawn component.";
                bool backpackRequired = captured.effect != null && captured.effect.requiresCosmeticBackpack;
                bool backpackAvailable = !backpackRequired ||
                    (mod.FuelSystem != null && mod.FuelSystem.HasWornCosmeticBackpack());
                if (backpackRequired && !backpackAvailable)
                    subtitle += "  Requires a worn cosmetic backpack.";

                Button tile = GarageTile(captured.name, subtitle, selected, () =>
                {
                    if (!backpackAvailable)
                    {
                        setStatus?.Invoke("Wear a cosmetic backpack first");
                        return;
                    }
                    selectPart?.Invoke(partCategory, captured.id);
                }, "part." + captured.id);
                tile.SetEnabled(backpackAvailable || selected);
                tile.name = "AlpinePart-" + SafeElementName(captured.id);
                tile.RegisterCallback<FocusInEvent>(_ => showPartDetails(captured));
                tile.RegisterCallback<PointerEnterEvent>(_ => showPartDetails(captured));
                tile.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    int leavingRevision = detailRevision;
                    tile.schedule.Execute(() =>
                    {
                        if (leavingRevision != detailRevision || ReferenceEquals(FocusedElement(rail), tile))
                            return;
                        showPartDetails(selectedPart);
                    });
                });
                rail.Add(tile);
                if (selected)
                    tileButtons.Insert(0, tile);
                else
                    tileButtons.Add(tile);
            }

            if (parts.Count == 0)
            {
                Button unavailable = GarageTile(
                    "NO COMPATIBLE PARTS",
                    "No compatible choices are available for this sled.",
                    false,
                    null,
                    "action.unavailable");
                unavailable.name = "AlpinePart-NoCompatibleParts";
                unavailable.SetEnabled(false);
                rail.Add(unavailable);
            }

            showPartDetails(selectedPart ?? parts.FirstOrDefault());
        }

        private static void BuildGaragePartAdjustments(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            string partCategory,
            Action setupChanged,
            Action render,
            Action<string> setStatus)
        {
            FineTuneSettings fine = working.fineTune ?? (working.fineTune = new FineTuneSettings());
            if (partCategory == PartCatalog.EngineCore ||
                partCategory == PartCatalog.EnginePiston ||
                partCategory == PartCatalog.EngineCrank ||
                partCategory == PartCatalog.Intake ||
                partCategory == PartCatalog.Turbo)
            {
                var section = Section("Fine Adjustment");
                AddSlider(section, "Power Trim",
                    AlpineNativeUiConfig.PowerTrimMin, AlpineNativeUiConfig.PowerTrimMax,
                    fine.powerTrimPercent, "F1", "%",
                    value => fine.powerTrimPercent = value, setupChanged,
                    "Fine output adjustment applied only when the setup is saved.");
                AddSlider(section, "Weight Trim",
                    AlpineNativeUiConfig.WeightTrimMin, AlpineNativeUiConfig.WeightTrimMax,
                    fine.weightTrimPercent, "F1", "%",
                    value => fine.weightTrimPercent = value, setupChanged,
                    "Fine setup-weight adjustment.");
                content.Add(section);
                return;
            }

            if (partCategory == PartCatalog.Clutch ||
                partCategory == PartCatalog.ClutchWeights ||
                partCategory == PartCatalog.RatioFeel)
            {
                var section = Section("Fine Adjustment");
                AddSlider(section, "Clutch Response",
                    AlpineNativeUiConfig.ClutchTrimMin, AlpineNativeUiConfig.ClutchTrimMax,
                    fine.clutchTrimPercent, "F1", "%",
                    value => fine.clutchTrimPercent = value, setupChanged,
                    "Adjusts engagement and RPM response around the selected drivetrain hardware.");
                content.Add(section);
                return;
            }

            if (partCategory == PartCatalog.Track)
            {
                var section = Section("Fine Adjustment");
                AddSlider(section, "Traction Trim",
                    AlpineNativeUiConfig.TractionTrimMin, AlpineNativeUiConfig.TractionTrimMax,
                    fine.tractionTrimPercent, "F1", "%",
                    value => fine.tractionTrimPercent = value, setupChanged,
                    "Fine snow-bite adjustment around the selected track.");
                content.Add(section);
                return;
            }

            if (partCategory == PartCatalog.Skis)
            {
                var section = Section("Fine Adjustment");
                AddSlider(section, "Ski Stance",
                    AlpineNativeUiConfig.SkiStanceMin, AlpineNativeUiConfig.SkiStanceMax,
                    fine.skiStanceTrim, "F3", " m",
                    value => fine.skiStanceTrim = value, setupChanged,
                    "Wider favors stability; narrower favors quicker turn-in.");
                content.Add(section);
                return;
            }

            if (partCategory == PartCatalog.Suspension ||
                partCategory == PartCatalog.Chassis ||
                partCategory == PartCatalog.TrackLimiter ||
                partCategory == PartCatalog.RearShock ||
                partCategory == PartCatalog.RearSpring)
            {
                var section = Section("Balance Adjustment");
                AddSlider(section, "Center of Mass Height",
                    AlpineNativeUiConfig.CenterOfMassYMin, AlpineNativeUiConfig.CenterOfMassYMax,
                    fine.centerOfMassYTrim, "F3", " m",
                    value => fine.centerOfMassYTrim = value, setupChanged,
                    "Moves setup balance vertically.");
                AddSlider(section, "Fore / Aft Balance",
                    AlpineNativeUiConfig.CenterOfMassZMin, AlpineNativeUiConfig.CenterOfMassZMax,
                    fine.centerOfMassZTrim, "F3", " m",
                    value => fine.centerOfMassZTrim = value, setupChanged,
                    "Moves setup balance forward or rearward.");
                content.Add(section);
                return;
            }

            if (partCategory == PartCatalog.HeadlightColor ||
                partCategory == PartCatalog.HeadlightBrightness ||
                partCategory == PartCatalog.HeadlightBeam ||
                partCategory == PartCatalog.HeadlightAim)
            {
                BuildGarageLightingControls(
                    mod, content, target, working, setupChanged, render, setStatus);
            }
        }

        private static void BuildGarageEnginePicker(
            AlpineTuningMod mod,
            SUIManagedList rail,
            VehicleScriptableObject target,
            TuneProfile working,
            TuneProfile installedReference,
            Action setupChanged,
            Action render,
            Action<string> setStatus,
            List<Button> tileButtons,
            VisualElement detailContent)
        {
            List<GarageEngineCandidate> candidates = mod.SelectableSleds
                .Where(vehicle => vehicle != null)
                .Select(vehicle => new GarageEngineCandidate(
                    vehicle,
                    StockDefaultsFor(mod, vehicle)))
                .Where(candidate => candidate.StockDefaults != null &&
                                    !string.IsNullOrWhiteSpace(candidate.Signature))
                .ToList();
            SledDefaults targetDefaults = StockDefaultsFor(mod, target);
            string stockSignature = EngineSignature(targetDefaults, target);
            bool hasSavedDonor = !string.IsNullOrWhiteSpace(working.donorSledKey) ||
                                 !string.IsNullOrWhiteSpace(working.donorVehicleId);
            VehicleScriptableObject selectedDonor = !hasSavedDonor
                ? null
                : mod.FindSledByIdentity(working.donorSledKey, working.donorVehicleId);
            SledDefaults selectedDonorDefaults = selectedDonor != null
                ? StockDefaultsFor(mod, selectedDonor)
                : null;
            bool selectedDonorUnavailable = hasSavedDonor &&
                                            (selectedDonor == null || selectedDonorDefaults == null);
            string selectedSignature = EngineSignature(selectedDonorDefaults, selectedDonor);
            int detailRevision = 0;
            Action restoreCurrentDetails = null;
            Action<Button> registerPointerRestore = tile =>
            {
                tile.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    int leavingRevision = detailRevision;
                    tile.schedule.Execute(() =>
                    {
                        if (leavingRevision != detailRevision || ReferenceEquals(FocusedElement(rail), tile))
                            return;
                        restoreCurrentDetails?.Invoke();
                    });
                });
            };

            Action<GarageEngineCandidate, int, bool> showEngineDetails =
                (candidate, modelCount, stock) =>
                {
                    detailRevision++;
                    SledDefaults engineDefaults = candidate?.StockDefaults;
                    VehicleScriptableObject engineVehicle = candidate?.Vehicle;
                    string name = EngineDisplayName(engineDefaults);
                    detailContent.Clear();
                    var detail = Section(stock ? "Stock Engine" : "Engine Swap");
                    detail.Add(CardTitle(name));
                    detail.Add(MutedLabel(stock
                        ? "The engine originally paired with the selected sled."
                        : "One native engine definition shared by " + modelCount +
                          (modelCount == 1 ? " compatible model." : " compatible models.")));

                    if (engineDefaults != null)
                    {
                        detail.Add(MutedLabel($"POWER {engineDefaults.horsePower:F0} HP"));
                        detail.Add(MutedLabel(engineDefaults.isTurboOn ? "TURBO" : "NATURALLY ASPIRATED"));
                    }

                    TuneProfile preview = TuneStore.Clone(working);
                    preview.donorSledKey = stock || engineVehicle == null
                        ? null
                        : AlpineTuningMod.GetSledKey(engineVehicle);
                    preview.donorVehicleId = stock || engineVehicle == null
                        ? null
                        : AlpineTuningMod.GetVehicleId(engineVehicle);

                    bool selectedInDraft = SameGarageDonor(preview, working);
                    detail.Add(MutedLabel(selectedInDraft
                        ? "CURRENT BUILD"
                        : "PROJECTED BUILD"));
                    GarageComparisonSnapshot snapshot = BuildGarageComparisonSnapshot(
                        mod,
                        target,
                        working,
                        selectedInDraft ? null : preview);
                    AddGarageComparisonMetrics(
                        detail,
                        GarageMetricsForSection(snapshot, "engine", mod.Settings.units),
                        !selectedInDraft);
                    AddGarageEngineReferences(mod, detail, target, snapshot, !selectedInDraft);
                    if (!selectedInDraft)
                        detail.Add(Badge("REBUILD"));
                    detailContent.Add(detail);
                };

            bool stockSelected = !hasSavedDonor ||
                                 (selectedDonor != null &&
                                  !string.IsNullOrWhiteSpace(stockSignature) &&
                                  !string.IsNullOrWhiteSpace(selectedSignature) &&
                                  string.Equals(selectedSignature, stockSignature, StringComparison.Ordinal));
            bool suppressNextStockFocusDetail = selectedDonorUnavailable;
            Button stockTile = GarageTile(
                EngineDisplayName(targetDefaults),
                "Selected sled's native engine",
                stockSelected,
                () =>
                {
                    if (stockSelected)
                    {
                        setStatus?.Invoke("This engine is already selected.");
                        return;
                    }
                    working.donorSledKey = null;
                    working.donorVehicleId = null;
                    setupChanged?.Invoke();
                    render?.Invoke();
                },
                EngineNativeIconKey(targetDefaults),
                "engine.stock-native");
            stockTile.name = "AlpineEngine-Stock";
            stockTile.RegisterCallback<FocusInEvent>(_ =>
            {
                // When an unavailable saved donor is selected, controller focus
                // still needs a valid landing point. Preserve the unavailable
                // explanation through that first automatic focus restoration;
                // later visits preview Stock normally.
                if (suppressNextStockFocusDetail)
                {
                    suppressNextStockFocusDetail = false;
                    return;
                }
                showEngineDetails(new GarageEngineCandidate(target, targetDefaults), 1, true);
            });
            stockTile.RegisterCallback<PointerEnterEvent>(_ =>
                showEngineDetails(new GarageEngineCandidate(target, targetDefaults), 1, true));
            registerPointerRestore(stockTile);
            rail.Add(stockTile);
            if (stockSelected)
                tileButtons.Insert(0, stockTile);
            else
                tileButtons.Add(stockTile);

            Action showUnavailableEngineDetails = () =>
            {
                detailRevision++;
                detailContent.Clear();
                var detail = Section("Unavailable Engine");
                detail.Add(CardTitle("Unavailable Engine"));
                detail.Add(MutedLabel(
                    "The engine saved in this draft is not present in the currently loaded native vehicle definitions. " +
                    "The saved donor identity has been preserved; choose Stock or another engine to replace it."));
                detailContent.Add(detail);
            };
            if (selectedDonorUnavailable)
            {
                Button unavailableTile = GarageTile(
                    "UNAVAILABLE ENGINE",
                    "The saved native engine is not currently loaded. Choose a replacement to change the draft.",
                    true,
                    null,
                    "engine.unavailable");
                unavailableTile.name = "AlpineEngine-Unavailable";
                unavailableTile.SetEnabled(false);
                rail.Add(unavailableTile);
                tileButtons.Insert(0, unavailableTile);
                showUnavailableEngineDetails();
            }

            var engineGroups = candidates
                .GroupBy(candidate => candidate.Signature, StringComparer.Ordinal)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key) &&
                                !string.Equals(group.Key, stockSignature, StringComparison.Ordinal))
                .Select(group => new
                {
                    Signature = group.Key,
                    Representative = group
                        .OrderBy(candidate => AlpineTuningMod.GetVehicleId(candidate.Vehicle), StringComparer.OrdinalIgnoreCase)
                        .First(),
                    Count = group.Count()
                })
                .OrderBy(group => EngineDisplayName(group.Representative.StockDefaults), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var engineGroup in engineGroups)
            {
                GarageEngineCandidate engine = engineGroup.Representative;
                string signature = engineGroup.Signature;
                string engineName = EngineDisplayName(engine.StockDefaults);
                int modelCount = engineGroup.Count;
                bool selected = selectedDonor != null &&
                                string.Equals(selectedSignature, signature, StringComparison.Ordinal);
                Button tile = GarageTile(
                    engineName,
                    $"{engine.StockDefaults.horsePower:F0} hp  |  " +
                    (engine.StockDefaults.isTurboOn ? "Turbo" : "Naturally aspirated") +
                    $"  |  {modelCount} model" + (modelCount == 1 ? string.Empty : "s"),
                    selected,
                    () =>
                    {
                        if (selected)
                        {
                            setStatus?.Invoke("This engine is already selected.");
                            return;
                        }

                        working.donorSledKey = AlpineTuningMod.GetSledKey(engine.Vehicle);
                        working.donorVehicleId = AlpineTuningMod.GetVehicleId(engine.Vehicle);
                        setupChanged?.Invoke();
                        render?.Invoke();
                    },
                    EngineNativeIconKey(engine.StockDefaults),
                    engine.StockDefaults.isTurboOn ? "engine.generic-turbo" : "engine.generic-na");
                tile.name = "AlpineEngine-" + SafeElementName(signature);
                tile.RegisterCallback<FocusInEvent>(_ =>
                    showEngineDetails(engine, modelCount, false));
                tile.RegisterCallback<PointerEnterEvent>(_ =>
                    showEngineDetails(engine, modelCount, false));
                registerPointerRestore(tile);
                rail.Add(tile);
                if (selected)
                    tileButtons.Insert(0, tile);
                else
                    tileButtons.Add(tile);
            }

            if (selectedDonorUnavailable)
            {
                restoreCurrentDetails = showUnavailableEngineDetails;
            }
            else if (selectedDonor != null)
            {
                var selectedGroup = engineGroups.FirstOrDefault(group =>
                    string.Equals(group.Signature, selectedSignature, StringComparison.Ordinal));
                restoreCurrentDetails = selectedGroup != null
                    ? (Action)(() => showEngineDetails(selectedGroup.Representative, selectedGroup.Count, false))
                    : () => showEngineDetails(new GarageEngineCandidate(target, targetDefaults), 1, true);
            }
            else
            {
                restoreCurrentDetails = () =>
                    showEngineDetails(new GarageEngineCandidate(target, targetDefaults), 1, true);
            }

            if (engineGroups.Count == 0)
            {
                restoreCurrentDetails?.Invoke();
                detailContent.Add(MutedLabel("No additional native engine definitions are loaded."));
            }
            else
                restoreCurrentDetails?.Invoke();
        }

        private static SledDefaults StockDefaultsFor(
            AlpineTuningMod mod,
            VehicleScriptableObject vehicle)
        {
            if (mod?.Store == null || vehicle == null)
                return null;

            return mod.Store.GetDefaults(
                AlpineTuningMod.GetSledKey(vehicle),
                AlpineTuningMod.GetVehicleId(vehicle));
        }

        private static bool SameGarageDonor(TuneProfile left, TuneProfile right)
        {
            if (left == null || right == null)
                return left == null && right == null;
            return string.Equals(left.donorSledKey, right.donorSledKey, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(left.donorVehicleId, right.donorVehicleId, StringComparison.OrdinalIgnoreCase);
        }

        private static string EngineDisplayName(SledDefaults defaults)
        {
            if (defaults == null)
                return "Stock Engine";
            if (!string.IsNullOrWhiteSpace(defaults.engineText))
                return defaults.engineText.Trim();
            return $"{defaults.horsePower:F0} HP " + (defaults.isTurboOn ? "Turbo" : "Engine");
        }

        private static string EngineNativeIconKey(SledDefaults defaults)
        {
            if (defaults == null ||
                float.IsNaN(defaults.horsePower) || float.IsInfinity(defaults.horsePower) ||
                float.IsNaN(defaults.powerFactor) || float.IsInfinity(defaults.powerFactor) ||
                !HasStoredEngineAudioToken(defaults))
            {
                return null;
            }

            double roundedHorsePower = Math.Round(
                defaults.horsePower,
                MidpointRounding.AwayFromZero);
            if (roundedHorsePower < int.MinValue || roundedHorsePower > int.MaxValue)
            {
                return null;
            }
            // powerFactor is not presented or tuned as physics. It remains part
            // of this resource-only discriminator because multiple shipped
            // native definitions share name/HP/audio but have distinct artwork.
            double roundedPowerFactor = Math.Round(
                defaults.powerFactor * 1000d,
                MidpointRounding.AwayFromZero);
            if (roundedPowerFactor < int.MinValue || roundedPowerFactor > int.MaxValue)
                return null;

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "engine.native-{0}-{1}-{2}-{3}-{4}",
                AsciiIconSlug(defaults.engineText),
                (int)roundedHorsePower,
                (int)roundedPowerFactor,
                defaults.isTurboOn ? "t" : "n",
                defaults.engineAudioEnumRawValue);
        }

        private static string AsciiIconSlug(string value)
        {
            var slug = new System.Text.StringBuilder();
            bool pendingSeparator = false;
            foreach (char raw in value ?? string.Empty)
            {
                char current = raw >= 'A' && raw <= 'Z'
                    ? (char)(raw + ('a' - 'A'))
                    : raw;
                bool asciiLetter = current >= 'a' && current <= 'z';
                bool asciiDigit = current >= '0' && current <= '9';
                if (!asciiLetter && !asciiDigit)
                {
                    pendingSeparator |= slug.Length > 0;
                    continue;
                }

                if (pendingSeparator)
                    slug.Append('-');
                slug.Append(current);
                pendingSeparator = false;
            }

            return slug.Length > 0 ? slug.ToString() : "unnamed";
        }

        private static bool HasStoredEngineAudioToken(SledDefaults defaults)
        {
            return defaults != null &&
                   !string.IsNullOrWhiteSpace(defaults.engineAudioEnumType) &&
                   (!string.IsNullOrWhiteSpace(defaults.engineAudioEnumName) ||
                    defaults.engineAudioEnumRawValue != 0);
        }

        private static string ExactFloatBits(float value)
        {
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            return unchecked((uint)bits).ToString(
                "X8",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string EngineSignature(
            SledDefaults defaults,
            VehicleScriptableObject identitySource)
        {
            if (defaults == null)
                return null;

            string nativeName = (defaults.engineText ?? string.Empty).Trim();
            string audioSignature;
            if (HasStoredEngineAudioToken(defaults))
            {
                audioSignature = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}:{1}:{2}",
                    (defaults.engineAudioEnumType ?? string.Empty).Trim().ToUpperInvariant(),
                    (defaults.engineAudioEnumName ?? string.Empty).Trim().ToUpperInvariant(),
                    defaults.engineAudioEnumRawValue);
            }
            else
            {
                // Without a verified audio token we cannot prove that two native
                // definitions are the same engine. Keep them separate instead of
                // silently collapsing a distinct engine/audio package.
                audioSignature = "UNKNOWN:" +
                                 (defaults.vehicleId ??
                                  defaults.sledKey ??
                                  AlpineTuningMod.GetVehicleId(identitySource) ??
                                  AlpineTuningMod.GetSledKey(identitySource) ??
                                  identitySource?.name ?? string.Empty).ToUpperInvariant();
            }
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}|HP:{1}|PF:{2}|{3}|{4}",
                string.IsNullOrWhiteSpace(nativeName) ? "UNNAMED" : nativeName.ToUpperInvariant(),
                ExactFloatBits(defaults.horsePower),
                ExactFloatBits(defaults.powerFactor),
                defaults.isTurboOn ? "T" : "N",
                audioSignature);
        }

        private static void BuildGarageFocusedPanel(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            string panelId,
            Action<TuneProfile> setWorking,
            Action render,
            Action setupChanged,
            Action<string> setStatus,
            Func<bool> saveAsNewSetup,
            Func<bool> hasUnsavedDraft,
            Func<TuneProfile, bool> loadSetupSlot,
            Func<TuneProfile, bool> setDefaultSetupSlot,
            Func<string> getSelectedProfileId,
            Action<string> setSelectedProfileId,
            Func<string> getPendingDeleteProfileId,
            Action<string> setPendingDeleteProfileId,
            Func<string> getPendingLoadProfileId,
            Action<string> setPendingLoadProfileId,
            Func<bool> getFactoryResetArmed,
            Action<bool> setFactoryResetArmed,
            List<Button> tileButtons,
            SUIManagedList tileContent,
            Action<string, string, string> navigate,
            Func<bool> getClearBindingArmed,
            Action<bool> setClearBindingArmed,
            Action closeDyno)
        {
            if (string.Equals(panelId, "setups", StringComparison.OrdinalIgnoreCase))
            {
                BuildGaragePresets(
                    mod, content, target, working, setWorking, render, setStatus, setupChanged,
                    saveAsNewSetup,
                    hasUnsavedDraft, loadSetupSlot, setDefaultSetupSlot,
                    getSelectedProfileId, setSelectedProfileId,
                    getPendingDeleteProfileId, setPendingDeleteProfileId,
                    getPendingLoadProfileId, setPendingLoadProfileId,
                    getFactoryResetArmed, setFactoryResetArmed,
                    tileButtons, tileContent);
                return;
            }

            if (string.Equals(panelId, "settings", StringComparison.OrdinalIgnoreCase))
            {
                BuildGarageSettingsRoot(content, tileContent, tileButtons, navigate);
                return;
            }

            if (string.Equals(panelId, "settings.runtime", StringComparison.OrdinalIgnoreCase))
            {
                BuildGarageRuntimeSettings(mod, content, tileContent, tileButtons, render, setStatus);
                return;
            }

            if (string.Equals(panelId, "settings.fuel", StringComparison.OrdinalIgnoreCase))
            {
                BuildGarageFuelSettings(mod, content, tileContent, tileButtons, render, setStatus);
                return;
            }

            if (string.Equals(panelId, "settings.display", StringComparison.OrdinalIgnoreCase))
            {
                BuildGarageDisplaySettings(mod, content, tileContent, tileButtons, render, setStatus);
                return;
            }

            if (string.Equals(panelId, "settings.hotkey", StringComparison.OrdinalIgnoreCase))
            {
                BuildGarageHotkeySettings(
                    mod,
                    content,
                    tileContent,
                    tileButtons,
                    render,
                    setStatus,
                    getClearBindingArmed,
                    setClearBindingArmed,
                    closeDyno);
                return;
            }

            Button unavailable = GarageTile(
                "PANEL UNAVAILABLE",
                "This view could not be opened. Back remains available.",
                false,
                null,
                "action.unavailable");
            unavailable.SetEnabled(false);
            tileContent?.Add(unavailable);
            content.Add(MutedLabel("This view is unavailable. Use Back."));
        }

        private static void BuildGarageSettingsRoot(
            VisualElement content,
            SUIManagedList rail,
            List<Button> tileButtons,
            Action<string, string, string> navigate)
        {
            var detail = Section("Settings");
            detail.Add(MutedLabel("Choose a settings group."));
            content.Add(detail);
            AddGarageNavigationTile(
                rail, tileButtons, "Runtime", "Enable or disable all Alpine runtime tuning without removing the mod.",
                NavigationPanel, "settings.runtime", "Runtime", navigate,
                "settings.runtime", "action.settings", false);
            AddGarageNavigationTile(
                rail, tileButtons, "Fuel", "Idle consumption and per-sled fuel persistence.",
                NavigationPanel, "settings.fuel", "Fuel", navigate,
                "settings.fuel", "action.settings", false);
            AddGarageNavigationTile(
                rail, tileButtons, "Display", "Metric or Imperial values.",
                NavigationPanel, "settings.display", "Display", navigate,
                "settings.display", "action.settings", false);
            AddGarageNavigationTile(
                rail, tileButtons, "Headlight Hotkey", "Enable, bind or clear headlight controls.",
                NavigationPanel, "settings.hotkey", "Headlight Hotkey", navigate,
                "settings.hotkey", "action.settings", false);
        }

        private static void BuildGarageDisplaySettings(
            AlpineTuningMod mod,
            VisualElement content,
            SUIManagedList rail,
            List<Button> tileButtons,
            Action render,
            Action<string> setStatus)
        {
            AlpineUserSettings settings = mod.Settings;
            var detail = Section("Display");
            detail.Add(MutedLabel(
                settings.units == AlpineDisplayUnits.Metric
                    ? "CURRENT  METRIC"
                    : "CURRENT  IMPERIAL"));
            detail.Add(MutedLabel("Comparison cards and Dyno update immediately."));
            content.Add(detail);

            Action<AlpineDisplayUnits, string> selectUnits = (units, label) =>
            {
                if (settings.units == units)
                {
                    setStatus?.Invoke(label);
                    return;
                }
                AlpineDisplayUnits previous = settings.units;
                settings.units = units;
                if (!mod.SaveSettings())
                {
                    settings.units = previous;
                    setStatus?.Invoke("Save failed");
                }
                else
                {
                    setStatus?.Invoke(label);
                }
                render?.Invoke();
            };

            Button metric = GarageTile(
                "METRIC",
                "Kilowatts, kilograms, millimetres and kilometres per hour.",
                settings.units == AlpineDisplayUnits.Metric,
                () => selectUnits(AlpineDisplayUnits.Metric, "Metric"),
                "settings.metric",
                "settings.display",
                false);
            metric.name = "AlpineSettings-Metric";
            rail.Add(metric);
            tileButtons.Add(metric);

            Button imperial = GarageTile(
                "IMPERIAL",
                "Horsepower, pounds, inches and miles per hour.",
                settings.units == AlpineDisplayUnits.Imperial,
                () => selectUnits(AlpineDisplayUnits.Imperial, "Imperial"),
                "settings.imperial",
                "settings.display",
                false);
            imperial.name = "AlpineSettings-Imperial";
            rail.Add(imperial);
            tileButtons.Add(imperial);
        }

        private static void BuildGarageRuntimeSettings(
            AlpineTuningMod mod,
            VisualElement content,
            SUIManagedList rail,
            List<Button> tileButtons,
            Action render,
            Action<string> setStatus)
        {
            AlpineUserSettings settings = mod.Settings;
            var detail = Section("Alpine Runtime");
            detail.Add(MutedLabel(settings.alpineTuningEnabled
                ? "STATUS  ENABLED · saved Alpine setup affects the live sled."
                : "STATUS  DISABLED · vanilla sled values remain active; saved tunes are retained."));
            detail.Add(MutedLabel("Disabling does not delete or reset any saved setup."));
            content.Add(detail);

            Action<bool> setEnabled = enabled =>
            {
                string message;
                if (!mod.SetAlpineTuningEnabled(enabled, out message))
                    setStatus?.Invoke(string.IsNullOrWhiteSpace(message) ? "Save failed" : message);
                else
                    setStatus?.Invoke(message);
                render?.Invoke();
            };

            Button enabledTile = GarageTile(
                "ENABLED",
                "Apply saved Alpine tuning to sled runtime.",
                settings.alpineTuningEnabled,
                () => setEnabled(true),
                "settings.runtime.enabled", "settings.runtime", false);
            Button disabledTile = GarageTile(
                "DISABLED / VANILLA",
                "Keep the mod and tune library loaded, but stop Alpine from changing sled runtime values.",
                !settings.alpineTuningEnabled,
                () => setEnabled(false),
                "settings.runtime.disabled", "settings.runtime", false);
            rail.Add(enabledTile);
            rail.Add(disabledTile);
            tileButtons.Add(enabledTile);
            tileButtons.Add(disabledTile);
        }

        private static void BuildGarageFuelSettings(
            AlpineTuningMod mod,
            VisualElement content,
            SUIManagedList rail,
            List<Button> tileButtons,
            Action render,
            Action<string> setStatus)
        {
            AlpineUserSettings settings = mod.Settings;
            var detail = Section("Fuel");
            detail.Add(MutedLabel("IDLE BURN  " + (settings.idleFuelConsumptionEnabled ? "ON" : "OFF")));
            detail.Add(MutedLabel("PER-SLED PERSISTENCE  " + (settings.persistentFuelLevelsEnabled ? "ON" : "OFF")));
            detail.Add(MutedLabel("Reverse fuel correction remains active whenever Alpine runtime tuning is enabled."));
            content.Add(detail);

            Action<Action, string> saveToggle = (change, label) =>
            {
                change();
                if (!mod.SaveSettings())
                {
                    change();
                    setStatus?.Invoke("Save failed");
                }
                else
                    setStatus?.Invoke(label);
                render?.Invoke();
            };

            Button idleOn = GarageTile(
                "IDLE BURN ON", "Engine-on idle consumes a small fuel floor.",
                settings.idleFuelConsumptionEnabled,
                () => { if (!settings.idleFuelConsumptionEnabled) saveToggle(() => settings.idleFuelConsumptionEnabled = !settings.idleFuelConsumptionEnabled, "Idle burn on"); },
                "settings.fuel.idle-on", "settings.fuel", false);
            Button idleOff = GarageTile(
                "IDLE BURN OFF", "Leave native zero-load fuel behavior unchanged.",
                !settings.idleFuelConsumptionEnabled,
                () => { if (settings.idleFuelConsumptionEnabled) saveToggle(() => settings.idleFuelConsumptionEnabled = !settings.idleFuelConsumptionEnabled, "Idle burn off"); },
                "settings.fuel.idle-off", "settings.fuel", false);
            Button persistOn = GarageTile(
                "PERSIST FUEL ON", "Remember remaining tank and backpack fuel for each sled between sessions.",
                settings.persistentFuelLevelsEnabled,
                () => { if (!settings.persistentFuelLevelsEnabled) saveToggle(() => settings.persistentFuelLevelsEnabled = !settings.persistentFuelLevelsEnabled, "Fuel persistence on"); },
                "settings.fuel.persist-on", "settings.fuel", false);
            Button persistOff = GarageTile(
                "PERSIST FUEL OFF", "Do not restore saved per-sled fuel on later sessions. Capacity-change litre retention still applies during rebuilds.",
                !settings.persistentFuelLevelsEnabled,
                () => { if (settings.persistentFuelLevelsEnabled) saveToggle(() => settings.persistentFuelLevelsEnabled = !settings.persistentFuelLevelsEnabled, "Fuel persistence off"); },
                "settings.fuel.persist-off", "settings.fuel", false);

            foreach (Button tile in new[] { idleOn, idleOff, persistOn, persistOff })
            {
                rail.Add(tile);
                tileButtons.Add(tile);
            }
        }

        private static void BuildGarageHotkeySettings(
            AlpineTuningMod mod,
            VisualElement content,
            SUIManagedList rail,
            List<Button> tileButtons,
            Action render,
            Action<string> setStatus,
            Func<bool> getClearBindingArmed,
            Action<bool> setClearBindingArmed,
            Action closeDyno)
        {
            AlpineUserSettings settings = mod.Settings;
            bool capturing = mod.IsCapturingHeadlightBinding;
            bool clearArmed = getClearBindingArmed != null && getClearBindingArmed();
            var detail = Section("Headlight Hotkey");
            detail.Add(MutedLabel("STATUS  " + (settings.headlightToggleEnabled ? "ENABLED" : "DISABLED")));
            detail.Add(MutedLabel("KEYBOARD  " + FormatSingleHeadlightBinding(settings.headlightKeyboardKey)));
            detail.Add(MutedLabel("CONTROLLER  " + FormatSingleHeadlightBinding(settings.headlightControllerButton, true)));
            Label captureBadge = null;
            if (capturing)
            {
                captureBadge = Badge("WAITING");
                SetTooltip(captureBadge, "Press an input. Native Cancel, Escape or controller Cancel aborts capture.");
                detail.Add(captureBadge);
            }
            else if (clearArmed)
            {
                detail.Add(Badge("CONFIRM CLEAR"));
                detail.Add(MutedLabel("Confirm to remove both saved bindings."));
            }
            content.Add(detail);

            Action<bool> setEnabled = requestedEnabled =>
            {
                if (capturing || clearArmed)
                    return;
                if (requestedEnabled && !HasConfiguredHeadlightBinding(settings))
                {
                    setStatus?.Invoke("Bind first");
                    return;
                }
                bool previous = settings.headlightToggleEnabled;
                settings.headlightToggleEnabled = requestedEnabled;
                settings.Normalize();
                if (!mod.SaveSettings())
                {
                    settings.headlightToggleEnabled = previous;
                    setStatus?.Invoke("Save failed");
                }
                else
                {
                    setStatus?.Invoke(requestedEnabled ? "Enabled" : "Disabled");
                }
                render?.Invoke();
            };

            Button enabledTile = GarageTile(
                "ENABLED", "Allow the configured headlight hotkey.",
                settings.headlightToggleEnabled,
                () => setEnabled(true), "settings.enabled", "settings.hotkey", false);
            enabledTile.name = "AlpineSettings-HotkeyEnabled";
            Button disabledTile = GarageTile(
                "DISABLED", "Keep bindings but ignore the hotkey.",
                !settings.headlightToggleEnabled,
                () => setEnabled(false), "settings.disabled", "settings.hotkey", false);
            disabledTile.name = "AlpineSettings-HotkeyDisabled";

            Button keyboard = GarageTile(
                mod.IsCapturingHeadlightKeyboardBinding ? "KEYBOARD - WAITING" : "KEYBOARD",
                FormatSingleHeadlightBinding(settings.headlightKeyboardKey),
                mod.IsCapturingHeadlightKeyboardBinding,
                () =>
                {
                    if (mod.IsCapturingHeadlightBinding || clearArmed)
                        return;
                    closeDyno?.Invoke();
                    mod.BeginHeadlightKeyboardBind();
                    setStatus?.Invoke("Waiting");
                    render?.Invoke();
                },
                "settings.keyboard", "settings.hotkey", false);
            keyboard.name = "AlpineSettings-HotkeyKeyboard";

            Button controller = GarageTile(
                mod.IsCapturingHeadlightControllerBinding ? "CONTROLLER - WAITING" : "CONTROLLER",
                FormatSingleHeadlightBinding(settings.headlightControllerButton, true),
                mod.IsCapturingHeadlightControllerBinding,
                () =>
                {
                    if (mod.IsCapturingHeadlightBinding || clearArmed)
                        return;
                    closeDyno?.Invoke();
                    mod.BeginHeadlightControllerBind();
                    setStatus?.Invoke("Waiting");
                    render?.Invoke();
                },
                "settings.controller", "settings.hotkey", false);
            controller.name = "AlpineSettings-HotkeyController";

            Button clear;
            Button cancelClear = null;
            if (clearArmed)
            {
                clear = GarageTile(
                    "CONFIRM CLEAR",
                    "Remove both headlight bindings.",
                    true,
                    () =>
                    {
                        setClearBindingArmed?.Invoke(false);
                        mod.CancelHeadlightBindingCapture();
                        setStatus?.Invoke(mod.ClearHeadlightBinding() ? "Bindings cleared" : "Save failed");
                        render?.Invoke();
                    },
                    "settings.confirm-clear", "settings.clear", false);
                cancelClear = GarageTile(
                    "CANCEL",
                    "Keep both bindings.",
                    false,
                    () =>
                    {
                        setClearBindingArmed?.Invoke(false);
                        setStatus?.Invoke("Clear cancelled");
                        render?.Invoke();
                    },
                    "action.continue", "settings.hotkey", false);
                cancelClear.name = "AlpineSettings-CancelClear";
            }
            else
            {
                clear = GarageTile(
                    "CLEAR BINDINGS",
                    "Requires confirmation before removing both bindings.",
                    false,
                    () =>
                    {
                        if (mod.IsCapturingHeadlightBinding)
                            return;
                        closeDyno?.Invoke();
                        setClearBindingArmed?.Invoke(true);
                        setStatus?.Invoke("Confirm clear");
                        render?.Invoke();
                    },
                    "settings.clear", "settings.hotkey", false);
            }
            clear.name = "AlpineSettings-HotkeyClear";

            var choices = new List<Button> { enabledTile, disabledTile, keyboard, controller, clear };
            if (cancelClear != null)
                choices.Add(cancelClear);
            foreach (Button tile in choices)
            {
                bool activeCaptureTile = ReferenceEquals(tile, keyboard) && mod.IsCapturingHeadlightKeyboardBinding ||
                                         ReferenceEquals(tile, controller) && mod.IsCapturingHeadlightControllerBinding;
                tile.SetEnabled(!capturing || activeCaptureTile);
                if (clearArmed)
                {
                    tile.SetEnabled(ReferenceEquals(tile, clear) || ReferenceEquals(tile, cancelClear));
                }
                rail.Add(tile);
                tileButtons.Add(tile);
            }

            if (capturing && captureBadge != null)
            {
                IVisualElementScheduledItem captureWatch = null;
                captureWatch = captureBadge.schedule.Execute(() =>
                {
                    if (captureBadge.panel == null)
                    {
                        captureWatch.Pause();
                        return;
                    }
                    if (mod.IsCapturingHeadlightBinding)
                        return;
                    captureWatch.Pause();
                    HeadlightBindingCaptureResult result = mod.ConsumeHeadlightBindingCaptureResult();
                    switch (result)
                    {
                        case HeadlightBindingCaptureResult.Saved:
                            setStatus?.Invoke("Binding saved");
                            break;
                        case HeadlightBindingCaptureResult.TimedOut:
                            setStatus?.Invoke("Timed out");
                            break;
                        case HeadlightBindingCaptureResult.SaveFailed:
                            setStatus?.Invoke("Save failed");
                            break;
                        case HeadlightBindingCaptureResult.Cancelled:
                            setStatus?.Invoke("Binding cancelled");
                            break;
                    }
                    render?.Invoke();
                }).Every(250);
            }
        }

        private static void BuildGarageLightingControls(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action setupChanged,
            Action render,
            Action<string> setStatus)
        {
            var section = Section("Headlight Mode");
            section.Add(MutedLabel("MODE " + FormatHeadlightMode(working).ToUpperInvariant()));
            section.Add(MutedLabel(mod.HasActiveHeadlightRuntimeBinding()
                ? "RUNTIME ACTIVE"
                : "RUNTIME NEXT RIDE"));
            section.Add(MutedLabel("HOTKEY IN SETTINGS"));

            AddButtonRow(section,
                SmallButton("Force On", () =>
                {
                    working.headlightEnabled = true;
                    setupChanged?.Invoke();
                    setStatus?.Invoke("On staged");
                    render();
                }),
                SmallButton("Force Off", () =>
                {
                    working.headlightEnabled = false;
                    setupChanged?.Invoke();
                    setStatus?.Invoke("Off staged");
                    render();
                }),
                SmallButton("Follow Game Time", () =>
                {
                    working.headlightEnabled = null;
                    setupChanged?.Invoke();
                    setStatus?.Invoke("Auto staged");
                    render();
                }));
            content.Add(section);
        }

        private static bool CanResetGarageNode(GarageNavigationNode node)
        {
            if (node == null)
                return false;
            return node.Kind == NavigationRoot ||
                   node.Kind == NavigationPart ||
                   node.Kind == NavigationCategory;
        }

        private static bool ResetGarageNode(
            AlpineTuningMod mod,
            GarageNavigationNode node,
            TuneProfile working,
            out string message)
        {
            message = "Nothing to reset.";
            if (mod == null || node == null || working == null)
                return false;

            if (node.Kind == NavigationRoot)
            {
                bool changed = false;
                foreach (string category in PartCatalog.OrderedCategories)
                {
                    string stockPartId = mod.Catalog.DefaultPartId(category);
                    if (!string.Equals(
                            working.GetPartId(category),
                            stockPartId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        working.SetPartId(category, stockPartId);
                        changed = true;
                    }
                }

                if (!string.IsNullOrWhiteSpace(working.donorSledKey) ||
                    !string.IsNullOrWhiteSpace(working.donorVehicleId))
                {
                    working.donorSledKey = null;
                    working.donorVehicleId = null;
                    changed = true;
                }

                FineTuneSettings fine = working.fineTune;
                if (fine != null &&
                    (fine.powerTrimPercent != 0f || fine.tractionTrimPercent != 0f ||
                     fine.weightTrimPercent != 0f || fine.clutchTrimPercent != 0f ||
                     fine.centerOfMassYTrim != 0f || fine.centerOfMassZTrim != 0f ||
                     fine.skiStanceTrim != 0f))
                {
                    changed = true;
                }
                working.fineTune = new FineTuneSettings();

                if (working.headlightEnabled.HasValue)
                {
                    working.headlightEnabled = null;
                    changed = true;
                }

                message = changed
                    ? "Complete build returned to factory settings."
                    : "Complete build is already at factory settings.";
                return changed;
            }

            if (node.Kind == NavigationPart)
            {
                bool changed = false;
                if (string.Equals(node.Id, "engine.donor", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(working.donorSledKey) ||
                        !string.IsNullOrWhiteSpace(working.donorVehicleId))
                    {
                        working.donorSledKey = null;
                        working.donorVehicleId = null;
                        changed = true;
                    }

                    changed |= ClearEngineFineTune(working);
                    message = changed
                        ? "Stock engine selected; power and weight trims cleared."
                        : "The stock engine and its adjustments are already selected.";
                    return changed;
                }

                string defaultPartId = mod.Catalog.DefaultPartId(node.Id);
                if (!string.Equals(
                        working.GetPartId(node.Id),
                        defaultPartId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    working.SetPartId(node.Id, defaultPartId);
                    changed = true;
                }

                string adjustmentDescription;
                changed |= ClearFineTuneForPart(working, node.Id, out adjustmentDescription);
                string categoryLabel = mod.Catalog.LabelForCategory(node.Id);
                message = changed
                    ? categoryLabel + " returned to stock" + adjustmentDescription + "."
                    : categoryLabel + " and its adjustments are already stock.";
                return changed;
            }

            if (node.Kind == NavigationCategory)
            {
                bool changed = false;
                foreach (string category in PartCategoriesForGarageSection(node.Id))
                {
                    string defaultPartId = mod.Catalog.DefaultPartId(category);
                    if (string.Equals(
                            working.GetPartId(category),
                            defaultPartId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    working.SetPartId(category, defaultPartId);
                    changed = true;
                }

                switch (node.Id)
                {
                    case "engine":
                        if (!string.IsNullOrWhiteSpace(working.donorSledKey) ||
                            !string.IsNullOrWhiteSpace(working.donorVehicleId))
                        {
                            working.donorSledKey = null;
                            working.donorVehicleId = null;
                            changed = true;
                        }
                        changed |= ClearEngineFineTune(working);
                        message = changed
                            ? "Engine parts, engine swap, power trim, and weight trim returned to stock."
                            : "Engine parts, engine swap, and adjustments are already stock.";
                        break;
                    case "drivetrain":
                        changed |= ClearDrivetrainFineTune(working);
                        message = changed
                            ? "Drivetrain parts and clutch response trim returned to stock."
                            : "Drivetrain parts and adjustment are already stock.";
                        break;
                    case "suspension":
                        changed |= ClearSuspensionFineTune(working);
                        message = changed
                            ? "Suspension parts and balance trims returned to stock."
                            : "Suspension parts and balance adjustments are already stock.";
                        break;
                    case "lighting":
                        if (working.headlightEnabled.HasValue)
                        {
                            working.headlightEnabled = null;
                            changed = true;
                        }
                        message = changed
                            ? "Lighting parts and operating mode returned to stock."
                            : "Lighting parts and operating mode are already stock.";
                        break;
                    default:
                        message = changed
                            ? node.Title + " returned to stock."
                            : node.Title + " is already stock.";
                        break;
                }

                return changed;
            }
            return false;
        }

        private static bool ClearFineTuneForPart(
            TuneProfile working,
            string partCategory,
            out string adjustmentDescription)
        {
            adjustmentDescription = string.Empty;
            if (string.Equals(partCategory, PartCatalog.EngineCore, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(partCategory, PartCatalog.EnginePiston, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(partCategory, PartCatalog.EngineCrank, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(partCategory, PartCatalog.Intake, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(partCategory, PartCatalog.Turbo, StringComparison.OrdinalIgnoreCase))
            {
                adjustmentDescription = "; power and weight trims cleared";
                return ClearEngineFineTune(working);
            }

            if (string.Equals(partCategory, PartCatalog.Clutch, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(partCategory, PartCatalog.ClutchWeights, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(partCategory, PartCatalog.RatioFeel, StringComparison.OrdinalIgnoreCase))
            {
                adjustmentDescription = "; clutch response trim cleared";
                return ClearDrivetrainFineTune(working);
            }

            if (string.Equals(partCategory, PartCatalog.Track, StringComparison.OrdinalIgnoreCase))
            {
                adjustmentDescription = "; traction trim cleared";
                FineTuneSettings fine = working.fineTune;
                if (fine == null || fine.tractionTrimPercent == 0f)
                    return false;
                fine.tractionTrimPercent = 0f;
                return true;
            }

            if (string.Equals(partCategory, PartCatalog.Skis, StringComparison.OrdinalIgnoreCase))
            {
                adjustmentDescription = "; ski stance trim cleared";
                FineTuneSettings fine = working.fineTune;
                if (fine == null || fine.skiStanceTrim == 0f)
                    return false;
                fine.skiStanceTrim = 0f;
                return true;
            }

            if (string.Equals(partCategory, "steeringGeometry", StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.Equals(partCategory, PartCatalog.Suspension, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(partCategory, PartCatalog.Chassis, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(partCategory, PartCatalog.TrackLimiter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(partCategory, PartCatalog.RearShock, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(partCategory, PartCatalog.RearSpring, StringComparison.OrdinalIgnoreCase))
            {
                adjustmentDescription = "; balance trims cleared";
                return ClearSuspensionFineTune(working);
            }

            if (string.Equals(partCategory, PartCatalog.HeadlightColor, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(partCategory, PartCatalog.HeadlightBrightness, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(partCategory, PartCatalog.HeadlightBeam, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(partCategory, PartCatalog.HeadlightAim, StringComparison.OrdinalIgnoreCase))
            {
                adjustmentDescription = "; operating mode returned to Follow Game Time";
                if (!working.headlightEnabled.HasValue)
                    return false;
                working.headlightEnabled = null;
                return true;
            }

            return false;
        }

        private static bool ClearEngineFineTune(TuneProfile working)
        {
            FineTuneSettings fine = working?.fineTune;
            if (fine == null)
                return false;

            bool changed = fine.powerTrimPercent != 0f || fine.weightTrimPercent != 0f;
            fine.powerTrimPercent = 0f;
            fine.weightTrimPercent = 0f;
            return changed;
        }

        private static bool ClearDrivetrainFineTune(TuneProfile working)
        {
            FineTuneSettings fine = working?.fineTune;
            if (fine == null || fine.clutchTrimPercent == 0f)
                return false;

            fine.clutchTrimPercent = 0f;
            return true;
        }

        private static bool ClearSuspensionFineTune(TuneProfile working)
        {
            FineTuneSettings fine = working?.fineTune;
            if (fine == null)
                return false;

            bool changed = fine.centerOfMassYTrim != 0f || fine.centerOfMassZTrim != 0f;
            fine.centerOfMassYTrim = 0f;
            fine.centerOfMassZTrim = 0f;
            return changed;
        }

        private static IEnumerable<string> PartCategoriesForGarageSection(string section)
        {
            switch (section)
            {
                case "engine":
                    return new[] { PartCatalog.EngineCore, PartCatalog.EnginePiston, PartCatalog.EngineCrank, PartCatalog.Intake, PartCatalog.Turbo };
                case "drivetrain":
                    return new[] { PartCatalog.Clutch, PartCatalog.ClutchWeights, PartCatalog.RatioFeel, "brakeCalibration" };
                case "track":
                    return new[] { PartCatalog.Track };
                case "steering":
                    return new[] { PartCatalog.Skis, "steeringGeometry" };
                case "suspension":
                    return new[]
                    {
                        PartCatalog.Suspension,
                        PartCatalog.Chassis,
                        PartCatalog.TrackLimiter,
                        PartCatalog.RearShock,
                        PartCatalog.RearSpring,
                        PartCatalog.Accessories
                    };
                case "lighting":
                    return new[] { PartCatalog.HeadlightColor, PartCatalog.HeadlightBrightness, PartCatalog.HeadlightBeam, PartCatalog.HeadlightAim };
                case "fuel":
                    return new[] { PartCatalog.FuelTank, PartCatalog.BackpackFuel };
                default:
                    return Array.Empty<string>();
            }
        }

        private static string GarageSectionForPartCategory(string category)
        {
            if (string.Equals(category, PartCatalog.EngineCore, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.EnginePiston, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.EngineCrank, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.Intake, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.Turbo, StringComparison.OrdinalIgnoreCase))
                return "engine";
            if (string.Equals(category, PartCatalog.Clutch, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.ClutchWeights, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.RatioFeel, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, "brakeCalibration", StringComparison.OrdinalIgnoreCase))
                return "drivetrain";
            if (string.Equals(category, PartCatalog.Track, StringComparison.OrdinalIgnoreCase))
                return "track";
            if (string.Equals(category, PartCatalog.Skis, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, "steeringGeometry", StringComparison.OrdinalIgnoreCase))
                return "steering";
            if (string.Equals(category, PartCatalog.Suspension, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.Chassis, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.TrackLimiter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.RearShock, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.RearSpring, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.Accessories, StringComparison.OrdinalIgnoreCase))
                return "suspension";
            if (string.Equals(category, PartCatalog.FuelTank, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.BackpackFuel, StringComparison.OrdinalIgnoreCase))
                return "fuel";
            if (string.Equals(category, PartCatalog.HeadlightColor, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.HeadlightBrightness, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.HeadlightBeam, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, PartCatalog.HeadlightAim, StringComparison.OrdinalIgnoreCase))
                return "lighting";
            return string.Empty;
        }

        private static void BuildGaragePresets(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action<TuneProfile> setWorking,
            Action render,
            Action<string> setStatus,
            Action setupChanged,
            Func<bool> saveAsNewSetup,
            Func<bool> hasUnsavedDraft,
            Func<TuneProfile, bool> loadSetupSlot,
            Func<TuneProfile, bool> setDefaultSetupSlot,
            Func<string> getSelectedProfileId,
            Action<string> setSelectedProfileId,
            Func<string> getPendingDeleteProfileId,
            Action<string> setPendingDeleteProfileId,
            Func<string> getPendingLoadProfileId,
            Action<string> setPendingLoadProfileId,
            Func<bool> getFactoryResetArmed,
            Action<bool> setFactoryResetArmed,
            List<Button> tileButtons,
            SUIManagedList tileContent)
        {
            const string recoverySelection = "__recovery__";
            string selectedId = getSelectedProfileId != null ? getSelectedProfileId() : null;
            bool recoveryAvailable = mod.ArchivedProfilesForSled(target).Count > 0 ||
                                     mod.ProfileHistoryForSled(target, 1).Count > 0;
            bool currentDraftSelected = string.IsNullOrWhiteSpace(selectedId);

            Button currentTile = GarageTile(
                "CURRENT DRAFT",
                mod.Store.BuildProfilePartSummary(working),
                currentDraftSelected,
                () =>
                {
                    setSelectedProfileId?.Invoke(null);
                    setPendingDeleteProfileId?.Invoke(null);
                    setPendingLoadProfileId?.Invoke(null);
                    setStatus?.Invoke("Current draft");
                    render?.Invoke();
                },
                "action.current-draft",
                "action.setups");
            currentTile.name = "AlpinePreset-CurrentDraft";
            tileContent.Add(currentTile);
            tileButtons?.Add(currentTile);

            Action addRecoveryTile = () =>
            {
                if (!recoveryAvailable)
                    return;
                Button recoveryTile = GarageTile(
                    "RECOVERY",
                    "Removed setups and earlier saved revisions.",
                    string.Equals(selectedId, recoverySelection, StringComparison.OrdinalIgnoreCase),
                    () =>
                    {
                        setSelectedProfileId?.Invoke(recoverySelection);
                        setPendingDeleteProfileId?.Invoke(null);
                        setPendingLoadProfileId?.Invoke(null);
                        setStatus?.Invoke("Recovery");
                        render?.Invoke();
                    },
                    "action.recovery",
                    "action.setups");
                recoveryTile.name = "AlpinePreset-Recovery";
                tileContent.Add(recoveryTile);
                tileButtons?.Add(recoveryTile);
            };

            bool draftHasChanges = hasUnsavedDraft != null
                ? hasUnsavedDraft()
                : working != null && working.setupEdited;
            string draftName = working != null && working.usesAutomaticName &&
                               (draftHasChanges || string.IsNullOrWhiteSpace(working.name))
                ? mod.Store.BuildAutomaticProfileName(working)
                : (working?.name ?? "Saved Setup");

            List<TuneProfile> profiles = mod.ProfilesForSled(target);
            foreach (TuneProfile profile in profiles)
            {
                TuneProfile captured = profile;
                bool isPreviewSelected = string.Equals(
                    selectedId,
                    captured.profileId,
                    StringComparison.OrdinalIgnoreCase);
                bool isLoaded;
                bool isDefault;
                mod.GetSetupSlotUsage(captured, target, out isLoaded, out isDefault);
                var tileState = new List<string>();
                if (isLoaded)
                    tileState.Add("LOADED");
                if (isDefault)
                    tileState.Add("DEFAULT");
                tileState.Add(mod.Store.BuildProfilePartSummary(captured));
                tileState.Add(FormatUnixTime(captured.updatedUnixTime));
                string subtitle = string.Join("  |  ", tileState.ToArray());
                Button tile = GarageTile(
                    captured.name ?? "(unnamed setup)",
                    subtitle,
                    isPreviewSelected,
                    () =>
                    {
                        setSelectedProfileId?.Invoke(captured.profileId);
                        setPendingDeleteProfileId?.Invoke(null);
                        setPendingLoadProfileId?.Invoke(null);
                        setStatus("Setup preview");
                        render();
                    },
                    "action.setups");
                tile.name = "AlpinePreset-" + SafeElementName(captured.profileId);
                tileContent.Add(tile);
                if (tileButtons != null)
                {
                    if (isPreviewSelected)
                        tileButtons.Insert(0, tile);
                    else
                    tileButtons.Add(tile);
                }
            }

            addRecoveryTile();
            if (currentDraftSelected)
            {
                var currentSection = Section("Current Draft");
                currentSection.Add(CardTitle(draftName));
                currentSection.Add(MutedLabel(mod.Store.BuildProfilePartSummary(working)));
                AddButtonRow(currentSection, SmallButton("Save as New", () =>
                {
                    if (saveAsNewSetup == null || !saveAsNewSetup())
                        return;
                    setSelectedProfileId?.Invoke(working.profileId);
                    setPendingDeleteProfileId?.Invoke(null);
                    setPendingLoadProfileId?.Invoke(null);
                    render?.Invoke();
                }));
                content.Add(currentSection);

                var restoreSection = Section("Reset");
                bool factoryResetArmed = getFactoryResetArmed != null && getFactoryResetArmed();
                restoreSection.Add(MutedLabel("Stage the stock setup."));
                Button resetToStockButton = DangerButton(
                    factoryResetArmed ? "Confirm Reset" : "Reset to Stock",
                    () =>
                    {
                        setPendingDeleteProfileId?.Invoke(null);
                        setPendingLoadProfileId?.Invoke(null);
                        if (!factoryResetArmed)
                        {
                            setFactoryResetArmed?.Invoke(true);
                            setStatus("Confirm stock reset");
                            render();
                            return;
                        }

                        setFactoryResetArmed?.Invoke(false);
                        TuneProfile stock = mod.Catalog.CreateDefaultProfile(target, working.author);
                        stock.profileId = working.profileId;
                        stock.name = working.name;
                        stock.usesAutomaticName = working.usesAutomaticName;
                        stock.setupSlotId = working.setupSlotId;
                        stock.setupSlotName = working.setupSlotName;
                        stock.isCurrentSetup = working.isCurrentSetup;
                        stock.setupEdited = true;
                        setWorking(stock);
                        setStatus("Stock staged");
                        render();
                    });
                // Keep one stable identity across the armed/confirmed labels so focus
                // restoration leaves controller users on the confirmation action.
                resetToStockButton.name = "alpine-button-reset-to-stock";
                AddButtonRow(restoreSection, resetToStockButton);
                content.Add(restoreSection);
                if (profiles.Count == 0)
                    content.Add(MutedLabel(AlpineNativeUiConfig.NoSavedProfilesText));
                return;
            }

            if (string.Equals(selectedId, recoverySelection, StringComparison.OrdinalIgnoreCase))
            {
                BuildGarageRecovery(mod, content, target, setSelectedProfileId, render, setStatus);
                return;
            }

            TuneProfile selectedProfile = profiles.FirstOrDefault(profile =>
                string.Equals(profile.profileId, selectedId, StringComparison.OrdinalIgnoreCase));
            if (selectedProfile == null)
            {
                content.Add(MutedLabel("Choose a setup tile to preview and manage it."));
                return;
            }

            TuneProfile selectedSlot = selectedProfile;
            bool pendingDelete = string.Equals(
                getPendingDeleteProfileId != null ? getPendingDeleteProfileId() : null,
                selectedSlot.profileId,
                StringComparison.OrdinalIgnoreCase);
            bool isCurrent;
            bool isDefaultSetup;
            mod.GetSetupSlotUsage(selectedSlot, target, out isCurrent, out isDefaultSetup);
            string pendingSetupAction = getPendingLoadProfileId != null
                ? getPendingLoadProfileId()
                : null;
            string loadActionKey = "load:" + selectedSlot.profileId;
            string defaultActionKey = "default:" + selectedSlot.profileId;
            bool pendingLoad = string.Equals(
                pendingSetupAction,
                loadActionKey,
                StringComparison.OrdinalIgnoreCase);
            bool pendingDefault = string.Equals(
                pendingSetupAction,
                defaultActionKey,
                StringComparison.OrdinalIgnoreCase);
            var detail = Section("Selected Setup");
            var preview = TuneStore.Clone(selectedSlot);
            mod.PreviewProfile(preview, target);
            detail.Add(CardTitle(selectedSlot.name ?? "Saved Setup"));
            if (isCurrent || isDefaultSetup)
            {
                var stateRow = new VisualElement();
                stateRow.style.flexDirection = FlexDirection.Row;
                stateRow.style.flexWrap = Wrap.Wrap;
                if (isCurrent)
                    stateRow.Add(Badge("LOADED"));
                if (isDefaultSetup)
                    stateRow.Add(Badge("DEFAULT"));
                detail.Add(stateRow);
            }
            detail.Add(MutedLabel(mod.Store.BuildProfilePartSummary(selectedSlot)));
            detail.Add(StatsPreview(mod, target, preview.resolvedStats, preview.requiresReload));
            string loadOverflowWarning;
            bool loadWouldOverflow = mod.TryGetFuelCapacityOverflowWarning(selectedSlot, target, out loadOverflowWarning);
            if (loadWouldOverflow)
            {
                detail.Add(Badge("FUEL OVERFLOW"));
                detail.Add(MutedLabel(loadOverflowWarning));
            }
            detail.Add(MutedLabel("UPDATED " + FormatUnixTime(selectedSlot.updatedUnixTime)));

            var renameField = new TextField("Tune Name") { value = selectedSlot.name ?? string.Empty };
            renameField.name = "AlpinePresetRename-" + SafeElementName(selectedSlot.profileId);
            ApplyControlStyle(renameField);
            Action<bool> commitRename = deferRender =>
            {
                string requestedName = (renameField.value ?? string.Empty).Trim();
                if (string.Equals(requestedName, selectedSlot.name, StringComparison.Ordinal))
                    return;
                string message;
                if (mod.RenameSetupSlot(selectedSlot, target, requestedName, out message))
                {
                    selectedSlot.name = requestedName;
                    selectedSlot.usesAutomaticName = false;
                    if (working != null &&
                        string.Equals(
                            working.setupSlotId,
                            selectedSlot.profileId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        // RenameSetupSlot persists the slot immediately. Keep the
                        // staged clone synchronized so its next Save cannot write
                        // the previous name back over that accepted rename.
                        working.name = requestedName;
                        working.setupSlotName = requestedName;
                        working.usesAutomaticName = false;
                    }
                    setPendingLoadProfileId?.Invoke(null);
                    setStatus(message);
                    if (deferRender)
                        renameField.schedule.Execute(() => render());
                    else
                        render();
                }
                else if (!string.IsNullOrWhiteSpace(message))
                {
                    setStatus(message);
                }
            };
            renameField.RegisterCallback<FocusOutEvent>(_ => commitRename(true));
            renameField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                    return;
                commitRename(false);
                evt.StopPropagation();
            });
            detail.Add(renameField);

            bool loadWouldDiscardDraft = draftHasChanges;
            bool loadNeedsConfirmation = loadWouldDiscardDraft || loadWouldOverflow;
            bool alreadyLoaded = isCurrent && !loadNeedsConfirmation;
            string loadLabel = alreadyLoaded
                ? "Loaded"
                : pendingLoad ? "Confirm Load" : "Load";
            Button equipButton = PrimaryButton(loadLabel, () =>
            {
                setPendingDeleteProfileId?.Invoke(null);
                if (loadNeedsConfirmation && !pendingLoad)
                {
                    setPendingLoadProfileId?.Invoke(loadActionKey);
                    setStatus(loadWouldOverflow
                        ? "Confirm load · fuel overflow will be lost"
                        : "Load again to discard draft");
                    render();
                    return;
                }

                setPendingLoadProfileId?.Invoke(null);
                if (loadSetupSlot != null && loadSetupSlot(selectedSlot))
                    setSelectedProfileId?.Invoke(selectedSlot.profileId);
                render();
            });
            equipButton.name = "AlpinePresetLoad-" + SafeElementName(selectedSlot.profileId);
            equipButton.SetEnabled(!alreadyLoaded);

            bool alreadyDefault = isCurrent && isDefaultSetup && !loadNeedsConfirmation;
            string defaultLabel = alreadyDefault
                ? "Default"
                : pendingDefault ? "Confirm Default" : isDefaultSetup ? "Load Default" : "Set Default";
            Button defaultButton = SmallButton(defaultLabel, () =>
            {
                setPendingDeleteProfileId?.Invoke(null);
                if (loadNeedsConfirmation && !pendingDefault)
                {
                    setPendingLoadProfileId?.Invoke(defaultActionKey);
                    setStatus(loadWouldOverflow
                        ? "Confirm default · fuel overflow will be lost"
                        : "Set again to discard draft");
                    render();
                    return;
                }

                setPendingLoadProfileId?.Invoke(null);
                if (setDefaultSetupSlot != null && setDefaultSetupSlot(selectedSlot))
                    setSelectedProfileId?.Invoke(selectedSlot.profileId);
                render();
            });
            defaultButton.name = "AlpinePresetDefault-" + SafeElementName(selectedSlot.profileId);
            defaultButton.SetEnabled(!alreadyDefault);

            Button duplicateButton = SmallButton("Duplicate", () =>
            {
                setPendingDeleteProfileId?.Invoke(null);
                setPendingLoadProfileId?.Invoke(null);
                string message;
                TuneProfile duplicate = mod.DuplicateSetupSlot(selectedSlot, target, out message);
                if (duplicate != null)
                    setSelectedProfileId?.Invoke(duplicate.profileId);
                setStatus(string.IsNullOrWhiteSpace(message) ? "Setup duplicated." : message);
                render();
            });
            duplicateButton.name = "AlpinePresetDuplicate-" + SafeElementName(selectedSlot.profileId);
            bool canRemove = !isCurrent && !isDefaultSetup;
            string removeLabel = !canRemove
                ? isCurrent ? "Loaded" : "Default"
                : pendingDelete ? "Confirm Remove" : "Remove";
            Button deleteButton = DangerButton(removeLabel, () =>
            {
                setPendingLoadProfileId?.Invoke(null);
                if (!pendingDelete)
                {
                    setPendingDeleteProfileId?.Invoke(selectedSlot.profileId);
                    setStatus("Confirm remove");
                    render();
                    return;
                }

                if (!mod.DeleteProfile(selectedSlot.profileId))
                {
                    setStatus("In use or remove failed");
                    return;
                }
                setSelectedProfileId?.Invoke(null);
                setPendingDeleteProfileId?.Invoke(null);
                setStatus("Moved to recovery");
                render();
            });
            deleteButton.name = "AlpinePresetRemove-" + SafeElementName(selectedSlot.profileId);
            deleteButton.SetEnabled(canRemove);
            AddSplitButtonRow(
                detail,
                new[] { equipButton, defaultButton },
                new[] { duplicateButton, deleteButton });
            content.Add(detail);
        }

        private static void BuildGarageRecovery(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            Action<string> setSelectedProfileId,
            Action render,
            Action<string> setStatus)
        {
            List<TuneProfile> archived = mod.ArchivedProfilesForSled(target);
            List<TuneHistoryEntry> history = mod.ProfileHistoryForSled(target, 20);
            if (archived.Count == 0 && history.Count == 0)
                return;

            var recovery = Section("Recovery");
            if (archived.Count > 0)
                recovery.Add(MutedLabel("REMOVED SETUPS"));
            foreach (TuneProfile removed in archived
                         .GroupBy(profile => profile.profileId, StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.First()))
            {
                TuneProfile capturedRemoved = removed;
                recovery.Add(MutedLabel(
                    (capturedRemoved.name ?? "Setup") + "  |  " +
                    mod.Store.BuildProfilePartSummary(capturedRemoved) + "  |  " +
                    FormatUnixTime(capturedRemoved.updatedUnixTime)));
                Button restoreButton = SmallButton("Restore", () =>
                {
                    TuneProfile restored;
                    string message;
                    if (mod.RestoreArchivedSetup(capturedRemoved.profileId, out restored, out message) && restored != null)
                        setSelectedProfileId?.Invoke(restored.profileId);
                    setStatus(string.IsNullOrWhiteSpace(message) ? "Restore failed" : message);
                    render();
                });
                restoreButton.name = "AlpineRecoveryRemoved-" + SafeElementName(capturedRemoved.profileId);
                AddButtonRow(recovery, restoreButton);
            }

            if (history.Count > 0)
                recovery.Add(MutedLabel("EARLIER VERSIONS"));
            foreach (TuneHistoryEntry entry in history.Where(item => item != null && item.profile != null))
            {
                TuneHistoryEntry capturedEntry = entry;
                recovery.Add(MutedLabel(
                    (capturedEntry.profile.name ?? "Setup") + "  |  " +
                    mod.Store.BuildProfilePartSummary(capturedEntry.profile) + "  |  " +
                    FormatUnixTime(capturedEntry.archivedUnixTime)));
                Button restoreHistoryButton = SmallButton("Restore as New", () =>
                {
                    TuneProfile restored;
                    string message;
                    if (mod.RestoreProfileHistory(capturedEntry, out restored, out message) && restored != null)
                        setSelectedProfileId?.Invoke(restored.profileId);
                    setStatus(string.IsNullOrWhiteSpace(message) ? "History restore failed" : message);
                    render();
                });
                restoreHistoryButton.name = "AlpineRecoveryHistory-" + SafeElementName(capturedEntry.historyId);
                AddButtonRow(recovery, restoreHistoryButton);
            }
            content.Add(recovery);
        }

        private static void AddSlider(
            VisualElement content,
            string label,
            float min,
            float max,
            float value,
            string valueFormat,
            string suffix,
            Action<float> changed,
            Action setupChanged = null,
            string tooltip = null,
            Func<float, string> formatValue = null)
        {
            string ValueText(float sliderValue)
            {
                if (formatValue != null)
                    return formatValue(sliderValue);

                return $"{sliderValue.ToString(valueFormat)}{suffix}";
            }

            var slider = new Slider(label, min, max)
            {
                name = "alpine-control-" + SafeElementName(label),
                value = Mathf.Clamp(value, min, max)
            };

            slider.label = $"{label}: {ValueText(slider.value)}";
            ApplyControlStyle(slider);
            ApplyInlineSliderLabel(slider, slider.label);
            SetTooltip(slider, tooltip);

            slider.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp(evt.newValue, min, max);
                changed(clamped);
                slider.label = $"{label}: {ValueText(clamped)}";
                ApplyInlineSliderLabel(slider, slider.label);
                setupChanged?.Invoke();
            });

            content.Add(slider);
        }

        private static void SetTooltip(VisualElement element, string text)
        {
            if (element == null)
                return;

            element.tooltip = string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : text.Trim();
        }

        private static string FormatPercentDelta(float value, float baseline)
        {
            if (Mathf.Abs(baseline) <= 0.001f)
                return "Stock";

            float delta = (value / baseline - 1f) * 100f;
            if (Mathf.Abs(delta) < 0.5f)
                return "Stock";

            return delta.ToString("+0;-0;0") + "%";
        }

        private static bool HasConfiguredHeadlightBinding(AlpineUserSettings settings)
        {
            return settings != null &&
                   (!string.IsNullOrWhiteSpace(settings.headlightKeyboardKey) ||
                    !string.IsNullOrWhiteSpace(settings.headlightControllerButton));
        }

        private static string FormatSingleHeadlightBinding(string value, bool controller = false)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Not set";
            if (!controller)
                return value;

            int marker = value.LastIndexOf("Button", StringComparison.OrdinalIgnoreCase);
            if (!value.StartsWith("Joystick", StringComparison.OrdinalIgnoreCase) || marker < 0)
                return value;

            string suffix = value.Substring(marker + "Button".Length);
            switch (suffix)
            {
                case "0": return "A / Cross";
                case "1": return "B / Circle";
                case "2": return "X / Square";
                case "3": return "Y / Triangle";
                case "4": return "Left Bumper";
                case "5": return "Right Bumper";
                case "6": return "View / Share";
                case "7": return "Menu / Options";
                case "8": return "Left Stick";
                case "9": return "Right Stick";
                default: return "Controller " + suffix;
            }
        }

        private static string FormatHeadlightMode(TuneProfile profile)
        {
            if (profile == null || !profile.headlightEnabled.HasValue)
                return "Follow Game Time";

            return profile.headlightEnabled.Value ? "Force On" : "Force Off";
        }

        private static string SafeElementName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "control";

            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c))
                    chars[i] = '-';
            }

            return new string(chars).Trim('-').ToLowerInvariant();
        }

        private static void SetGarageStatus(Label status, string message)
        {
            if (status == null)
                return;
            string full = (message ?? string.Empty).Trim();
            string compact = full;
            string normalized = full.ToLowerInvariant();
            if (normalized.Contains("binding cancel"))
                compact = "Binding cancelled";
            else if (normalized.Contains("confirm clear"))
                compact = "Confirm clear";
            else if (normalized.Contains("binding") && normalized.Contains("saved"))
                compact = "Binding saved";
            else if (normalized.Contains("waiting") || normalized.Contains("press a key") ||
                     normalized.Contains("press a button") || normalized.Contains("press input"))
                compact = "Waiting";
            else if ((normalized.Contains("failed") || normalized.Contains("could not")) &&
                     (normalized.Contains("saved") || normalized.Contains("updated")))
                compact = "Partial";
            else if (normalized.Contains("staged") || normalized.Contains("updated") ||
                     normalized.Contains("adjusted"))
                compact = "Staged";
            else if (normalized.Contains("saved") || normalized == "save")
                compact = "Saved";
            else if (normalized.Contains("loaded"))
                compact = "Loaded";
            else if (normalized.Contains("unavailable") || normalized.Contains("not found"))
                compact = "Unavailable";
            else if (normalized.Contains("failed") || normalized.Contains("could not"))
                compact = "Failed";
            else if (normalized.Contains("next ride"))
                compact = "Next ride";
            else if (full.Length > 20)
                compact = full.Substring(0, 17).TrimEnd() + "...";

            status.text = compact;
            status.tooltip = full;
            status.style.display = string.IsNullOrWhiteSpace(compact)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        private static void ApplyInlineSliderLabel(Slider slider, string text)
        {
            if (slider == null || slider.labelElement == null)
                return;
            Label label = slider.labelElement;
            label.text = text ?? string.Empty;
            label.pickingMode = PickingMode.Ignore;
            label.style.position = Position.Absolute;
            label.style.left = 8f;
            label.style.right = 8f;
            label.style.top = 0f;
            label.style.bottom = 0f;
            label.style.width = StyleKeyword.Auto;
            label.style.minWidth = 0f;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.color = AlpineNativeUiConfig.RowTextColor;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.BringToFront();

            slider.style.position = Position.Relative;
            slider.style.alignSelf = Align.Stretch;
            slider.style.width = Length.Percent(100f);
            slider.style.maxWidth = Length.Percent(100f);
            slider.style.minWidth = 0f;
            slider.style.height = 32f;
            VisualElement input = slider.Q<VisualElement>(className: "unity-base-field__input");
            if (input != null)
            {
                input.style.flexGrow = 1f;
                input.style.width = Length.Percent(100f);
                input.style.maxWidth = Length.Percent(100f);
                input.style.minWidth = 0f;
            }
        }

        private static VisualElement Section(string title)
        {
            var section = new VisualElement();
            section.style.flexDirection = FlexDirection.Column;
            section.style.alignSelf = Align.Stretch;
            section.style.width = Length.Percent(100f);
            section.style.maxWidth = Length.Percent(100f);
            section.style.minWidth = 0f;
            section.style.marginTop = AlpineNativeUiConfig.SectionGap;
            section.Add(SectionTitle(title));
            return section;
        }

        private static TuneProfile PreviewClone(
            AlpineTuningMod mod,
            VehicleScriptableObject target,
            TuneProfile source)
        {
            TuneProfile preview = TuneStore.Clone(source);
            if (preview == null || mod == null || target == null)
                return preview;
            try
            {
                mod.PreviewProfile(preview, target);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Garage comparison preview skipped: {ex.GetType().Name}");
            }
            return preview;
        }

        private static VisualElement StatsPreview(AlpineTuningMod mod, VehicleScriptableObject sled, ResolvedStats stats, bool requiresReload)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignSelf = Align.Stretch;
            row.style.width = Length.Percent(100f);
            row.style.maxWidth = Length.Percent(100f);
            row.style.minWidth = 0f;
            row.style.marginBottom = AlpineNativeUiConfig.SectionGap;

            if (stats != null)
            {
                var settings = mod != null ? mod.Settings : new AlpineUserSettings();
                var defaults = mod != null && sled != null
                    ? mod.Store.GetDefaults(AlpineTuningMod.GetSledKey(sled), AlpineTuningMod.GetVehicleId(sled))
                    : null;
                AddStatChip(row, "Engine Output", UnitConversion.FormatPower(stats.horsePower, settings.units));
                AddStatChip(row, "Paddle", TrackSpecResolver.FormatPaddleHeight(stats.lugHeight));
                AddStatChip(row, "Track Bite", FormatPercentDelta(stats.friction, defaults != null ? defaults.friction : stats.friction));
                AddStatChip(row, "Weight", UnitConversion.FormatWeight(stats.weight, settings.units));
                AddStatChip(row, "Tank", stats.fuelCapacity.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " L");
                AddStatChip(row, "Consumption", stats.fuelConsumption.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " L/100 km");
                if (stats.backpackFuelCapacityLiters > 0.001f)
                    AddStatChip(row, "Backpack Reserve", stats.backpackFuelCapacityLiters.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " L");
                AddStatChip(row, "Ski Stance", settings.units == AlpineDisplayUnits.Imperial
                    ? UnitConversion.MillimetersToInches(stats.skiStance).ToString("F1") + " in"
                    : stats.skiStance.ToString("F0") + " mm");
            }

            if (requiresReload)
                AddStatusChip(row, AlpineNativeUiConfig.ReloadRequiredHintText);

            return row;
        }

        private static void AddStatChip(VisualElement row, string label, string value)
        {
            if (row == null)
                return;

            row.Add(Chip($"{label} {value}", false));
        }

        private static void AddStatusChip(VisualElement row, string text)
        {
            if (row == null)
                return;

            var chip = Chip(text, true);
            if (string.Equals(text, AlpineNativeUiConfig.ReloadRequiredHintText, StringComparison.OrdinalIgnoreCase))
            {
                SetTooltip(
                    chip,
                    "This sled is not currently spawned or needs a rebuild. Your setup will equip automatically when you ride it.");
            }
            row.Add(chip);
        }

        private static Label Chip(string text, bool status)
        {
            var chip = new Label(text ?? string.Empty);
            chip.style.color = status
                ? AlpineNativeUiConfig.ActiveButtonTextColor
                : AlpineNativeUiConfig.RowTextColor;
            chip.style.backgroundColor = status
                ? AlpineNativeUiConfig.AccentColor
                : AlpineNativeUiConfig.ChipBackgroundColor;
            chip.style.paddingLeft = AlpineNativeUiConfig.StatChipPaddingHorizontal;
            chip.style.paddingRight = AlpineNativeUiConfig.StatChipPaddingHorizontal;
            chip.style.paddingTop = AlpineNativeUiConfig.StatChipPaddingVertical;
            chip.style.paddingBottom = AlpineNativeUiConfig.StatChipPaddingVertical;
            chip.style.marginRight = AlpineNativeUiConfig.InlineGap;
            chip.style.marginTop = AlpineNativeUiConfig.RowGap;
            chip.style.flexShrink = 1;
            ApplyTextWrap(chip);
            return chip;
        }

        private static Label Badge(string text)
        {
            var badge = Chip(text, true);
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            return badge;
        }

        private static Label SectionTitle(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.style.color = AlpineNativeUiConfig.TitleTextColor;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = AlpineNativeUiConfig.RowGap;
            label.style.fontSize = Mathf.Max(12f, AlpineNativeUiConfig.DefaultTitleFontSize - 3f);
            label.style.alignSelf = Align.Stretch;
            label.style.width = Length.Percent(100f);
            label.style.maxWidth = Length.Percent(100f);
            label.style.minWidth = 0f;
            label.style.overflow = Overflow.Hidden;
            ApplyTextWrap(label);
            return label;
        }

        private static Label CardTitle(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.style.color = AlpineNativeUiConfig.TitleTextColor;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = Mathf.Max(12f, AlpineNativeUiConfig.DefaultTitleFontSize - 2f);
            label.style.marginRight = AlpineNativeUiConfig.InlineGap;
            label.style.flexShrink = 1;
            label.style.alignSelf = Align.Stretch;
            label.style.width = Length.Percent(100f);
            label.style.maxWidth = Length.Percent(100f);
            label.style.minWidth = 0f;
            label.style.overflow = Overflow.Hidden;
            ApplyTextWrap(label);
            return label;
        }

        private static string FormatUnixTime(long unixTime)
        {
            if (unixTime <= 0)
                return "unknown";

            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixTime)
                    .ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return "unknown";
            }
        }

        private static void AddButtonRow(VisualElement content, params Button[] buttons)
        {
            var row = new VisualElement();
            ApplyButtonRowStyle(row);

            foreach (var button in buttons)
            {
                if (button != null)
                    row.Add(button);
            }

            content.Add(row);
        }

        private static void AddSplitButtonRow(VisualElement content, Button[] leftButtons, Button[] rightButtons)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginTop = AlpineNativeUiConfig.DefaultButtonRowMarginTop;
            row.style.minWidth = 0;
            row.style.flexShrink = 1;

            var left = new VisualElement();
            left.style.flexDirection = FlexDirection.Row;
            left.style.flexWrap = Wrap.Wrap;
            left.style.flexGrow = 1;
            left.style.flexShrink = 1;
            left.style.minWidth = 0;

            var right = new VisualElement();
            right.style.flexDirection = FlexDirection.Row;
            right.style.flexWrap = Wrap.Wrap;
            right.style.justifyContent = Justify.FlexEnd;
            right.style.marginTop = AlpineNativeUiConfig.RowGap;
            right.style.marginLeft = 0;
            right.style.flexShrink = 1;
            right.style.minWidth = 0;

            if (leftButtons != null)
            {
                foreach (var button in leftButtons)
                {
                    if (button != null)
                        left.Add(button);
                }
            }

            if (rightButtons != null)
            {
                foreach (var button in rightButtons)
                {
                    if (button != null)
                        right.Add(button);
                }
            }

            row.Add(left);
            row.Add(right);
            content.Add(row);
        }

        private static Button SmallButton(string text, Action clicked)
        {
            var button = new Button(clicked)
            {
                name = "alpine-button-" + SafeElementName(text),
                text = text
            };

            ApplyButtonStyle(button);
            return button;
        }

        private static Button PrimaryButton(string text, Action clicked)
        {
            var button = SmallButton(text, clicked);
            button.style.backgroundColor = AlpineNativeUiConfig.AccentColor;
            button.style.color = AlpineNativeUiConfig.ActiveButtonTextColor;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            return button;
        }

        private static Button DangerButton(string text, Action clicked)
        {
            var button = SmallButton(text, clicked);
            button.style.backgroundColor = AlpineNativeUiConfig.DangerButtonColor;
            button.style.color = AlpineNativeUiConfig.DangerTextColor;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            return button;
        }

        private static Label MutedLabel(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.style.color = AlpineNativeUiConfig.MutedTextColor;
            label.style.marginTop = AlpineNativeUiConfig.DefaultMutedLabelMarginTop;
            label.style.flexShrink = 1;
            label.style.alignSelf = Align.Stretch;
            label.style.width = Length.Percent(100f);
            label.style.maxWidth = Length.Percent(100f);
            label.style.minWidth = 0f;
            label.style.overflow = Overflow.Hidden;
            ApplyTextWrap(label);
            return label;
        }

        private static void ApplyControlStyle(VisualElement control)
        {
            if (control == null)
                return;

            control.style.marginTop = AlpineNativeUiConfig.RowGap;
            control.style.flexGrow = 1;
            control.style.flexShrink = 1;
            control.style.minWidth = 0;
            control.style.alignSelf = Align.Stretch;
            control.style.width = Length.Percent(100f);
            control.style.maxWidth = Length.Percent(100f);
        }

        private static void ApplyButtonRowStyle(VisualElement row)
        {
            if (row == null)
                return;

            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = AlpineNativeUiConfig.DefaultButtonRowMarginTop;
            row.style.flexShrink = 1;
            row.style.minWidth = 0;
        }

        private static void ApplyButtonStyle(Button button)
        {
            if (button == null)
                return;

            button.style.marginRight = AlpineNativeUiConfig.DefaultButtonMarginRight;
            button.style.marginTop = AlpineNativeUiConfig.DefaultButtonMarginTop;
            button.style.marginBottom = AlpineNativeUiConfig.DefaultButtonMarginBottom;
            button.style.height = AlpineNativeUiConfig.DefaultButtonHeight;
            button.style.minWidth = 72f;
            button.style.flexShrink = 1;
            button.style.backgroundColor = AlpineNativeUiConfig.ButtonBackgroundColor;
            button.style.color = AlpineNativeUiConfig.RowTextColor;
        }

        private static void ApplyTextWrap(Label label)
        {
            if (label == null)
                return;

            label.style.whiteSpace = WhiteSpace.Normal;
        }

        private static VisualElement FindVisualRoot(object controller)
        {
            return SleddersGameBindings.FindVisualRoot(controller);
        }

        private static T FirstDescendant<T>(VisualElement root) where T : VisualElement
        {
            if (root == null)
                return null;

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root[i];

                if (child is T typed)
                    return typed;

                var nested = FirstDescendant<T>(child);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static void CollectDescendants<T>(VisualElement root, List<T> results)
            where T : VisualElement
        {
            if (root == null || results == null)
                return;

            for (int i = 0; i < root.childCount; i++)
            {
                VisualElement child = root[i];
                if (child is T typed)
                    results.Add(typed);
                CollectDescendants(child, results);
            }
        }

        private static void CopyClasses(VisualElement source, VisualElement target)
        {
            if (source == null || target == null)
                return;

            foreach (string className in source.GetClasses())
                target.AddToClassList(className);
        }

    }
}
