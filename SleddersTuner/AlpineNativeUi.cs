using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlpineTuning
{
    internal static class AlpineNativeUiConfig
    {
        // Element names / IDs.
        public const string RootName = "alpine-tuning-root";
        public const string PanelName = "alpine-tuning-panel";
        public const string GarageTabName = "Tab_AlpineTuning";
        public const string GarageTabButtonName = "AlpineTuningTabButton";
        public const string PauseButtonName = "AlpineTuningPauseButton";

        // Button / tab labels.
        public const string ModTitle = "SLED SETUP";
        public const string BuildTabLabel = "Setup";
        public const string TrackTabLabel = "Track";
        public const string EngineTabLabel = "Engine";
        public const string ClutchTabLabel = "Clutching";
        public const string SetupTabLabel = "Suspension";
        public const string LightsTabLabel = "Lighting";
        public const string PerformanceTabLabel = "Performance";
        public const string FineTuneTabLabel = "Adjustment";
        public const string LibraryTabLabel = "Setup Slots";
        public const string ShareTabLabel = "Multiplayer";
        public const string GuideTabLabel = "Guide";
        public const string UiSettingsTabLabel = "Settings";
        public const string RefreshSledLabel = "Find Sled";

        // Feature switches.
        public const bool EnableRuntimeUiSettingsTab = true;
        public static readonly bool ShowRefreshSledButton = true;
        public const bool ShowNativeAccessoriesCategory = false;
        public const bool EnablePeerReplicationToggle = true;

        // Reflection field/method names used by the native game UI.
        // These are intentionally centralized because obfuscated game updates may change them.
        public const string VehicleRootFieldName = "NPAACPBJNOL";
        public const string VehicleNativeTabManagerFieldName = "CDOJAEOEMDH";
        public const string NativeTabPanelsFieldName = "FLNOHFIPDDN";
        public const string NativeTabButtonsFieldName = "PJBNPIEGJFB";
        public const string NativeTabCallbacksFieldName = "BFLLJAMNBEK";
        public const string NativeSelectTabMethodName = "PJFAFBFMOIK";

        // Native UI lookup names.
        public const string GarageTabsButtonsName = "TabsButtons";
        public const string GarageTabsName = "Tabs";
        public const string PauseSelectVehicleButtonName = "SelectVehicle";
        public const string PauseOptionsButtonName = "Options";

        // Default UI layout values.
        public const float DefaultPanelMaxWidth = 1180f;
        public const float DefaultPanelMaxHeight = 360f;
        public const float DefaultInlineSurfaceMaxHeight = 340f;
        public const float DefaultInlinePanelMaxHeight = 240f;
        public const float DefaultPanelMinWidth = 520f;
        public const float DefaultPanelWidthPercent = 68f;
        public const float DefaultRootMarginTop = 6f;
        public const float DefaultRootMarginBottom = 8f;
        public const float DefaultRootMarginLeft = 4f;
        public const float DefaultRootMarginRight = 4f;
        public const float DefaultPanelPadding = 12f;
        public const float DefaultPanelMarginTop = 4f;
        public const float DefaultTabsMarginBottom = 8f;
        public const float DefaultStatusMarginTop = 6f;
        public const float DefaultButtonHeight = 28f;
        public const float DefaultButtonMarginRight = 4f;
        public const float DefaultButtonMarginTop = 2f;
        public const float DefaultButtonMarginBottom = 2f;
        public const float DefaultControlMarginTop = 6f;
        public const float DefaultMutedLabelMarginTop = 2f;
        public const float DefaultRowMarginTop = 4f;
        public const float DefaultButtonRowMarginTop = 8f;
        public const float DefaultTitleFontSize = 17f;
        public const float SectionGap = 12f;
        public const float RowGap = 6f;
        public const float InlineGap = 6f;
        public const float CardPadding = 8f;
        public const float CardGap = 8f;
        public const float FooterGap = 10f;
        public const float StatChipPaddingHorizontal = 6f;
        public const float StatChipPaddingVertical = 3f;

        // Runtime UI settings slider limits.
        public const float RuntimePanelWidthMin = 420f;
        public const float RuntimePanelWidthMax = 1400f;
        public const float RuntimePanelHeightMin = 200f;
        public const float RuntimePanelHeightMax = 1000f;
        public const float RuntimePaddingMin = 2f;
        public const float RuntimePaddingMax = 28f;
        public const float RuntimeButtonHeightMin = 20f;
        public const float RuntimeButtonHeightMax = 44f;
        public const float RuntimeFontSizeMin = 10f;
        public const float RuntimeFontSizeMax = 24f;
        public const float RuntimeOpacityMin = 0.35f;
        public const float RuntimeOpacityMax = 1f;

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
        public static readonly Color ButtonHoverColor = new Color(0.22f, 0.25f, 0.27f, 0.94f);
        public static readonly Color ActiveButtonBackgroundColor = new Color(0.78f, 0.92f, 0.08f, 0.95f);
        public static readonly Color ActiveButtonTextColor = new Color(0.04f, 0.05f, 0.05f, 1f);
        public static readonly Color CardBackgroundColor = new Color(0.10f, 0.12f, 0.14f, 0.72f);
        public static readonly Color SelectedCardBackgroundColor = new Color(0.14f, 0.17f, 0.19f, 0.88f);
        public static readonly Color ChipBackgroundColor = new Color(0.18f, 0.21f, 0.23f, 0.88f);
        public static readonly Color DangerButtonColor = new Color(0.42f, 0.14f, 0.12f, 0.92f);
        public static readonly Color DangerTextColor = new Color(1f, 0.82f, 0.78f, 1f);

        // Text.
        public const string NoSavedProfilesText = "No setup slots for this sled yet.";
        public const string NoSharedTunesText = "Networked setup sharing is paused for this build.";
        public const string FineTuneHelpText = "Adjustments are clamped to keep setups predictable.";
        public const string ReloadRequiredHintText = "Ready for next ride";
        public const string RefreshedSledText = "Selected sled found.";
        public const string PreviewUpdatedText = "Setup updated.";
        public const string FactoryDefaultsRestoredText = "Returned to stock.";
        public const string ActiveProfileSavedText = "Setup saved.";
        public const string AppliedSavedActiveText = "Setup saved.";
        public const string AppliedSavedReloadedText = "Setup saved.";
        public const string InstalledBuildText = "Installed.";
        public const string InstalledRebuiltBuildText = "Ready for next ride.";
        public const string FineTuneAppliedText = "Adjusted.";
        public const string PublishedTuneText = "Shared setup summary sent to discovered lobby peers.";
        public const string PeerHelloText = "Sent peer discovery hello.";
        public const string PeerReplicationUnavailableText = "Replicate Peers Coming Soon!";
        public const string SharedPayloadMissingText = "Shared payload not available.";
        public const string ApplyFailedText = "Setup update failed.";
        public const string SaveFailedText = "Setup save failed.";
        public const string ResetFailedText = "Could not return to stock.";
        public const string SharingUnavailableText = AlpineConstants.PeerSharingPausedNotice;
    }

    internal sealed class AlpineNativeUiRuntimeSettings
    {
        public float PanelMaxWidth = AlpineNativeUiConfig.DefaultPanelMaxWidth;
        public float PanelMaxHeight = AlpineNativeUiConfig.DefaultPanelMaxHeight;
        public float PanelPadding = AlpineNativeUiConfig.DefaultPanelPadding;
        public float ButtonHeight = AlpineNativeUiConfig.DefaultButtonHeight;
        public float TitleFontSize = AlpineNativeUiConfig.DefaultTitleFontSize;
        public float PanelOpacity = AlpineNativeUiConfig.PanelBackgroundColor.a;

        public void ResetToDefaults()
        {
            PanelMaxWidth = AlpineNativeUiConfig.DefaultPanelMaxWidth;
            PanelMaxHeight = AlpineNativeUiConfig.DefaultPanelMaxHeight;
            PanelPadding = AlpineNativeUiConfig.DefaultPanelPadding;
            ButtonHeight = AlpineNativeUiConfig.DefaultButtonHeight;
            TitleFontSize = AlpineNativeUiConfig.DefaultTitleFontSize;
            PanelOpacity = AlpineNativeUiConfig.PanelBackgroundColor.a;
        }
    }

    internal static class AlpineNativeUi
    {
        private enum AlpineUiSurfaceMode
        {
            GarageTab,
            PauseInline,
            FallbackInline
        }

        private static readonly AlpineNativeUiRuntimeSettings RuntimeUi = new AlpineNativeUiRuntimeSettings();
        private static readonly Dictionary<int, Action> GarageRenderActions = new Dictionary<int, Action>();
        private static int _attachedMenuCount;
        private static float _lastUiRefreshLogTime;

        public static bool HasAttachedMenus => HasAttachedNativeUiRoot();

        public static void NotifyGarageSelectionChanged(VehicleSelectionUiController controller)
        {
            if (controller == null)
                return;

            if (!GarageRenderActions.TryGetValue(controller.GetInstanceID(), out var render) || render == null)
                return;

            try
            {
                render();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Garage setup refresh skipped: {ex.Message}");
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

                foreach (var pauseMenu in Resources.FindObjectsOfTypeAll<PauseUIController>())
                    attached |= AttachToPause(mod, pauseMenu);

                return attached;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Native UI scan skipped: {ex.Message}");
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
                    if (root != null && root.Q<VisualElement>(AlpineNativeUiConfig.RootName) != null)
                        return true;
                }

                foreach (var pauseMenu in Resources.FindObjectsOfTypeAll<PauseUIController>())
                {
                    VisualElement root = FindVisualRoot(pauseMenu);
                    if (root != null && root.Q<VisualElement>(AlpineNativeUiConfig.RootName) != null)
                        return true;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Native UI attachment check skipped: {ex.Message}");
            }

            return false;
        }

        public static bool AttachToVehicleSelection(AlpineTuningMod mod, VehicleSelectionUiController controller)
        {
            return AttachToGarage(mod, controller);
        }

        public static bool AttachToPause(AlpineTuningMod mod, PauseUIController controller)
        {
            return AttachToPauseMenu(mod, controller);
        }

        private static bool AttachToGarage(AlpineTuningMod mod, VehicleSelectionUiController controller)
        {
            if (mod == null || controller == null)
                return false;

            VisualElement menuRoot = FindVisualRoot(controller);
            if (menuRoot == null || menuRoot.Q<VisualElement>(AlpineNativeUiConfig.RootName) != null)
                return false;

            object nativeTabManager = SleddersGameBindings.GetFieldValue<object>(
                controller,
                AlpineNativeUiConfig.VehicleNativeTabManagerFieldName);

            VisualElement tabsButtons;
            VisualElement tabs;
            if (!TryFindNativeTabContainers(nativeTabManager, out tabsButtons, out tabs))
            {
                tabsButtons = menuRoot.Q<VisualElement>(AlpineNativeUiConfig.GarageTabsButtonsName);
                tabs = menuRoot.Q<VisualElement>(AlpineNativeUiConfig.GarageTabsName);
            }

            if (tabsButtons == null || tabs == null)
            {
                return AttachInlineFallback(mod, controller, "Garage", menuRoot);
            }

            Action render;
            VisualElement surface = CreateTuningSurface(
                mod,
                controller,
                "Garage",
                AlpineUiSurfaceMode.GarageTab,
                out render);

            var tabPanel = new VisualElement { name = AlpineNativeUiConfig.GarageTabName };
            tabPanel.Add(surface);
            tabPanel.style.display = DisplayStyle.None;
            tabPanel.style.flexGrow = 1;
            tabPanel.style.flexShrink = 1;

            var tabButton = new Button
            {
                name = AlpineNativeUiConfig.GarageTabButtonName,
                text = AlpineNativeUiConfig.ModTitle
            };

            tabButton.focusable = false;
            CopyClasses(LastButtonChild(tabsButtons), tabButton);

            int insertIndex = Mathf.Min(tabsButtons.childCount, tabs.childCount);
            tabsButtons.Insert(insertIndex, tabButton);
            tabs.Insert(insertIndex, tabPanel);

            if (TryRegisterNativeTab(nativeTabManager, tabPanel, tabButton, insertIndex, render, out int nativeIndex))
            {
                tabButton.clicked += () => SelectNativeTab(nativeTabManager, nativeIndex);
            }
            else
            {
                tabButton.clicked += () =>
                {
                    render();
                    SelectTabWithoutNativeManager(tabs, tabsButtons, tabPanel, tabButton);
                };
            }

            _attachedMenuCount++;
            return true;
        }

        private static bool TryFindNativeTabContainers(
            object nativeTabManager,
            out VisualElement tabsButtons,
            out VisualElement tabs)
        {
            tabsButtons = null;
            tabs = null;

            if (nativeTabManager == null)
                return false;

            try
            {
                var nativePanels = SleddersGameBindings.GetFieldValue<List<VisualElement>>(
                    nativeTabManager,
                    AlpineNativeUiConfig.NativeTabPanelsFieldName);

                var nativeButtons = SleddersGameBindings.GetFieldValue<List<Button>>(
                    nativeTabManager,
                    AlpineNativeUiConfig.NativeTabButtonsFieldName);

                if (nativePanels != null)
                    tabs = nativePanels.LastOrDefault(p => p != null && p.parent != null)?.parent;

                if (nativeButtons != null)
                    tabsButtons = nativeButtons.LastOrDefault(b => b != null && b.parent != null)?.parent;

                return tabsButtons != null && tabs != null;
            }
            catch
            {
                tabsButtons = null;
                tabs = null;
                return false;
            }
        }

        private static bool AttachToPauseMenu(AlpineTuningMod mod, PauseUIController controller)
        {
            if (mod == null || controller == null)
                return false;

            VisualElement menuRoot = FindVisualRoot(controller);
            if (menuRoot == null || menuRoot.Q<VisualElement>(AlpineNativeUiConfig.RootName) != null)
                return false;

            Button anchor =
                menuRoot.Q<Button>(AlpineNativeUiConfig.PauseSelectVehicleButtonName) ??
                menuRoot.Q<Button>(AlpineNativeUiConfig.PauseOptionsButtonName) ??
                FirstDescendant<Button>(menuRoot);

            VisualElement parent = anchor != null && anchor.parent != null
                ? anchor.parent
                : menuRoot;

            Action render;
            VisualElement surface = CreateTuningSurface(
                mod,
                controller,
                "Pause",
                AlpineUiSurfaceMode.PauseInline,
                out render);
            surface.style.display = DisplayStyle.None;

            var button = new Button
            {
                name = AlpineNativeUiConfig.PauseButtonName,
                text = AlpineNativeUiConfig.ModTitle
            };

            CopyClasses(anchor, button);
            ApplyNativeAttachedButtonStyle(button);

            int insertIndex = anchor != null ? parent.IndexOf(anchor) + 1 : parent.childCount;
            insertIndex = Mathf.Clamp(insertIndex, 0, parent.childCount);

            parent.Insert(insertIndex, button);
            parent.Insert(insertIndex + 1, surface);

            button.clicked += () =>
            {
                bool open = surface.style.display == DisplayStyle.None;
                surface.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                button.EnableInClassList("open", open);

                if (open)
                    render();
            };

            _attachedMenuCount++;
            return true;
        }

        private static bool AttachInlineFallback(AlpineTuningMod mod, object menuContext, string source, VisualElement parent)
        {
            if (mod == null || menuContext == null)
                return false;

            if (parent == null || parent.Q<VisualElement>(AlpineNativeUiConfig.RootName) != null)
                return false;

            Action render;
            VisualElement surface = CreateTuningSurface(
                mod,
                menuContext,
                source,
                AlpineUiSurfaceMode.FallbackInline,
                out render);
            surface.style.display = DisplayStyle.None;

            var button = new Button { text = AlpineNativeUiConfig.ModTitle };
            Button nativeButton = FirstDescendant<Button>(parent);
            CopyClasses(nativeButton, button);
            ApplyNativeAttachedButtonStyle(button);

            if (nativeButton == null)
                ApplyFallbackAttachedButtonStyle(button);

            button.clicked += () =>
            {
                bool open = surface.style.display == DisplayStyle.None;
                surface.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

                if (open)
                    render();
            };

            parent.Add(button);
            parent.Add(surface);
            _attachedMenuCount++;
            return true;
        }

        private static VisualElement CreateTuningSurface(
            AlpineTuningMod mod,
            object menuContext,
            string source,
            AlpineUiSurfaceMode mode,
            out Action renderAction)
        {
            var resolvedTarget = mod.ResolveTargetSledContext(menuContext);
            var target = resolvedTarget.sled;
            var working = target != null ? mod.CreateWorkingProfile(target) : null;
            string activeTab = mode == AlpineUiSurfaceMode.PauseInline
                ? AlpineNativeUiConfig.LibraryTabLabel
                : AlpineNativeUiConfig.EngineTabLabel;
            string librarySelectedProfileId = null;
            string pendingDeleteProfileId = null;
            bool factoryResetArmed = false;

            var root = new VisualElement { name = AlpineNativeUiConfig.RootName };
            var panel = new VisualElement { name = AlpineNativeUiConfig.PanelName };
            var title = new Label();
            var stats = new VisualElement();
            var tabRow = new VisualElement();
            var actionRow = new VisualElement();
            var primaryActions = new VisualElement();
            var dangerActions = new VisualElement();
            var status = new Label();
            var content = new ScrollView();
            var tooltip = CreateTooltipOverlay();
            var diagnostics = new Foldout
            {
                text = "Diagnostics",
                value = false
            };
            var tabButtons = new Dictionary<string, Button>();
            var scrollByTab = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
            Button testRideButton = null;
            Button resetButton = null;
            Button refreshSledButton = null;
            string lastRenderedTab = activeTab;
            string lastFocusedElementName = null;
            bool hasRenderedOnce = false;

            ApplyRootStyle(root, mode);
            ApplyPanelStyle(panel, mode);
            ApplyTitleStyle(title);
            ApplyStatRowStyle(stats);
            ApplyTabRowStyle(tabRow);
            ApplyActionRowStyle(actionRow, primaryActions, dangerActions);
            ApplyStatusStyle(status);
            ApplyContentStyle(content, mode);
            ApplyDiagnosticsStyle(diagnostics);

            Action render = null;
            Action refreshChrome = null;

            Action<string> setStatus = message =>
            {
                status.text = message ?? string.Empty;
            };
            setStatus(resolvedTarget.status);

            Action<TuneProfile> setWorking = profile =>
            {
                working = profile;
                pendingDeleteProfileId = null;
                factoryResetArmed = false;
            };

            Action setupChanged = () =>
            {
                if (target == null || working == null)
                    return;

                factoryResetArmed = false;
                pendingDeleteProfileId = null;
                string message;
                mod.UpdateCurrentSetup(working, target, out message);
                setStatus(message);
                refreshChrome?.Invoke();
            };

            Action refreshTarget = () =>
            {
                var refreshed = mod.ResolveTargetSledContext(menuContext);
                string previousKey = SledIdentity.StableIdentityKey(target);
                string nextKey = refreshed.identity != null ? refreshed.identity.StableKey : null;

                resolvedTarget = refreshed;
                if (refreshed.sled == null && target != null)
                {
                    target = null;
                    working = null;
                    pendingDeleteProfileId = null;
                    factoryResetArmed = false;
                    setStatus(refreshed.status);
                    return;
                }

                if (refreshed.sled != null &&
                    !string.Equals(previousKey, nextKey, StringComparison.OrdinalIgnoreCase))
                {
                    target = refreshed.sled;
                    working = mod.CreateWorkingProfile(target);
                    pendingDeleteProfileId = null;
                    factoryResetArmed = false;
                    setStatus(refreshed.status);
                }
            };

            Action<string> renderWithReason = null;
            render = () => renderWithReason?.Invoke("requested");
            renderWithReason = reason =>
            {
                string previousRenderedTab = lastRenderedTab;
                Vector2 previousScroll = content.scrollOffset;
                if (!string.IsNullOrWhiteSpace(previousRenderedTab))
                    scrollByTab[previousRenderedTab] = previousScroll;

                lastFocusedElementName = FocusedElementName(root);
                refreshTarget();

                ApplyRootStyle(root, mode);
                ApplyPanelStyle(panel, mode);
                ApplyTitleStyle(title);
                ApplyStatRowStyle(stats);
                ApplyTabRowStyle(tabRow);
                ApplyActionRowStyle(actionRow, primaryActions, dangerActions);
                ApplyStatusStyle(status);
                ApplyContentStyle(content, mode);
                ApplyDiagnosticsStyle(diagnostics);
                ApplyTabButtonStates(tabButtons, activeTab);

                content.Clear();
                stats.Clear();
                diagnostics.Clear();

                if (target == null || working == null)
                {
                    if (testRideButton != null)
                        testRideButton.SetEnabled(false);

                    if (resetButton != null)
                        resetButton.SetEnabled(false);

                    if (refreshSledButton != null)
                        refreshSledButton.style.display = DisplayStyle.Flex;

                    title.text = AlpineNativeUiConfig.ModTitle;
                    content.Add(new Label("Select a sled to edit its setup."));
                    diagnostics.Add(MutedLabel($"Source: {source}"));
                    RestoreUiState(root, content, scrollByTab, activeTab, lastFocusedElementName);
                    LogUiRefresh(reason, target, activeTab);
                    hasRenderedOnce = true;
                    lastRenderedTab = activeTab;
                    return;
                }

                mod.PreviewProfile(working, target);
                UpdateSummary(mod, title, stats, diagnostics, source, resolvedTarget, target, working);

                if (testRideButton != null)
                {
                    testRideButton.SetEnabled(true);
                }

                if (resetButton != null)
                    resetButton.SetEnabled(true);

                if (refreshSledButton != null)
                    refreshSledButton.style.display = ShouldShowRefreshSledButton(target, activeTab)
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;

                try
                {
                    switch (activeTab)
                    {
                        case AlpineNativeUiConfig.EngineTabLabel:
                            BuildEngineTab(mod, content, target, working, render, setupChanged);
                            break;

                        case AlpineNativeUiConfig.ClutchTabLabel:
                            BuildClutchTab(mod, content, target, working, render, setupChanged);
                            break;

                        case AlpineNativeUiConfig.SetupTabLabel:
                        case AlpineNativeUiConfig.FineTuneTabLabel:
                            BuildSetupTab(mod, content, target, working, render, setStatus, setupChanged);
                            break;

                        case AlpineNativeUiConfig.LightsTabLabel:
                            BuildLightsTab(mod, content, target, working, render, setupChanged, setStatus);
                            break;

                        case AlpineNativeUiConfig.PerformanceTabLabel:
                            BuildPerformanceTab(mod, content, target, working, render, setStatus);
                            break;

                        case AlpineNativeUiConfig.LibraryTabLabel:
                            BuildLibraryTab(mod, content, target, working, setWorking, render, setStatus, setupChanged,
                                () => librarySelectedProfileId,
                                id => librarySelectedProfileId = id,
                                () => pendingDeleteProfileId,
                                id => pendingDeleteProfileId = id,
                                () => factoryResetArmed,
                                value => factoryResetArmed = value);
                            break;

                        case AlpineNativeUiConfig.ShareTabLabel:
                            BuildShareTab(mod, content, target, working, render, setStatus);
                            break;

                        case AlpineNativeUiConfig.GuideTabLabel:
                            BuildGuideTab(mod, content);
                            break;

                        case AlpineNativeUiConfig.UiSettingsTabLabel:
                            BuildUiSettingsTab(mod, content, render, setStatus);
                            break;

                        case AlpineNativeUiConfig.TrackTabLabel:
                        default:
                            BuildTrackTab(mod, content, target, working, render, setupChanged);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Alpine tab '{activeTab}' render skipped: {ex.Message}");
                    content.Clear();
                    var card = Card(false);
                    card.Add(CardTitle($"{activeTab} unavailable"));
                    card.Add(MutedLabel("This tab hit a Sledders UI binding error. Other tabs remain available."));
                    content.Add(card);
                    setStatus("One tab could not render.");
                }

                RestoreUiState(root, content, scrollByTab, activeTab, lastFocusedElementName);
                if (hasRenderedOnce)
                    LogUiRefresh(reason, target, activeTab);
                hasRenderedOnce = true;
                lastRenderedTab = activeTab;
            };

            refreshChrome = () =>
            {
                if (target == null || working == null)
                    return;

                try
                {
                    mod.PreviewProfile(working, target);
                    UpdateSummary(mod, title, stats, diagnostics, source, resolvedTarget, target, working);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Garage summary update skipped: {ex.Message}");
                }
            };

            foreach (string tab in RuntimeTabLabels(mode))
            {
                string captured = tab;
                Button tabButton = TabButton(captured, () =>
                {
                    pendingDeleteProfileId = null;
                    factoryResetArmed = false;
                    activeTab = captured;
                    renderWithReason?.Invoke("tab changed");
                });
                tabButtons[captured] = tabButton;
                tabRow.Add(tabButton);
            }

            testRideButton = PrimaryButton("Test Ride", () =>
            {
                if (target == null || working == null)
                    return;

                factoryResetArmed = false;
                pendingDeleteProfileId = null;
                setupChanged();
                if (mod.HasRuntimeInstanceForSled(target))
                {
                    mod.ReloadSled();
                    setStatus("Setup ready for test ride.");
                }
                else
                {
                    setStatus("Setup ready for your next ride.");
                }
                renderWithReason?.Invoke("test ride");
            });
            primaryActions.Add(testRideButton);

            primaryActions.Add(SmallButton("Save Setup", () =>
            {
                if (target == null || working == null)
                    return;

                factoryResetArmed = false;
                pendingDeleteProfileId = null;

                string message;
                setStatus(mod.SaveCurrentSetupAsSlot(working, target, out message)
                    ? message
                    : (string.IsNullOrWhiteSpace(message) ? AlpineNativeUiConfig.SaveFailedText : message));
                renderWithReason?.Invoke("setup saved");
            }));

            primaryActions.Add(SmallButton("Set Default", () =>
            {
                if (target == null || working == null)
                    return;

                factoryResetArmed = false;
                pendingDeleteProfileId = null;

                string message;
                setStatus(mod.SaveCurrentSetupAsDefault(working, target, out message)
                    ? message
                    : (string.IsNullOrWhiteSpace(message) ? "Default setup save failed." : message));
                renderWithReason?.Invoke("default setup saved");
            }));

            primaryActions.Add(SmallButton("Setup Slots", () =>
            {
                pendingDeleteProfileId = null;
                factoryResetArmed = false;
                activeTab = AlpineNativeUiConfig.LibraryTabLabel;
                renderWithReason?.Invoke("setup slots opened");
            }));

            resetButton = DangerButton("Reset", () =>
            {
                if (target == null)
                    return;

                pendingDeleteProfileId = null;

                if (!factoryResetArmed)
                {
                    factoryResetArmed = true;
                    setStatus("Press Reset again to return this sled to stock.");
                    renderWithReason?.Invoke("reset armed");
                    return;
                }

                factoryResetArmed = false;
                if (mod.ResetToFactory(target, false))
                {
                    working = mod.CreateWorkingProfile(target);
                    setStatus(AlpineNativeUiConfig.FactoryDefaultsRestoredText);
                }
                else
                {
                    setStatus(AlpineNativeUiConfig.ResetFailedText);
                }

                renderWithReason?.Invoke("reset completed");
            });
            dangerActions.Add(resetButton);

            if (AlpineNativeUiConfig.ShowRefreshSledButton)
            {
                refreshSledButton = SmallButton(AlpineNativeUiConfig.RefreshSledLabel, () =>
                {
                    factoryResetArmed = false;
                    pendingDeleteProfileId = null;
                    resolvedTarget = mod.ResolveTargetSledContext(menuContext);
                    target = resolvedTarget.sled;
                    working = target != null ? mod.CreateWorkingProfile(target) : null;
                    setStatus(AlpineNativeUiConfig.RefreshedSledText);
                    renderWithReason?.Invoke("selected sled refreshed");
                });
                primaryActions.Add(refreshSledButton);
            }
            actionRow.Add(primaryActions);
            actionRow.Add(dangerActions);

            panel.Add(title);
            if (mode == AlpineUiSurfaceMode.GarageTab)
                panel.Add(stats);
            panel.Add(tabRow);
            panel.Add(actionRow);
            panel.Add(status);
            panel.Add(content);

            if (mode == AlpineUiSurfaceMode.GarageTab)
                panel.Add(diagnostics);

            root.Add(panel);

            // Tooltip must be an overlay sibling, not part of the panel's flex layout.
            // If this is added to panel/content as a normal child, it will resize the menu
            // and move hovered controls, causing tooltip flicker.
            root.Add(tooltip);
            AttachTooltipFeedback(root, tooltip);
            root.schedule.Execute(() =>
            {
                if (root.panel != null)
                    renderWithReason("panel opened");
            });

            if (menuContext is VehicleSelectionUiController garageController)
            {
                int garageId = garageController.GetInstanceID();
                GarageRenderActions[garageId] = render;
                root.RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    if (GarageRenderActions.TryGetValue(garageId, out var registered) &&
                        registered == render)
                    {
                        GarageRenderActions.Remove(garageId);
                    }
                });
            }

            renderAction = render;
            return root;
        }

        private static IEnumerable<string> RuntimeTabLabels(AlpineUiSurfaceMode mode)
        {
            if (mode == AlpineUiSurfaceMode.PauseInline)
            {
                yield return AlpineNativeUiConfig.LibraryTabLabel;
                yield return AlpineNativeUiConfig.ShareTabLabel;
                yield break;
            }

            yield return AlpineNativeUiConfig.EngineTabLabel;
            yield return AlpineNativeUiConfig.ClutchTabLabel;
            yield return AlpineNativeUiConfig.SetupTabLabel;
            yield return AlpineNativeUiConfig.TrackTabLabel;
            if (SleddersGameBindings.HeadlightRuntimeBindingAvailable)
                yield return AlpineNativeUiConfig.LightsTabLabel;
            yield return AlpineNativeUiConfig.PerformanceTabLabel;
            yield return AlpineNativeUiConfig.LibraryTabLabel;
            yield return AlpineNativeUiConfig.ShareTabLabel;
            yield return AlpineNativeUiConfig.GuideTabLabel;

            if (mode == AlpineUiSurfaceMode.GarageTab &&
                AlpineNativeUiConfig.EnableRuntimeUiSettingsTab)
            {
                yield return AlpineNativeUiConfig.UiSettingsTabLabel;
            }
        }

        private static bool ShouldShowNativeAccessoriesCategory()
        {
            return AlpineNativeUiConfig.ShowNativeAccessoriesCategory;
        }

        private static bool ShouldShowRefreshSledButton(VehicleScriptableObject target, string activeTab)
        {
            if (!AlpineNativeUiConfig.ShowRefreshSledButton)
                return false;

            if (target == null)
                return true;

            return string.Equals(activeTab, AlpineNativeUiConfig.UiSettingsTabLabel, StringComparison.OrdinalIgnoreCase);
        }

        private static void UpdateSummary(
            AlpineTuningMod mod,
            Label title,
            VisualElement statsRow,
            Foldout diagnostics,
            string source,
            ResolvedSledTarget resolvedTarget,
            VehicleScriptableObject sled,
            TuneProfile profile)
        {
            string sledName = AlpineTuningMod.GetSledDisplayName(sled);

            if (title != null)
                title.text = $"{AlpineNativeUiConfig.ModTitle}\n{sledName}";

            var stats = profile.resolvedStats;
            var settings = mod != null ? mod.Settings : new AlpineUserSettings();
            AlpineDisplayUnits units = settings.units;
            var defaults = mod != null && sled != null ? mod.Store.GetDefaults(AlpineTuningMod.GetSledKey(sled)) : null;

            if (statsRow != null)
            {
                statsRow.Clear();
                AddStatusChip(statsRow, $"Current Setup: {mod.CurrentSetupDisplayName(profile)}");
                AddStatusChip(statsRow, $"Status: {(resolvedTarget != null ? resolvedTarget.status : "Ready")}");
                AddStatChip(statsRow, "Engine Output", UnitConversion.FormatPower(stats.horsePower, units));
                AddStatChip(statsRow, "Drive Response", $"{stats.powerFactor:F2}");
                AddStatChip(statsRow, "Paddle", TrackSpecResolver.FormatPaddleHeight(stats.lugHeight));
                AddStatChip(statsRow, "Track Bite", FormatPercentDelta(stats.friction, defaults != null ? defaults.friction : stats.friction));
                AddStatChip(statsRow, "Weight", UnitConversion.FormatWeight(stats.weight, units));

                if (settings.advancedDetails)
                {
                    AddStatChip(statsRow, "Raw Track Bite", $"{stats.friction:F2}");
                    AddStatChip(statsRow, "Ski Width", UnitConversion.FormatLengthFromMeters(stats.skiStance, units));
                }

                if (profile.requiresReload)
                    AddStatusChip(statsRow, AlpineNativeUiConfig.ReloadRequiredHintText);
            }

            if (diagnostics != null)
            {
                diagnostics.Clear();
                diagnostics.Add(MutedLabel($"Source: {source}"));
                if (resolvedTarget != null && resolvedTarget.identity != null)
                {
                    diagnostics.Add(MutedLabel($"Selected Sled: {resolvedTarget.identity.displayName}"));
                    diagnostics.Add(MutedLabel($"Identity: {resolvedTarget.identity.StableKey}"));
                    diagnostics.Add(MutedLabel($"Runtime: {(resolvedTarget.hasRuntimeInstance ? "matched" : "not spawned")}"));
                }
                diagnostics.Add(MutedLabel($"Alpine {AlpineConstants.ModVersion}"));
                diagnostics.Add(MutedLabel($"Catalog {AlpineConstants.CatalogVersion}"));
                diagnostics.Add(MutedLabel($"Schema {AlpineConstants.SchemaVersion}"));
                var report = SleddersGameBindings.GetCompatibilityReport();
                if (report != null)
                {
                    diagnostics.Add(MutedLabel($"Compatibility: {DisplayOrUnknown(report.overallStatus)}"));
                    diagnostics.Add(MutedLabel($"Assembly Fingerprint: {DisplayOrUnknown(report.assemblyLightHash)}"));
                }
            }
        }

        private static void BuildTrackTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action render,
            Action setupChanged,
            Action<string> setStatus = null)
        {
            var trackSection = Section("Track");
            AddPartDropdown(mod, trackSection, working, PartCatalog.Track, render, setupChanged, "Paddle / Track Package");

            if (working.resolvedStats != null)
                trackSection.Add(MutedLabel($"Resolved paddle height: {TrackSpecResolver.FormatPaddleHeight(working.resolvedStats.lugHeight)}"));
            trackSection.Add(MutedLabel("Long Track Kit changes setup feel; visual track length depends on Sledders model support."));

            content.Add(trackSection);

            var setupSection = Section("Track Setup");
            AddPartDropdown(mod, setupSection, working, PartCatalog.TrackLimiter, render, setupChanged, "Limiter Strap Setup");
            AddPartDropdown(mod, setupSection, working, PartCatalog.RearShock, render, setupChanged, "Rear Shock Setup");
            AddPartDropdown(mod, setupSection, working, PartCatalog.RearSpring, render, setupChanged, "Rear Spring Setup");
            content.Add(setupSection);

            var detailsSection = Section("Details");
            detailsSection.Add(BuildPartDetailsFoldout(mod, working));
            content.Add(detailsSection);
        }

        private static void BuildEngineTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action render,
            Action setupChanged)
        {
            var profileSection = Section("Current Setup");
            AddProfileNameField(profileSection, working, setupChanged);
            AddDonorDropdown(mod, profileSection, working, render, setupChanged);
            content.Add(profileSection);

            var engineSection = Section("Engine");
            AddPartDropdown(mod, engineSection, working, PartCatalog.EngineCore, render, setupChanged, "Block / Engine Package");
            AddPartDropdown(mod, engineSection, working, PartCatalog.EnginePiston, render, setupChanged);
            AddPartDropdown(mod, engineSection, working, PartCatalog.EngineCrank, render, setupChanged);
            AddPartDropdown(mod, engineSection, working, PartCatalog.Intake, render, setupChanged, "Intake / Exhaust");
            AddPartDropdown(mod, engineSection, working, PartCatalog.Turbo, render, setupChanged, "Turbo / Induction");
            content.Add(engineSection);

            var boostSection = Section("Boost Estimate");
            AddBoostEstimate(boostSection, working.resolvedStats);
            content.Add(boostSection);
        }

        private static void BuildClutchTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action render,
            Action setupChanged)
        {
            var clutchSection = Section("Clutch");
            AddPartDropdown(mod, clutchSection, working, PartCatalog.Clutch, render, setupChanged, "Clutch Calibration");
            AddPartDropdown(mod, clutchSection, working, PartCatalog.ClutchWeights, render, setupChanged);
            AddPartDropdown(mod, clutchSection, working, PartCatalog.RatioFeel, render, setupChanged);
            content.Add(clutchSection);

            var fine = working.fineTune ?? (working.fineTune = new FineTuneSettings());
            var trimSection = Section("Calibration Trim");
            AddSlider(
                trimSection,
                "Clutch Trim",
                AlpineNativeUiConfig.ClutchTrimMin,
                AlpineNativeUiConfig.ClutchTrimMax,
                fine.clutchTrimPercent,
                "F1",
                "%",
                value => fine.clutchTrimPercent = value,
                setupChanged,
                "Adjusts clutch RPM response. Higher values hold more RPM and feel more aggressive.");
            content.Add(trimSection);
        }

        private static void BuildSetupTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action render,
            Action<string> setStatus,
            Action setupChanged)
        {
            content.Add(MutedLabel(AlpineNativeUiConfig.FineTuneHelpText));

            var fine = working.fineTune ?? (working.fineTune = new FineTuneSettings());

            var handlingSection = Section("Chassis / Stance");
            AddPartDropdown(mod, handlingSection, working, PartCatalog.Suspension, render, setupChanged, "Handling Setup");
            AddPartDropdown(mod, handlingSection, working, PartCatalog.Chassis, render, setupChanged);
            AddPartDropdown(mod, handlingSection, working, PartCatalog.Skis, render, setupChanged);

            if (ShouldShowNativeAccessoriesCategory())
                AddPartDropdown(mod, handlingSection, working, PartCatalog.Accessories, render, setupChanged);

            content.Add(handlingSection);

            var driveSection = Section("Power / Drive");
            AddSlider(
                driveSection,
                "Power Trim",
                AlpineNativeUiConfig.PowerTrimMin,
                AlpineNativeUiConfig.PowerTrimMax,
                fine.powerTrimPercent,
                "F1",
                "%",
                value => fine.powerTrimPercent = value,
                setupChanged,
                "Fine adjustment for estimated engine output. Higher values increase acceleration and track speed.");

            AddSlider(
                driveSection,
                "Traction Trim",
                AlpineNativeUiConfig.TractionTrimMin,
                AlpineNativeUiConfig.TractionTrimMax,
                fine.tractionTrimPercent,
                "F1",
                "%",
                value => fine.tractionTrimPercent = value,
                setupChanged,
                "Fine adjustment for snow traction. Higher values bite harder but can add drag.");

            AddSlider(
                driveSection,
                "Weight Trim",
                AlpineNativeUiConfig.WeightTrimMin,
                AlpineNativeUiConfig.WeightTrimMax,
                fine.weightTrimPercent,
                "F1",
                "%",
                value => fine.weightTrimPercent = value,
                setupChanged,
                "Fine adjustment for setup weight. Lower weight improves response and climbing.");
            content.Add(driveSection);

            var balanceSection = Section("Balance / Stance");
            AddSlider(
                balanceSection,
                "Center of Mass Height",
                AlpineNativeUiConfig.CenterOfMassYMin,
                AlpineNativeUiConfig.CenterOfMassYMax,
                fine.centerOfMassYTrim,
                "F3",
                " m",
                value => fine.centerOfMassYTrim = value,
                setupChanged,
                "Moves weight higher or lower. Lower feels more stable; higher can feel more playful.",
                value => UnitConversion.FormatLengthFromMeters(value, mod.Settings.units));

            AddSlider(
                balanceSection,
                "Center of Mass Forward",
                AlpineNativeUiConfig.CenterOfMassZMin,
                AlpineNativeUiConfig.CenterOfMassZMax,
                fine.centerOfMassZTrim,
                "F3",
                " m",
                value => fine.centerOfMassZTrim = value,
                setupChanged,
                "Moves balance forward or rearward. Forward helps front bite; rearward helps lift.",
                value => UnitConversion.FormatLengthFromMeters(value, mod.Settings.units));

            AddSlider(
                balanceSection,
                "Ski Stance",
                AlpineNativeUiConfig.SkiStanceMin,
                AlpineNativeUiConfig.SkiStanceMax,
                fine.skiStanceTrim,
                "F3",
                " m",
                value => fine.skiStanceTrim = value,
                setupChanged,
                "Changes front ski width. Wider improves stability; narrower turns tighter.",
                value => UnitConversion.FormatLengthFromMeters(value, mod.Settings.units));
            content.Add(balanceSection);

            Button previewButton = SmallButton("Update Summary", () =>
            {
                mod.PreviewProfile(working, target);
                setStatus(AlpineNativeUiConfig.PreviewUpdatedText);
                render();
            });

            AddSplitButtonRow(content,
                new[] { previewButton },
                null);
        }

        private static void BuildLightsTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action render,
            Action setupChanged,
            Action<string> setStatus)
        {
            var lightSection = Section("Headlights");
            AddPartDropdown(mod, lightSection, working, PartCatalog.HeadlightColor, render, setupChanged);
            AddPartDropdown(mod, lightSection, working, PartCatalog.HeadlightBrightness, render, setupChanged);
            AddPartDropdown(mod, lightSection, working, PartCatalog.HeadlightBeam, render, setupChanged);
            AddPartDropdown(mod, lightSection, working, PartCatalog.HeadlightAim, render, setupChanged);

            if (mod != null && mod.HasActiveHeadlightRuntimeBinding())
                lightSection.Add(MutedLabel("Runtime headlight binding active for the current sled."));
            else
                lightSection.Add(MutedLabel("Runtime headlight binding is unavailable until a sled with native HeadLight components is active."));

            content.Add(lightSection);

            var controlsSection = Section("Controls");
            var settings = mod.Settings;
            controlsSection.Add(MutedLabel($"Headlight Mode: {FormatHeadlightMode(working)}"));
            controlsSection.Add(MutedLabel($"Hotkey: {FormatHeadlightBinding(settings)}"));
            controlsSection.Add(MutedLabel($"Keyboard Bind: {FormatSingleHeadlightBinding(settings?.headlightKeyboardKey)}"));
            controlsSection.Add(MutedLabel($"Controller Bind: {FormatSingleHeadlightBinding(settings?.headlightControllerButton)}"));
            if (mod.IsCapturingHeadlightBinding && !string.IsNullOrWhiteSpace(mod.HeadlightBindingCaptureLabel))
                controlsSection.Add(MutedLabel(mod.HeadlightBindingCaptureLabel));

            AddButtonRow(controlsSection,
                SmallButton(settings.headlightToggleEnabled ? "Disable Hotkey" : "Enable Hotkey", () =>
                {
                    if (!settings.headlightToggleEnabled && !HasConfiguredHeadlightBinding(settings))
                    {
                        setStatus?.Invoke("Set a keyboard or controller bind first.");
                        render();
                        return;
                    }

                    settings.headlightToggleEnabled = !settings.headlightToggleEnabled;
                    settings.Normalize();
                    mod.SaveSettings();
                    render();
                }),
                SmallButton("Set Keyboard Bind", () =>
                {
                    mod.BeginHeadlightKeyboardBind();
                    setStatus?.Invoke("Press the keyboard key to use for the headlight hotkey.");
                    render();
                }),
                SmallButton("Set Controller Bind", () =>
                {
                    mod.BeginHeadlightControllerBind();
                    setStatus?.Invoke("Press the controller button to use for the headlight hotkey.");
                    render();
                }),
                SmallButton("Clear Binding", () =>
                {
                    mod.ClearHeadlightBinding();
                    setStatus?.Invoke("Headlight hotkey binding cleared.");
                    render();
                }));

            AddButtonRow(controlsSection,
                SmallButton("Force On", () =>
                {
                    string message;
                    bool updated = mod.SetSetupHeadlightEnabled(working, target, true, out message);
                    setStatus?.Invoke(updated ? "Headlights forced on." : message);
                    render();
                }),
                SmallButton("Force Off", () =>
                {
                    string message;
                    bool updated = mod.SetSetupHeadlightEnabled(working, target, false, out message);
                    setStatus?.Invoke(updated ? "Headlights forced off." : message);
                    render();
                }),
                SmallButton("Follow Game Time", () =>
                {
                    string message;
                    bool updated = mod.ClearSetupHeadlightOverride(working, target, out message);
                    setStatus?.Invoke(updated ? message : (string.IsNullOrWhiteSpace(message) ? "Headlight mode update failed." : message));
                    render();
                }));

            content.Add(controlsSection);
        }

        private static void BuildLibraryTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action<TuneProfile> setWorking,
            Action render,
            Action<string> setStatus,
            Action setupChanged,
            Func<string> getSelectedProfileId,
            Action<string> setSelectedProfileId,
            Func<string> getPendingDeleteProfileId,
            Action<string> setPendingDeleteProfileId,
            Func<bool> getFactoryResetArmed,
            Action<bool> setFactoryResetArmed)
        {
            var currentSection = Section("Current Setup");
            currentSection.Add(MutedLabel("Changes are preserved automatically for this sled."));
            AddButtonRow(currentSection,
                SmallButton("Save Setup", () =>
                {
                    setPendingDeleteProfileId?.Invoke(null);
                    string message;
                    setStatus(mod.SaveCurrentSetupAsSlot(working, target, out message)
                        ? message
                        : (string.IsNullOrWhiteSpace(message) ? AlpineNativeUiConfig.SaveFailedText : message));
                    render();
                }));
            content.Add(currentSection);

            var restoreSection = Section("Reset");
            bool factoryResetArmed = getFactoryResetArmed != null && getFactoryResetArmed();
            restoreSection.Add(MutedLabel("Reset returns this sled's current setup to stock."));
            AddButtonRow(restoreSection,
                DangerButton(factoryResetArmed ? "Confirm Reset" : "Reset to Stock", () =>
                {
                    setPendingDeleteProfileId?.Invoke(null);

                    if (!factoryResetArmed)
                    {
                        setFactoryResetArmed?.Invoke(true);
                        setStatus("Press Reset to Stock again to confirm.");
                        render();
                        return;
                    }

                    setFactoryResetArmed?.Invoke(false);
                    if (mod.ResetToFactory(target, false))
                    {
                        setWorking(mod.CreateWorkingProfile(target));
                        setStatus(AlpineNativeUiConfig.FactoryDefaultsRestoredText);
                    }
                    else
                    {
                        setStatus(AlpineNativeUiConfig.ResetFailedText);
                    }

                    render();
                }));
            content.Add(restoreSection);

            var profiles = mod.ProfilesForSled(target);
            if (profiles.Count == 0)
            {
                content.Add(MutedLabel(AlpineNativeUiConfig.NoSavedProfilesText));
                content.Add(MutedLabel("Use Save Setup to create your first setup slot for this sled."));
                return;
            }

            string selectedId = getSelectedProfileId != null ? getSelectedProfileId() : null;
            string pendingDeleteId = getPendingDeleteProfileId != null ? getPendingDeleteProfileId() : null;

            var savedSection = Section("Setup Slots");

            foreach (var profile in profiles)
            {
                TuneProfile captured = profile;
                bool isSelected = !string.IsNullOrWhiteSpace(selectedId) &&
                                  string.Equals(selectedId, captured.profileId, StringComparison.OrdinalIgnoreCase);
                bool isPendingDelete = !string.IsNullOrWhiteSpace(pendingDeleteId) &&
                                       string.Equals(pendingDeleteId, captured.profileId, StringComparison.OrdinalIgnoreCase);

                var card = Card(isSelected);
                var cardHeader = new VisualElement();
                cardHeader.style.flexDirection = FlexDirection.Row;
                cardHeader.style.flexWrap = Wrap.Wrap;
                cardHeader.style.alignItems = Align.Center;

                var name = CardTitle(captured.name ?? "(unnamed setup)");
                name.style.flexGrow = 1;
                cardHeader.Add(name);

                if (isSelected)
                {
                    cardHeader.Add(Badge("Selected"));
                }
                else
                {
                    cardHeader.Add(SmallButton("Select", () =>
                    {
                        setSelectedProfileId?.Invoke(captured.profileId);
                        setPendingDeleteProfileId?.Invoke(null);
                        render();
                    }));
                }

                card.Add(cardHeader);

                var preview = TuneStore.Clone(captured);
                mod.PreviewProfile(preview, target);
                card.Add(StatsPreview(mod, target, preview.resolvedStats, preview.requiresReload));
                card.Add(MutedLabel($"Builder: {DisplayOrUnknown(captured.author)} | Updated: {FormatUnixTime(captured.updatedUnixTime)}"));

                if (isSelected)
                {
                    var renameField = new TextField("Rename Setup")
                    {
                        value = captured.name ?? string.Empty
                    };
                    ApplyControlStyle(renameField);
                    renameField.RegisterValueChangedCallback(evt =>
                    {
                        string message;
                        if (mod.RenameSetupSlot(captured, target, evt.newValue, out message))
                        {
                            captured.name = evt.newValue;
                            setStatus(message);
                        }
                        else if (!string.IsNullOrWhiteSpace(message))
                        {
                            setStatus(message);
                        }
                    });
                    card.Add(renameField);

                    Button equipButton = PrimaryButton("Equip", () =>
                    {
                        setPendingDeleteProfileId?.Invoke(null);
                        TuneProfile equipped;
                        string message;
                        if (mod.EquipSetupSlot(captured, target, out equipped, out message))
                            setWorking(equipped);

                        setStatus(string.IsNullOrWhiteSpace(message)
                            ? AlpineNativeUiConfig.ApplyFailedText
                            : message);
                        render();
                    });

                    Button duplicateButton = SmallButton("Duplicate Setup", () =>
                    {
                        setPendingDeleteProfileId?.Invoke(null);
                        string message;
                        mod.DuplicateSetupSlot(captured, target, out message);
                        setStatus(string.IsNullOrWhiteSpace(message) ? "Setup duplicated." : message);
                        render();
                    });

                    Button defaultButton = SmallButton("Set as Default", () =>
                    {
                        setPendingDeleteProfileId?.Invoke(null);
                        string message;
                        mod.SetDefaultSetup(captured, target, out message);
                        setStatus(message);
                        render();
                    });

                    Button shareButton = SmallButton("Share", () =>
                    {
                        setPendingDeleteProfileId?.Invoke(null);
                        var toShare = TuneStore.Clone(captured);
                        if (!mod.SaveProfile(toShare, target, false))
                        {
                            setStatus(AlpineNativeUiConfig.SaveFailedText);
                        }
                        else
                        {
                            setStatus(mod.PublishProfile(toShare, target)
                                ? AlpineNativeUiConfig.PublishedTuneText
                                : (mod.Sharing != null && !string.IsNullOrWhiteSpace(mod.Sharing.StatusMessage)
                                    ? mod.Sharing.StatusMessage
                                    : AlpineNativeUiConfig.SharingUnavailableText));
                        }
                        render();
                    });
                    if (AlpineConstants.PeerSharingTemporarilyDisabled)
                    {
                        shareButton.text = "Sharing Paused";
                        shareButton.SetEnabled(false);
                    }

                    Button deleteButton = DangerButton(isPendingDelete ? "Confirm Remove" : "Remove", () =>
                    {
                        if (!isPendingDelete)
                        {
                            setPendingDeleteProfileId?.Invoke(captured.profileId);
                            setStatus($"Press Remove again to remove {captured.name}.");
                            render();
                            return;
                        }

                        mod.DeleteProfile(captured.profileId);
                        if (string.Equals(selectedId, captured.profileId, StringComparison.OrdinalIgnoreCase))
                            setSelectedProfileId?.Invoke(null);

                        setPendingDeleteProfileId?.Invoke(null);
                        setStatus($"Removed {captured.name}.");
                        render();
                    });

                    AddSplitButtonRow(card,
                        new[] { equipButton, duplicateButton, defaultButton, shareButton },
                        new[] { deleteButton });
                }

                savedSection.Add(card);
            }

            content.Add(savedSection);
        }

        private static void BuildShareTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action render,
            Action<string> setStatus)
        {
            var sharingSection = Section("Multiplayer Sharing");
            sharingSection.Add(MutedLabel(AlpineConstants.PeerSharingPausedNotice));
            sharingSection.Add(MutedLabel("Local setup slots, defaults, lighting, and performance tuning remain available."));
            content.Add(sharingSection);
        }

        private static void BuildPerformanceTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action render,
            Action<string> setStatus)
        {
            var estimate = mod.EstimatePerformance(working, target);
            var settings = mod.Settings;

            var header = Section("Performance Estimate");
            header.Add(MutedLabel($"{DisplayOrUnknown(estimate.sledName)} | {DisplayOrUnknown(estimate.setupName)}"));
            header.Add(MutedLabel("Garage estimate based on the current setup. It is a tuning guide, not measured live dyno data."));
            content.Add(header);

            var bars = Section("Compared to Stock");
            foreach (var stat in estimate.stats)
                bars.Add(PerformanceBar(stat));
            content.Add(bars);

            var graph = Section("Estimated Dyno");
            graph.Add(BuildCurveGraph(estimate, settings.units));
            graph.Add(MutedLabel(
                $"Peak Power: {UnitConversion.FormatPower(estimate.peakHorsepower, settings.units)} | " +
                $"Peak Torque: {UnitConversion.FormatTorque(estimate.peakTorqueNm, settings.units)} | " +
                $"Engagement: {estimate.engagementRpm:F0} rpm | " +
                $"Weight: {UnitConversion.FormatWeight(estimate.estimatedWeightKg, settings.units)}"));
            content.Add(graph);

            if (settings.advancedDetails)
            {
                var advanced = Section("Advanced Estimate");
                advanced.Add(MutedLabel("Curve and stat values are derived from setup parts, stock baseline values, and Alpine's tune math."));
                if (working.resolvedStats != null)
                {
                    advanced.Add(MutedLabel($"Drive Response: {working.resolvedStats.powerFactor:F3}"));
                    advanced.Add(MutedLabel($"Raw Track Bite: {working.resolvedStats.friction:F3}"));
                    advanced.Add(MutedLabel($"Paddle Height: {TrackSpecResolver.FormatPaddleHeight(working.resolvedStats.lugHeight)}"));
                    advanced.Add(MutedLabel($"Boost Estimate: {working.resolvedStats.estimatedBoostPsi:F1} psi"));
                }
                content.Add(advanced);
            }
        }

        private static void AddCompatibilityReport(
            AlpineTuningMod mod,
            VisualElement content,
            bool includeDetails,
            bool includeDiagnosticActions,
            Action render = null,
            Action<string> setStatus = null)
        {
            var report = SleddersGameBindings.GetCompatibilityReport();
            var section = Section("Compatibility");

            if (report == null)
            {
                section.Add(MutedLabel("Status: unknown"));
                content.Add(section);
                return;
            }

            section.Add(MutedLabel($"Status: {DisplayOrUnknown(report.overallStatus)}"));
            section.Add(MutedLabel(report.SummaryLine));

            if (includeDetails)
            {
                section.Add(MutedLabel($"Assembly: {DisplayOrUnknown(report.assemblyPath)}"));
                section.Add(MutedLabel($"Last Write: {DisplayOrUnknown(report.assemblyLastWriteUtc)}"));
                section.Add(MutedLabel($"Fingerprint: {DisplayOrUnknown(report.assemblyLightHash)} ({FormatBytes(report.assemblyLengthBytes)})"));

                foreach (var capability in report.capabilities)
                {
                    if (capability == null)
                        continue;

                    section.Add(MutedLabel(
                        $"{capability.label}: {DisplayOrUnknown(capability.state)}" +
                        (string.IsNullOrWhiteSpace(capability.detail) ? string.Empty : $" - {capability.detail}")));
                }
            }

            if (includeDiagnosticActions && mod != null && AlpineConstants.PeerSharingTemporarilyDisabled)
            {
                section.Add(MutedLabel("Peer transport diagnostics are paused with networked setup sharing."));
            }
            else if (includeDiagnosticActions && mod != null)
            {
                var settings = mod.Settings;
                section.Add(MutedLabel("Steam ID scanning is a log-only diagnostic and stays off during normal peer discovery."));

                var runScan = SmallButton("Run Steam ID Scan", () =>
                {
                    bool ran = SleddersGameBindings.LogNetClientSteamIdScan(settings.diagnosticSteamIdScanEnabled);
                    setStatus?.Invoke(ran
                        ? "Steam ID diagnostic scan written to the MelonLoader log."
                        : "Enable the Steam ID diagnostic scanner first.");
                });
                runScan.SetEnabled(settings.diagnosticSteamIdScanEnabled);

                AddButtonRow(section,
                    SmallButton(settings.diagnosticSteamIdScanEnabled ? "Steam ID Scan: On" : "Steam ID Scan: Off", () =>
                    {
                        settings.diagnosticSteamIdScanEnabled = !settings.diagnosticSteamIdScanEnabled;
                        mod.SaveSettings();
                        setStatus?.Invoke(settings.diagnosticSteamIdScanEnabled
                            ? "Steam ID diagnostic scanner enabled."
                            : "Steam ID diagnostic scanner disabled.");
                        render?.Invoke();
                    }),
                    runScan);
            }

            content.Add(section);
        }

        private static void BuildGuideTab(AlpineTuningMod mod, VisualElement content)
        {
            var categories = Section("Tuning Guide");
            categories.Add(MutedLabel("Engine changes estimated output, boost, and throttle feel."));
            categories.Add(MutedLabel("Clutching changes engagement RPM and how strongly the sled holds power under load."));
            categories.Add(MutedLabel("Track changes paddle height, track bite, powder float, and rotating weight."));
            categories.Add(MutedLabel("Suspension changes weight transfer, ski stance, balance, and stability."));
            categories.Add(MutedLabel("Lighting changes headlight color, brightness, beam, aim, and forced headlight mode."));
            categories.Add(MutedLabel("Multiplayer setup sharing is paused while new P2P methods are investigated."));
            content.Add(categories);

            var statuses = Section("Statuses");
            statuses.Add(MutedLabel("Ready: the selected sled can use this setup."));
            statuses.Add(MutedLabel("Updated: the current spawned sled has been adjusted."));
            statuses.Add(MutedLabel("Ready for next ride: the setup has been saved and will equip when that sled spawns."));
            statuses.Add(MutedLabel("Setup saved: the current setup was written to a setup slot."));
            statuses.Add(MutedLabel("Default setup saved: the current setup will equip automatically for that sled."));
            statuses.Add(MutedLabel("Returned to stock: Alpine tuning fields were reset without changing vanilla cosmetics."));
            content.Add(statuses);

            var units = Section("Units");
            units.Add(MutedLabel(mod.Settings.units == AlpineDisplayUnits.Imperial
                ? "Imperial units are active: lb, hp, lb-ft, inches, rpm."
                : "Metric units are active: kg, kW, Nm, mm, rpm."));
            units.Add(MutedLabel("Power and dyno values are estimates unless a future live telemetry source is available."));
            content.Add(units);

            var limitations = Section("Current Limits");
            limitations.Add(MutedLabel("Long Track Kit changes handling, bite, weight, and balance. Visual track length depends on game model support."));
            limitations.Add(MutedLabel(AlpineConstants.PeerSharingPausedNotice));
            content.Add(limitations);

            AddCompatibilityReport(mod, content, mod != null && mod.Settings.advancedDetails, false);
        }

        private static void BuildUiSettingsTab(
            AlpineTuningMod mod,
            VisualElement content,
            Action render,
            Action<string> setStatus)
        {
            var settings = mod.Settings;

            var garageSection = Section("Garage Settings");
            AddButtonRow(garageSection,
                SmallButton(settings.units == AlpineDisplayUnits.Metric ? "Units: Metric" : "Units: Imperial", () =>
                {
                    settings.units = settings.units == AlpineDisplayUnits.Metric
                        ? AlpineDisplayUnits.Imperial
                        : AlpineDisplayUnits.Metric;
                    mod.SaveSettings();
                    setStatus("Units updated.");
                    render();
                }),
                SmallButton(settings.advancedDetails ? "Advanced Details: On" : "Advanced Details: Off", () =>
                {
                    settings.advancedDetails = !settings.advancedDetails;
                    mod.SaveSettings();
                    setStatus(settings.advancedDetails ? "Advanced details shown." : "Advanced details hidden.");
                    render();
                }));
            content.Add(garageSection);

            AddCompatibilityReport(mod, content, settings.advancedDetails, settings.advancedDetails, render, setStatus);

            if (!settings.advancedDetails)
                return;

            var debugSection = Section("Panel Layout");
            debugSection.Add(MutedLabel("Layout controls for the native Alpine panel. Changes last until the game closes."));

            AddRuntimeSlider(
                debugSection,
                "Panel Max Width",
                AlpineNativeUiConfig.RuntimePanelWidthMin,
                AlpineNativeUiConfig.RuntimePanelWidthMax,
                RuntimeUi.PanelMaxWidth,
                value => RuntimeUi.PanelMaxWidth = value,
                render);

            AddRuntimeSlider(
                debugSection,
                "Panel Max Height",
                AlpineNativeUiConfig.RuntimePanelHeightMin,
                AlpineNativeUiConfig.RuntimePanelHeightMax,
                RuntimeUi.PanelMaxHeight,
                value => RuntimeUi.PanelMaxHeight = value,
                render);

            AddRuntimeSlider(
                debugSection,
                "Panel Padding",
                AlpineNativeUiConfig.RuntimePaddingMin,
                AlpineNativeUiConfig.RuntimePaddingMax,
                RuntimeUi.PanelPadding,
                value => RuntimeUi.PanelPadding = value,
                render);

            AddRuntimeSlider(
                debugSection,
                "Button Height",
                AlpineNativeUiConfig.RuntimeButtonHeightMin,
                AlpineNativeUiConfig.RuntimeButtonHeightMax,
                RuntimeUi.ButtonHeight,
                value => RuntimeUi.ButtonHeight = value,
                render);

            AddRuntimeSlider(
                debugSection,
                "Title Font Size",
                AlpineNativeUiConfig.RuntimeFontSizeMin,
                AlpineNativeUiConfig.RuntimeFontSizeMax,
                RuntimeUi.TitleFontSize,
                value => RuntimeUi.TitleFontSize = value,
                render);

            AddRuntimeSlider(
                debugSection,
                "Panel Opacity",
                AlpineNativeUiConfig.RuntimeOpacityMin,
                AlpineNativeUiConfig.RuntimeOpacityMax,
                RuntimeUi.PanelOpacity,
                value => RuntimeUi.PanelOpacity = value,
                render);

            AddButtonRow(debugSection,
                SmallButton("Reset UI Defaults", () =>
                {
                    RuntimeUi.ResetToDefaults();
                    setStatus("Layout debug settings reset to hardcoded defaults.");
                    render();
                }));

            content.Add(debugSection);
        }

        private static void AddDonorDropdown(
            AlpineTuningMod mod,
            VisualElement content,
            TuneProfile working,
            Action render,
            Action setupChanged)
        {
            var sleds = mod.SelectableSleds.ToList();
            var options = new List<string> { "None" };

            options.AddRange(sleds.Select(s =>
                !string.IsNullOrWhiteSpace(s.displayName) ? s.displayName : s.name));

            int selected = 0;

            if (!string.IsNullOrWhiteSpace(working.donorSledKey))
            {
                int donorIndex = sleds.FindIndex(s => AlpineTuningMod.GetSledKey(s) == working.donorSledKey);
                if (donorIndex >= 0)
                    selected = donorIndex + 1;
            }

            var dropdown = Dropdown("Engine Donor", options, selected);
            SetTooltip(dropdown, "Uses another sled's stock engine and audio as the starting point for this setup.");
            dropdown.RegisterValueChangedCallback(evt =>
            {
                int index = options.IndexOf(evt.newValue);

                working.donorSledKey = index > 0 && index - 1 < sleds.Count
                    ? AlpineTuningMod.GetSledKey(sleds[index - 1])
                    : null;

                setupChanged?.Invoke();
            });

            content.Add(dropdown);
        }

        private static void AddProfileNameField(VisualElement content, TuneProfile working, Action setupChanged)
        {
            var nameField = new TextField("Setup Name")
            {
                name = "alpine-control-setup-name",
                value = working.name ?? "Current Setup"
            };

            ApplyControlStyle(nameField);
            SetTooltip(nameField, "Names the setup slot shown in your garage.");
            nameField.RegisterValueChangedCallback(evt =>
            {
                working.name = evt.newValue;
                setupChanged?.Invoke();
            });
            content.Add(nameField);
        }

        private static void AddBoostEstimate(VisualElement content, ResolvedStats stats)
        {
            if (stats == null || stats.boostTargetPsi <= 0.01f)
            {
                content.Add(MutedLabel("Estimated boost: naturally aspirated or no Alpine boost target."));
                return;
            }

            content.Add(MutedLabel($"Estimated boost: {stats.estimatedBoostPsi:F1} psi"));
            content.Add(MutedLabel($"Boost target: {stats.boostTargetPsi:F1} psi"));
            if (stats.boostLimitPsi > 0.01f)
                content.Add(MutedLabel($"Boost limit metadata: {stats.boostLimitPsi:F1} psi"));
            content.Add(MutedLabel($"Altitude compensation: {stats.altitudeCompensationPercent:F0}%"));
            content.Add(MutedLabel($"Estimated manifold pressure: {stats.estimatedManifoldPressureKpa:F0} kPa"));
        }

        private static VisualElement PerformanceBar(PerformanceStatEstimate stat)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            row.style.marginTop = AlpineNativeUiConfig.RowGap;
            row.style.minWidth = 0;
            SetTooltip(row, stat != null ? stat.tooltip : null);

            var label = new Label(stat != null ? stat.label : string.Empty);
            label.style.width = 150f;
            label.style.color = AlpineNativeUiConfig.RowTextColor;
            ApplyTextWrap(label);
            row.Add(label);

            var track = new VisualElement();
            track.style.height = 10f;
            track.style.width = 180f;
            track.style.marginRight = AlpineNativeUiConfig.InlineGap;
            track.style.backgroundColor = AlpineNativeUiConfig.ChipBackgroundColor;

            var fill = new VisualElement();
            fill.style.height = 10f;
            fill.style.width = Length.Percent(Mathf.Clamp01(stat != null ? stat.normalized01 : 0f) * 100f);
            fill.style.backgroundColor = AlpineNativeUiConfig.AccentColor;
            track.Add(fill);
            row.Add(track);

            var delta = new Label(stat != null ? stat.deltaLabel : string.Empty);
            delta.style.color = AlpineNativeUiConfig.MutedTextColor;
            delta.style.minWidth = 72f;
            ApplyTextWrap(delta);
            row.Add(delta);
            return row;
        }

        private static VisualElement BuildCurveGraph(AlpinePerformanceEstimate estimate, AlpineDisplayUnits units)
        {
            var graph = new VisualElement();
            graph.style.flexDirection = FlexDirection.Column;
            graph.style.paddingLeft = AlpineNativeUiConfig.CardPadding;
            graph.style.paddingRight = AlpineNativeUiConfig.CardPadding;
            graph.style.paddingTop = AlpineNativeUiConfig.CardPadding;
            graph.style.paddingBottom = AlpineNativeUiConfig.CardPadding;
            graph.style.backgroundColor = AlpineNativeUiConfig.CardBackgroundColor;
            graph.style.marginTop = AlpineNativeUiConfig.RowGap;
            SetTooltip(graph, "Estimated garage curve based on the current setup. It is a tuning guide, not a certified dyno result.");

            var samples = estimate != null ? estimate.curve : null;
            if (samples == null || samples.Count == 0)
            {
                graph.Add(MutedLabel("Curve unavailable until a sled setup is selected."));
                return graph;
            }

            const int columns = 44;
            const int rows = 10;
            float max = 0f;
            for (int i = 0; i < samples.Count; i++)
                max = Mathf.Max(max, samples[i].stockHorsepower, samples[i].currentHorsepower);
            max = Mathf.Max(1f, max);

            char[,] cells = new char[rows, columns];
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                    cells[y, x] = ' ';

            PlotCurve(samples, columns, rows, max, false, cells);
            PlotCurve(samples, columns, rows, max, true, cells);

            graph.Add(MutedLabel("Power Curve over RPM  (S=Stock, C=Current)"));
            for (int y = 0; y < rows; y++)
            {
                char[] line = new char[columns];
                for (int x = 0; x < columns; x++)
                    line[x] = cells[y, x];

                var label = new Label(new string(line));
                label.style.color = AlpineNativeUiConfig.RowTextColor;
                label.style.unityFontStyleAndWeight = FontStyle.Normal;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                graph.Add(label);
            }

            graph.Add(MutedLabel($"2500 rpm{new string(' ', 24)}9000 rpm"));
            return graph;
        }

        private static void PlotCurve(
            List<PerformanceCurveSample> samples,
            int columns,
            int rows,
            float max,
            bool current,
            char[,] cells)
        {
            for (int x = 0; x < columns; x++)
            {
                int sampleIndex = Mathf.Clamp(Mathf.RoundToInt(x / (float)(columns - 1) * (samples.Count - 1)), 0, samples.Count - 1);
                float value = current ? samples[sampleIndex].currentHorsepower : samples[sampleIndex].stockHorsepower;
                int y = Mathf.Clamp(rows - 1 - Mathf.RoundToInt(value / max * (rows - 1)), 0, rows - 1);
                char mark = current ? 'C' : 'S';
                cells[y, x] = cells[y, x] == ' ' ? mark : '*';
            }
        }

        private static void AddPartDropdown(
            AlpineTuningMod mod,
            VisualElement content,
            TuneProfile working,
            string category,
            Action render,
            Action setupChanged,
            string labelOverride = null)
        {
            var parts = mod.Catalog.PartsForCategory(category).ToList();
            if (parts.Count == 0)
                return;

            var options = parts.Select(p => p.name).ToList();
            string selectedPartId = working.GetPartId(category);
            int selectedIndex = Mathf.Max(0, parts.FindIndex(p => p.id == selectedPartId));

            var dropdown = Dropdown(labelOverride ?? mod.Catalog.LabelForCategory(category), options, selectedIndex);
            SetTooltip(dropdown, TooltipForCategory(category));
            dropdown.RegisterValueChangedCallback(evt =>
            {
                int index = options.IndexOf(evt.newValue);

                if (index >= 0 && index < parts.Count)
                    working.SetPartId(category, parts[index].id);

                setupChanged?.Invoke();
            });

            content.Add(dropdown);
        }

        private static DropdownField Dropdown(string label, List<string> choices, int selectedIndex)
        {
            if (choices == null)
                choices = new List<string>();

            if (choices.Count == 0)
                choices.Add(string.Empty);

            selectedIndex = Mathf.Clamp(selectedIndex, 0, choices.Count - 1);

            var dropdown = new DropdownField(label)
            {
                name = "alpine-control-" + SafeElementName(label),
                choices = choices,
                value = choices[selectedIndex]
            };

            ApplyControlStyle(dropdown);
            return dropdown;
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
            SetTooltip(slider, tooltip);

            slider.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp(evt.newValue, min, max);
                changed(clamped);
                slider.label = $"{label}: {ValueText(clamped)}";
                setupChanged?.Invoke();
            });

            content.Add(slider);
        }

        private static Foldout BuildPartDetailsFoldout(AlpineTuningMod mod, TuneProfile working)
        {
            var foldout = new Foldout
            {
                text = "Changed Parts",
                value = false
            };

            foldout.style.marginTop = AlpineNativeUiConfig.DefaultButtonRowMarginTop;

            if (mod == null || working == null)
            {
                foldout.Add(MutedLabel("No setup selected."));
                return foldout;
            }

            bool hasChangedPart = false;
            foreach (string category in PartCatalog.OrderedCategories)
            {
                if (!AlpineNativeUiConfig.ShowNativeAccessoriesCategory &&
                    string.Equals(category, PartCatalog.Accessories, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string partId = working.GetPartId(category);
                string defaultPartId = mod.Catalog.DefaultPartId(category);
                if (string.Equals(partId, defaultPartId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var part = mod.Catalog.Find(partId) ?? mod.Catalog.Find(mod.Catalog.DefaultPartId(category));
                if (part == null)
                    continue;

                hasChangedPart = true;
                foldout.Add(MutedLabel($"{mod.Catalog.LabelForCategory(category)}: {part.name}"));
                if (!string.IsNullOrWhiteSpace(part.description))
                    foldout.Add(MutedLabel(part.description));
            }

            if (!hasChangedPart)
                foldout.Add(MutedLabel("All visible part categories are stock."));

            return foldout;
        }

        private static string TooltipForCategory(string category)
        {
            switch (category)
            {
                case PartCatalog.EngineCore:
                    return "Changes the main engine package and estimated output.";
                case PartCatalog.EnginePiston:
                    return "Changes engine response and rotating mass.";
                case PartCatalog.EngineCrank:
                    return "Changes how smoothly and quickly the engine changes RPM.";
                case PartCatalog.Turbo:
                    return "Adds boost and altitude help. More boost increases power, heat, and belt load.";
                case PartCatalog.Intake:
                    return "Changes breathing and throttle response with small weight changes.";
                case PartCatalog.Clutch:
                    return "Changes engagement and shift behavior. Higher RPM setups feel more aggressive.";
                case PartCatalog.ClutchWeights:
                    return "Changes clutch weight feel. Lighter feels quicker; heavier feels calmer.";
                case PartCatalog.RatioFeel:
                    return "Approximates shorter or taller drive feel through power delivery and clutch response.";
                case PartCatalog.Track:
                    return "Changes paddle, bite, flotation, and track weight. Visual length depends on game model support.";
                case PartCatalog.TrackLimiter:
                    return "Changes weight transfer. Tight setups reduce lift; loose setups feel more playful.";
                case PartCatalog.RearShock:
                    return "Changes rear damping feel and stability.";
                case PartCatalog.RearSpring:
                    return "Changes rear support feel for rider weight, mountain use, or race response.";
                case PartCatalog.Suspension:
                    return "Changes balance, center of mass, and handling personality.";
                case PartCatalog.Chassis:
                    return "Changes sled weight and chassis balance.";
                case PartCatalog.Skis:
                    return "Changes front ski width and bite. Wider is stable; narrower turns tighter.";
                case PartCatalog.HeadlightColor:
                    return "Changes the visible color of your headlights.";
                case PartCatalog.HeadlightBrightness:
                    return "Changes headlight intensity and reach.";
                case PartCatalog.HeadlightBeam:
                    return "Changes beam width and distance.";
                case PartCatalog.HeadlightAim:
                    return "Aims headlights slightly up or down.";
                case PartCatalog.Accessories:
                    return "Changes native visual equipment only when explicitly selected.";
                default:
                    return null;
            }
        }

        private static Label CreateTooltipOverlay()
        {
            var tooltip = new Label
            {
                name = "alpine-tooltip-overlay",
                pickingMode = PickingMode.Ignore,
                text = string.Empty
            };

            ApplyTooltipOverlayStyle(tooltip);
            HideTooltip(tooltip);
            return tooltip;
        }

        private static void ApplyTooltipOverlayStyle(Label tooltip)
        {
            if (tooltip == null)
                return;

            tooltip.pickingMode = PickingMode.Ignore;

            // Absolute overlay: does not participate in flex layout and cannot resize the menu.
            tooltip.style.position = Position.Absolute;
            tooltip.style.display = DisplayStyle.None;

            tooltip.style.left = 0f;
            tooltip.style.top = 0f;

            tooltip.style.maxWidth = 340f;
            tooltip.style.minWidth = 120f;

            tooltip.style.paddingLeft = 8f;
            tooltip.style.paddingRight = 8f;
            tooltip.style.paddingTop = 6f;
            tooltip.style.paddingBottom = 6f;

            tooltip.style.backgroundColor = new Color(0.04f, 0.05f, 0.06f, 0.96f);
            tooltip.style.color = AlpineNativeUiConfig.RowTextColor;

            tooltip.style.fontSize = Mathf.Max(10f, RuntimeUi.TitleFontSize - 5f);
            tooltip.style.whiteSpace = WhiteSpace.Normal;

            tooltip.style.flexGrow = 0;
            tooltip.style.flexShrink = 0;

            // Added last under root, so it renders above the panel without needing to
            // steal picking/hover events.
        }

        private static void SetTooltip(VisualElement element, string text)
        {
            if (element == null)
                return;

            element.tooltip = string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : text.Trim();
        }

        private static void AttachTooltipFeedback(VisualElement root, Label tooltip)
        {
            if (root == null || tooltip == null)
                return;

            Vector2 lastMousePosition = new Vector2(18f, 18f);

            Action<VisualElement, Vector2> showForElement = (element, position) =>
            {
                string text = FindTooltip(element);

                if (string.IsNullOrWhiteSpace(text))
                {
                    HideTooltip(tooltip);
                    return;
                }

                ShowTooltip(tooltip, root, text, position);
            };

            root.RegisterCallback<MouseMoveEvent>(evt =>
            {
                lastMousePosition = evt.mousePosition;
                showForElement(evt.target as VisualElement, lastMousePosition);
            }, TrickleDown.TrickleDown);

            root.RegisterCallback<MouseOverEvent>(evt =>
            {
                lastMousePosition = evt.mousePosition;
                showForElement(evt.target as VisualElement, lastMousePosition);
            }, TrickleDown.TrickleDown);

            root.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                HideTooltip(tooltip);
            });

            root.RegisterCallback<FocusInEvent>(evt =>
            {
                var element = evt.target as VisualElement;
                if (element == null)
                {
                    HideTooltip(tooltip);
                    return;
                }

                string text = FindTooltip(element);
                if (string.IsNullOrWhiteSpace(text))
                {
                    HideTooltip(tooltip);
                    return;
                }

                Vector2 focusPosition = TooltipPositionForElement(root, element);
                ShowTooltip(tooltip, root, text, focusPosition);
            }, TrickleDown.TrickleDown);

            root.RegisterCallback<FocusOutEvent>(_ =>
            {
                HideTooltip(tooltip);
            }, TrickleDown.TrickleDown);

            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                HideTooltip(tooltip);
            });
        }

        private static void ShowTooltip(Label tooltip, VisualElement root, string text, Vector2 desiredPosition)
        {
            if (tooltip == null || root == null || string.IsNullOrWhiteSpace(text))
            {
                HideTooltip(tooltip);
                return;
            }

            tooltip.text = text.Trim();
            tooltip.style.display = DisplayStyle.Flex;

            // Re-apply these defensively in case native USS/classes touch labels globally.
            tooltip.pickingMode = PickingMode.Ignore;
            tooltip.style.position = Position.Absolute;
            tooltip.style.flexGrow = 0;
            tooltip.style.flexShrink = 0;

            PositionTooltip(tooltip, root, desiredPosition);
        }

        private static void HideTooltip(Label tooltip)
        {
            if (tooltip == null)
                return;

            tooltip.text = string.Empty;
            tooltip.style.display = DisplayStyle.None;
        }

        private static void PositionTooltip(Label tooltip, VisualElement root, Vector2 desiredPosition)
        {
            if (tooltip == null || root == null)
                return;

            const float offsetX = 14f;
            const float offsetY = 18f;
            const float safeMargin = 8f;

            float x = desiredPosition.x + offsetX;
            float y = desiredPosition.y + offsetY;

            Rect rootBounds = root.layout;
            Rect tooltipBounds = tooltip.layout;

            float tooltipWidth = tooltipBounds.width > 1f ? tooltipBounds.width : 300f;
            float tooltipHeight = tooltipBounds.height > 1f ? tooltipBounds.height : 48f;

            float maxX = Mathf.Max(safeMargin, rootBounds.width - tooltipWidth - safeMargin);
            float maxY = Mathf.Max(safeMargin, rootBounds.height - tooltipHeight - safeMargin);

            x = Mathf.Clamp(x, safeMargin, maxX);
            y = Mathf.Clamp(y, safeMargin, maxY);

            tooltip.style.left = x;
            tooltip.style.top = y;

            // First layout pass may not know tooltip size yet. Re-clamp next frame.
            tooltip.schedule.Execute(() =>
            {
                if (tooltip == null ||
                    root == null ||
                    tooltip.panel == null ||
                    tooltip.style.display == DisplayStyle.None)
                {
                    return;
                }

                Rect updatedRootBounds = root.layout;
                Rect updatedTooltipBounds = tooltip.layout;

                float updatedWidth = updatedTooltipBounds.width > 1f ? updatedTooltipBounds.width : tooltipWidth;
                float updatedHeight = updatedTooltipBounds.height > 1f ? updatedTooltipBounds.height : tooltipHeight;

                float updatedMaxX = Mathf.Max(safeMargin, updatedRootBounds.width - updatedWidth - safeMargin);
                float updatedMaxY = Mathf.Max(safeMargin, updatedRootBounds.height - updatedHeight - safeMargin);

                float clampedX = Mathf.Clamp(x, safeMargin, updatedMaxX);
                float clampedY = Mathf.Clamp(y, safeMargin, updatedMaxY);

                tooltip.style.left = clampedX;
                tooltip.style.top = clampedY;
            });
        }

        private static Vector2 TooltipPositionForElement(VisualElement root, VisualElement element)
        {
            if (root == null || element == null)
                return new Vector2(18f, 18f);

            try
            {
                Rect rootWorld = root.worldBound;
                Rect elementWorld = element.worldBound;

                float x = elementWorld.xMax - rootWorld.xMin + 8f;
                float y = elementWorld.yMin - rootWorld.yMin + 4f;

                return new Vector2(x, y);
            }
            catch
            {
                return new Vector2(18f, 18f);
            }
        }

        private static string FindTooltip(VisualElement element)
        {
            while (element != null)
            {
                if (!string.IsNullOrWhiteSpace(element.tooltip))
                    return element.tooltip;

                element = element.parent;
            }

            return null;
        }
        private static string FocusedElementName(VisualElement root)
        {
            try
            {
                var focused = root != null && root.panel != null
                    ? root.panel.focusController.focusedElement as VisualElement
                    : null;

                return focused != null && !string.IsNullOrWhiteSpace(focused.name)
                    ? focused.name
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static void RestoreUiState(
            VisualElement root,
            ScrollView content,
            Dictionary<string, Vector2> scrollByTab,
            string activeTab,
            string focusedElementName)
        {
            if (root == null || content == null)
                return;

            root.schedule.Execute(() =>
            {
                if (scrollByTab != null && !string.IsNullOrWhiteSpace(activeTab) &&
                    scrollByTab.TryGetValue(activeTab, out var offset))
                {
                    content.scrollOffset = offset;
                }

                if (!string.IsNullOrWhiteSpace(focusedElementName))
                {
                    var focusTarget = root.Q<VisualElement>(focusedElementName);
                    focusTarget?.Focus();
                }
            });
        }

        private static void LogUiRefresh(string reason, VehicleScriptableObject target, string activeTab)
        {
            if (Time.unscaledTime < _lastUiRefreshLogTime + 0.50f)
                return;

            _lastUiRefreshLogTime = Time.unscaledTime;
            MelonLogger.Msg(
                $"Alpine UI refreshed: {DisplayOrUnknown(reason)}; " +
                $"tab={DisplayOrUnknown(activeTab)}; " +
                $"sled={(target != null ? AlpineTuningMod.GetSledDisplayName(target) : "none")}.");
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

        private static string FormatHeadlightBinding(AlpineUserSettings settings)
        {
            if (settings == null || !settings.headlightToggleEnabled)
                return "Off";

            var values = new List<string>();
            if (!string.IsNullOrWhiteSpace(settings.headlightKeyboardKey))
                values.Add(settings.headlightKeyboardKey);
            if (!string.IsNullOrWhiteSpace(settings.headlightControllerButton))
                values.Add(settings.headlightControllerButton);

            return values.Count == 0 ? "Not bound" : string.Join(" / ", values.ToArray());
        }

        private static bool HasConfiguredHeadlightBinding(AlpineUserSettings settings)
        {
            return settings != null &&
                   (!string.IsNullOrWhiteSpace(settings.headlightKeyboardKey) ||
                    !string.IsNullOrWhiteSpace(settings.headlightControllerButton));
        }

        private static string FormatSingleHeadlightBinding(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Not set" : value;
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

        private static VisualElement Section(string title)
        {
            var section = new VisualElement();
            section.style.flexDirection = FlexDirection.Column;
            section.style.marginTop = AlpineNativeUiConfig.SectionGap;
            section.Add(SectionTitle(title));
            return section;
        }

        private static VisualElement Card(bool selected)
        {
            var card = new VisualElement();
            ApplyCardStyle(card, selected);
            return card;
        }

        private static VisualElement StatsPreview(AlpineTuningMod mod, VehicleScriptableObject sled, ResolvedStats stats, bool requiresReload)
        {
            var row = new VisualElement();
            ApplyStatRowStyle(row);

            if (stats != null)
            {
                var settings = mod != null ? mod.Settings : new AlpineUserSettings();
                var defaults = mod != null && sled != null ? mod.Store.GetDefaults(AlpineTuningMod.GetSledKey(sled)) : null;
                AddStatChip(row, "Engine Output", UnitConversion.FormatPower(stats.horsePower, settings.units));
                AddStatChip(row, "Drive Response", $"{stats.powerFactor:F2}");
                AddStatChip(row, "Paddle", TrackSpecResolver.FormatPaddleHeight(stats.lugHeight));
                AddStatChip(row, "Track Bite", FormatPercentDelta(stats.friction, defaults != null ? defaults.friction : stats.friction));
                AddStatChip(row, "Weight", UnitConversion.FormatWeight(stats.weight, settings.units));
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
                SetTooltip(chip, "This sled is not currently spawned or needs a rebuild. Your setup will equip automatically when you ride it.");
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
            label.style.fontSize = Mathf.Max(12f, RuntimeUi.TitleFontSize - 3f);
            ApplyTextWrap(label);
            return label;
        }

        private static Label CardTitle(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.style.color = AlpineNativeUiConfig.TitleTextColor;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = Mathf.Max(12f, RuntimeUi.TitleFontSize - 2f);
            label.style.marginRight = AlpineNativeUiConfig.InlineGap;
            label.style.flexShrink = 1;
            ApplyTextWrap(label);
            return label;
        }

        private static string DisplayOrUnknown(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
                return "unknown size";

            const double kib = 1024d;
            const double mib = kib * 1024d;

            if (bytes >= mib)
                return (bytes / mib).ToString("F1") + " MiB";

            if (bytes >= kib)
                return (bytes / kib).ToString("F1") + " KiB";

            return bytes + " B";
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

        private static void AddRuntimeSlider(
            VisualElement content,
            string label,
            float min,
            float max,
            float value,
            Action<float> changed,
            Action render)
        {
            var slider = new Slider(label, min, max)
            {
                value = Mathf.Clamp(value, min, max)
            };

            slider.label = $"{label}: {slider.value:F2}";
            ApplyControlStyle(slider);

            slider.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp(evt.newValue, min, max);
                changed(clamped);
                slider.label = $"{label}: {clamped:F2}";
                render();
            });

            content.Add(slider);
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

        private static Button TabButton(string text, Action clicked)
        {
            var button = SmallButton(text, clicked);
            button.style.minWidth = 84f;
            return button;
        }

        private static Label MutedLabel(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.style.color = AlpineNativeUiConfig.MutedTextColor;
            label.style.marginTop = AlpineNativeUiConfig.DefaultMutedLabelMarginTop;
            label.style.flexShrink = 1;
            ApplyTextWrap(label);
            return label;
        }

        private static void ApplyRootStyle(VisualElement root, AlpineUiSurfaceMode mode)
        {
            if (root == null)
                return;

            root.style.flexShrink = 1;
            root.style.minWidth = 0;
            root.style.marginTop = AlpineNativeUiConfig.DefaultRootMarginTop;
            root.style.marginBottom = AlpineNativeUiConfig.DefaultRootMarginBottom;
            root.style.marginLeft = AlpineNativeUiConfig.DefaultRootMarginLeft;
            root.style.marginRight = AlpineNativeUiConfig.DefaultRootMarginRight;

            if (mode == AlpineUiSurfaceMode.GarageTab)
            {
                root.style.flexGrow = 1;
                root.style.alignSelf = Align.Stretch;
                root.style.width = Length.Percent(AlpineNativeUiConfig.DefaultPanelWidthPercent);
                root.style.minWidth = AlpineNativeUiConfig.DefaultPanelMinWidth;
                root.style.maxWidth = RuntimeUi.PanelMaxWidth;
                root.style.maxHeight = StyleKeyword.None;
                return;
            }

            root.style.flexGrow = 0;
            root.style.alignSelf = Align.Stretch;
            root.style.width = Length.Percent(100f);
            root.style.maxWidth = StyleKeyword.None;
            root.style.maxHeight = AlpineNativeUiConfig.DefaultInlineSurfaceMaxHeight;
        }

        private static void ApplyPanelStyle(VisualElement panel, AlpineUiSurfaceMode mode)
        {
            if (panel == null)
                return;

            Color panelColor = AlpineNativeUiConfig.PanelBackgroundColor;
            panelColor.a = RuntimeUi.PanelOpacity;
            float padding = mode == AlpineUiSurfaceMode.GarageTab
                ? RuntimeUi.PanelPadding
                : Mathf.Min(RuntimeUi.PanelPadding, 8f);

            panel.style.paddingTop = padding;
            panel.style.paddingBottom = padding;
            panel.style.paddingLeft = padding;
            panel.style.paddingRight = padding;
            panel.style.marginTop = AlpineNativeUiConfig.DefaultPanelMarginTop;
            panel.style.backgroundColor = panelColor;
            panel.style.flexGrow = mode == AlpineUiSurfaceMode.GarageTab ? 1 : 0;
            panel.style.flexShrink = 1;
            panel.style.minWidth = 0;

            if (mode == AlpineUiSurfaceMode.GarageTab)
                panel.style.maxHeight = StyleKeyword.None;
            else
                panel.style.maxHeight = AlpineNativeUiConfig.DefaultInlineSurfaceMaxHeight;
        }

        private static void ApplyTitleStyle(Label title)
        {
            if (title == null)
                return;

            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = RuntimeUi.TitleFontSize;
            title.style.color = AlpineNativeUiConfig.TitleTextColor;
            title.style.marginBottom = AlpineNativeUiConfig.RowGap;
            title.style.flexShrink = 1;
            ApplyTextWrap(title);
        }

        private static void ApplyStatRowStyle(VisualElement stats)
        {
            if (stats == null)
                return;

            stats.style.flexDirection = FlexDirection.Row;
            stats.style.flexWrap = Wrap.Wrap;
            stats.style.marginBottom = AlpineNativeUiConfig.SectionGap;
            stats.style.flexShrink = 1;
            stats.style.minWidth = 0;
        }

        private static void ApplyTabRowStyle(VisualElement tabs)
        {
            if (tabs == null)
                return;

            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.flexWrap = Wrap.Wrap;
            tabs.style.marginBottom = AlpineNativeUiConfig.RowGap;
            tabs.style.flexShrink = 1;
            tabs.style.minWidth = 0;
        }

        private static void ApplyActionRowStyle(VisualElement row, VisualElement primaryActions, VisualElement dangerActions)
        {
            if (row != null)
            {
                row.style.flexDirection = FlexDirection.Column;
                row.style.marginBottom = AlpineNativeUiConfig.RowGap;
                row.style.minWidth = 0;
                row.style.flexShrink = 1;
            }

            if (primaryActions != null)
            {
                primaryActions.style.flexDirection = FlexDirection.Row;
                primaryActions.style.flexWrap = Wrap.Wrap;
                primaryActions.style.flexGrow = 1;
                primaryActions.style.flexShrink = 1;
                primaryActions.style.minWidth = 0;
            }

            if (dangerActions != null)
            {
                dangerActions.style.flexDirection = FlexDirection.Row;
                dangerActions.style.flexWrap = Wrap.Wrap;
                dangerActions.style.justifyContent = Justify.FlexEnd;
                dangerActions.style.marginTop = AlpineNativeUiConfig.RowGap;
                dangerActions.style.marginLeft = 0;
                dangerActions.style.flexShrink = 1;
                dangerActions.style.minWidth = 0;
            }
        }

        private static void ApplyTabsStyle(VisualElement tabs)
        {
            if (tabs == null)
                return;

            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.flexWrap = Wrap.Wrap;
            tabs.style.marginBottom = AlpineNativeUiConfig.DefaultTabsMarginBottom;
        }

        private static void ApplyStatusStyle(Label status)
        {
            if (status == null)
                return;

            status.style.marginTop = AlpineNativeUiConfig.DefaultStatusMarginTop;
            status.style.color = AlpineNativeUiConfig.StatusTextColor;
            status.style.flexShrink = 1;
            ApplyTextWrap(status);
        }

        private static void ApplyContentStyle(ScrollView content, AlpineUiSurfaceMode mode)
        {
            if (content == null)
                return;

            content.style.maxHeight = mode == AlpineUiSurfaceMode.GarageTab
                ? RuntimeUi.PanelMaxHeight
                : AlpineNativeUiConfig.DefaultInlinePanelMaxHeight;
            content.style.flexGrow = 1;
            content.style.flexShrink = 1;
            content.style.minWidth = 0;
        }

        private static void ApplyDiagnosticsStyle(Foldout diagnostics)
        {
            if (diagnostics == null)
                return;

            diagnostics.style.marginTop = AlpineNativeUiConfig.FooterGap;
        }

        private static void ApplyControlStyle(VisualElement control)
        {
            if (control == null)
                return;

            control.style.marginTop = AlpineNativeUiConfig.RowGap;
            control.style.flexGrow = 1;
            control.style.flexShrink = 1;
            control.style.minWidth = 0;
            control.style.maxWidth = RuntimeUi.PanelMaxWidth;
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
            button.style.height = RuntimeUi.ButtonHeight;
            button.style.minWidth = 72f;
            button.style.flexShrink = 1;
            button.style.backgroundColor = AlpineNativeUiConfig.ButtonBackgroundColor;
            button.style.color = AlpineNativeUiConfig.RowTextColor;
        }

        private static void ApplyNativeAttachedButtonStyle(Button button)
        {
            if (button == null)
                return;

            button.style.marginRight = AlpineNativeUiConfig.DefaultButtonMarginRight;
            button.style.marginTop = AlpineNativeUiConfig.DefaultButtonMarginTop;
            button.style.marginBottom = AlpineNativeUiConfig.DefaultButtonMarginBottom;
            button.style.flexShrink = 1;
        }

        private static void ApplyFallbackAttachedButtonStyle(Button button)
        {
            if (button == null)
                return;

            button.style.backgroundColor = AlpineNativeUiConfig.ButtonBackgroundColor;
            button.style.color = AlpineNativeUiConfig.RowTextColor;
            button.style.minHeight = RuntimeUi.ButtonHeight;
        }

        private static void ApplyCardStyle(VisualElement card, bool selected)
        {
            if (card == null)
                return;

            card.style.flexDirection = FlexDirection.Column;
            card.style.marginTop = AlpineNativeUiConfig.CardGap;
            card.style.paddingLeft = AlpineNativeUiConfig.CardPadding;
            card.style.paddingRight = AlpineNativeUiConfig.CardPadding;
            card.style.paddingTop = AlpineNativeUiConfig.CardPadding;
            card.style.paddingBottom = AlpineNativeUiConfig.CardPadding;
            card.style.backgroundColor = selected
                ? AlpineNativeUiConfig.SelectedCardBackgroundColor
                : AlpineNativeUiConfig.CardBackgroundColor;
            card.style.flexShrink = 1;
            card.style.minWidth = 0;
        }

        private static void ApplyTabButtonStates(Dictionary<string, Button> buttons, string activeTab)
        {
            if (buttons == null)
                return;

            foreach (var pair in buttons)
            {
                Button button = pair.Value;
                if (button == null)
                    continue;

                bool active = string.Equals(pair.Key, activeTab, StringComparison.OrdinalIgnoreCase);
                button.EnableInClassList("open", active);
                button.style.backgroundColor = active
                    ? AlpineNativeUiConfig.ActiveButtonBackgroundColor
                    : AlpineNativeUiConfig.ButtonBackgroundColor;
                button.style.color = active
                    ? AlpineNativeUiConfig.ActiveButtonTextColor
                    : AlpineNativeUiConfig.RowTextColor;
                button.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
            }
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

        private static bool TryRegisterNativeTab(
            object nativeTabManager,
            VisualElement tabPanel,
            Button tabButton,
            int insertIndex,
            Action selected,
            out int nativeIndex)
        {
            return SleddersGameBindings.TryRegisterNativeTab(
                nativeTabManager,
                tabPanel,
                tabButton,
                insertIndex,
                selected,
                out nativeIndex);
        }

        private static void SelectNativeTab(object nativeTabManager, int index)
        {
            SleddersGameBindings.SelectNativeTab(nativeTabManager, index);
        }

        private static void SelectTabWithoutNativeManager(
            VisualElement tabs,
            VisualElement tabButtons,
            VisualElement selectedPanel,
            Button selectedButton)
        {
            if (tabs == null || tabButtons == null)
                return;

            for (int i = 0; i < tabs.childCount; i++)
                tabs[i].style.display = tabs[i] == selectedPanel ? DisplayStyle.Flex : DisplayStyle.None;

            for (int i = 0; i < tabButtons.childCount; i++)
                tabButtons[i].EnableInClassList("open", tabButtons[i] == selectedButton);
        }

        private static Button LastButtonChild(VisualElement parent)
        {
            if (parent == null)
                return null;

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                if (parent[i] is Button button)
                    return button;
            }

            return null;
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

        private static void CopyClasses(VisualElement source, VisualElement target)
        {
            if (source == null || target == null)
                return;

            foreach (string className in source.GetClasses())
                target.AddToClassList(className);
        }

    }
}
