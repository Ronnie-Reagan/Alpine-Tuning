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
        public const string ModTitle = "ALPINE TUNING 2.0";
        public const string BuildTabLabel = "Build";
        public const string TrackTabLabel = "Track";
        public const string EngineTabLabel = "Engine";
        public const string ClutchTabLabel = "Clutch";
        public const string SetupTabLabel = "Setup";
        public const string LightsTabLabel = "Lights";
        public const string FineTuneTabLabel = "Fine Tune";
        public const string LibraryTabLabel = "Library";
        public const string ShareTabLabel = "Share";
        public const string UiSettingsTabLabel = "Layout Debug";
        public const string RefreshSledLabel = "Refresh Sled";

        // Feature switches.
        public const bool EnableRuntimeUiSettingsTab = true;
        public static readonly bool ShowRefreshSledButton = true;
        public const bool ShowNativeAccessoriesCategory = false;
        public const bool EnablePeerReplicationToggle = false;

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
        public const string NoSavedProfilesText = "No saved profiles for this sled yet.";
        public const string NoSharedTunesText = "No shared tunes discovered yet. Both players need the mod.";
        public const string FineTuneHelpText = "Fine tune trims are intentionally clamped so shared builds stay sane.";
        public const string ReloadRequiredHintText = "Reload required";
        public const string RefreshedSledText = "Refreshed current sled context.";
        public const string PreviewUpdatedText = "Preview updated.";
        public const string FactoryDefaultsRestoredText = "Factory defaults restored.";
        public const string ActiveProfileSavedText = "Saved as active profile.";
        public const string AppliedSavedActiveText = "Applied and saved as active profile.";
        public const string AppliedSavedReloadedText = "Applied, saved, and reloaded if required.";
        public const string InstalledBuildText = "Installed and saved build.";
        public const string InstalledRebuiltBuildText = "Installed, saved, and requested sled rebuild.";
        public const string FineTuneAppliedText = "Fine tune applied and saved.";
        public const string PublishedTuneText = "Published tune summary to discovered lobby peers.";
        public const string PeerHelloText = "Sent peer discovery hello.";
        public const string PeerReplicationUnavailableText = "Replicate Peers Coming Soon!";
        public const string SharedPayloadMissingText = "Shared payload not available.";
        public const string ApplyFailedText = "Apply failed.";
        public const string SaveFailedText = "Save failed.";
        public const string ResetFailedText = "Factory reset failed.";
        public const string SharingUnavailableText = "Peer sharing unavailable.";
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
        private static bool _peerReplicationEnabled;
        private static int _attachedMenuCount;

        public static bool HasAttachedMenus => HasAttachedNativeUiRoot();

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

            VisualElement tabsButtons = menuRoot.Q<VisualElement>(AlpineNativeUiConfig.GarageTabsButtonsName);
            VisualElement tabs = menuRoot.Q<VisualElement>(AlpineNativeUiConfig.GarageTabsName);

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

            object nativeTabManager = SleddersGameBindings.GetFieldValue<object>(
                controller,
                AlpineNativeUiConfig.VehicleNativeTabManagerFieldName);

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
            var target = mod.ResolveTargetSled(menuContext);
            var working = target != null ? mod.CreateWorkingProfile(target) : null;
            string activeTab = mode == AlpineUiSurfaceMode.PauseInline
                ? AlpineNativeUiConfig.LibraryTabLabel
                : AlpineNativeUiConfig.TrackTabLabel;
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
            var diagnostics = new Foldout
            {
                text = "Diagnostics",
                value = false
            };
            var tabButtons = new Dictionary<string, Button>();
            Button primaryInstallButton = null;
            Button refreshSledButton = null;

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

            Action<string> setStatus = message =>
            {
                status.text = message ?? string.Empty;
            };

            Action<TuneProfile> setWorking = profile =>
            {
                working = profile;
                pendingDeleteProfileId = null;
                factoryResetArmed = false;
            };

            Action refreshTarget = () =>
            {
                var refreshed = mod.ResolveTargetSled(menuContext);
                if (refreshed != null && refreshed != target)
                {
                    target = refreshed;
                    working = mod.CreateWorkingProfile(target);
                    pendingDeleteProfileId = null;
                    factoryResetArmed = false;
                }
            };

            render = () =>
            {
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
                    if (primaryInstallButton != null)
                        primaryInstallButton.SetEnabled(false);

                    if (refreshSledButton != null)
                        refreshSledButton.style.display = DisplayStyle.Flex;

                    title.text = $"{AlpineNativeUiConfig.ModTitle} - No sled detected";
                    content.Add(new Label($"No sled detected for {source}."));
                    diagnostics.Add(MutedLabel($"Source: {source}"));
                    return;
                }

                mod.PreviewProfile(working, target);
                UpdateSummary(title, stats, diagnostics, source, target, working);

                if (primaryInstallButton != null)
                {
                    primaryInstallButton.SetEnabled(true);
                    primaryInstallButton.text = working.requiresReload ? "Install & Rebuild" : "Install";
                }

                if (refreshSledButton != null)
                    refreshSledButton.style.display = ShouldShowRefreshSledButton(target, activeTab)
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;

                switch (activeTab)
                {
                    case AlpineNativeUiConfig.EngineTabLabel:
                        BuildEngineTab(mod, content, target, working, render);
                        break;

                    case AlpineNativeUiConfig.ClutchTabLabel:
                        BuildClutchTab(mod, content, target, working, render);
                        break;

                    case AlpineNativeUiConfig.SetupTabLabel:
                    case AlpineNativeUiConfig.FineTuneTabLabel:
                        BuildSetupTab(mod, content, target, working, render, setStatus);
                        break;

                    case AlpineNativeUiConfig.LightsTabLabel:
                        BuildLightsTab(mod, content, target, working, render);
                        break;

                    case AlpineNativeUiConfig.LibraryTabLabel:
                        BuildLibraryTab(mod, content, target, working, setWorking, render, setStatus,
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

                    case AlpineNativeUiConfig.UiSettingsTabLabel:
                        BuildUiSettingsTab(content, render, setStatus);
                        break;

                    case AlpineNativeUiConfig.TrackTabLabel:
                    default:
                        BuildTrackTab(mod, content, target, working, render);
                        break;
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
                    render();
                });
                tabButtons[captured] = tabButton;
                tabRow.Add(tabButton);
            }

            primaryInstallButton = PrimaryButton("Install", () =>
            {
                if (target == null || working == null)
                    return;

                factoryResetArmed = false;
                pendingDeleteProfileId = null;

                string message;
                bool applied = mod.ApplyProfile(working, target, true, working.requiresReload, out message);
                setStatus(applied
                    ? (string.IsNullOrWhiteSpace(message)
                        ? (working.requiresReload
                            ? AlpineNativeUiConfig.InstalledRebuiltBuildText
                            : AlpineNativeUiConfig.InstalledBuildText)
                        : message)
                    : (string.IsNullOrWhiteSpace(message) ? AlpineNativeUiConfig.ApplyFailedText : message));
                render();
            });
            primaryActions.Add(primaryInstallButton);

            primaryActions.Add(SmallButton("Save Build", () =>
            {
                if (target == null || working == null)
                    return;

                factoryResetArmed = false;
                pendingDeleteProfileId = null;

                setStatus(mod.SaveProfile(working, target, true)
                    ? AlpineNativeUiConfig.ActiveProfileSavedText
                    : AlpineNativeUiConfig.SaveFailedText);
                render();
            }));

            if (AlpineNativeUiConfig.ShowRefreshSledButton)
            {
                refreshSledButton = SmallButton(AlpineNativeUiConfig.RefreshSledLabel, () =>
                {
                    factoryResetArmed = false;
                    pendingDeleteProfileId = null;
                    target = mod.ResolveTargetSled(menuContext);
                    working = target != null ? mod.CreateWorkingProfile(target) : null;
                    setStatus(AlpineNativeUiConfig.RefreshedSledText);
                    render();
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

            yield return AlpineNativeUiConfig.TrackTabLabel;
            yield return AlpineNativeUiConfig.EngineTabLabel;
            yield return AlpineNativeUiConfig.ClutchTabLabel;
            yield return AlpineNativeUiConfig.SetupTabLabel;
            if (SleddersGameBindings.HeadlightRuntimeBindingAvailable)
                yield return AlpineNativeUiConfig.LightsTabLabel;
            yield return AlpineNativeUiConfig.LibraryTabLabel;
            yield return AlpineNativeUiConfig.ShareTabLabel;

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
            Label title,
            VisualElement statsRow,
            Foldout diagnostics,
            string source,
            VehicleScriptableObject sled,
            TuneProfile profile)
        {
            string sledName = !string.IsNullOrWhiteSpace(sled.displayName)
                ? sled.displayName
                : sled.name;

            if (title != null)
                title.text = $"{AlpineNativeUiConfig.ModTitle} - {sledName}";

            var stats = profile.resolvedStats;

            if (statsRow != null)
            {
                statsRow.Clear();
                AddStatChip(statsRow, "HP", $"{stats.horsePower:F1}");
                AddStatChip(statsRow, "Power", $"{stats.powerFactor:F2}");
                AddStatChip(statsRow, "Paddle", TrackSpecResolver.FormatPaddleHeight(stats.lugHeight));
                AddStatChip(statsRow, "Friction", $"{stats.friction:F2}");
                AddStatChip(statsRow, "Weight", $"{stats.weight:F1}");

                if (profile.requiresReload)
                    AddStatusChip(statsRow, AlpineNativeUiConfig.ReloadRequiredHintText);
            }

            if (diagnostics != null)
            {
                diagnostics.Clear();
                diagnostics.Add(MutedLabel($"Source: {source}"));
                diagnostics.Add(MutedLabel($"Alpine {AlpineConstants.ModVersion}"));
                diagnostics.Add(MutedLabel($"Catalog {AlpineConstants.CatalogVersion}"));
                diagnostics.Add(MutedLabel($"Schema {AlpineConstants.SchemaVersion}"));
            }
        }

        private static void BuildTrackTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action render,
            Action<string> setStatus = null)
        {
            var trackSection = Section("Track");
            AddPartDropdown(mod, trackSection, working, PartCatalog.Track, render, "Paddle / Track Package");

            if (working.resolvedStats != null)
                trackSection.Add(MutedLabel($"Resolved paddle height: {TrackSpecResolver.FormatPaddleHeight(working.resolvedStats.lugHeight)}"));

            content.Add(trackSection);

            var setupSection = Section("Track Setup");
            AddPartDropdown(mod, setupSection, working, PartCatalog.TrackLimiter, render, "Limiter Strap Setup");
            AddPartDropdown(mod, setupSection, working, PartCatalog.RearShock, render, "Rear Shock Setup");
            AddPartDropdown(mod, setupSection, working, PartCatalog.RearSpring, render, "Rear Spring Setup");
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
            Action render)
        {
            var profileSection = Section("Build");
            AddProfileNameField(profileSection, working);
            AddDonorDropdown(mod, profileSection, working, render);
            content.Add(profileSection);

            var engineSection = Section("Engine");
            AddPartDropdown(mod, engineSection, working, PartCatalog.EngineCore, render, "Block / Engine Package");
            AddPartDropdown(mod, engineSection, working, PartCatalog.EnginePiston, render);
            AddPartDropdown(mod, engineSection, working, PartCatalog.EngineCrank, render);
            AddPartDropdown(mod, engineSection, working, PartCatalog.Intake, render, "Intake / Exhaust");
            AddPartDropdown(mod, engineSection, working, PartCatalog.Turbo, render, "Turbo / Induction");
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
            Action render)
        {
            var clutchSection = Section("Clutch");
            AddPartDropdown(mod, clutchSection, working, PartCatalog.Clutch, render, "Clutch Calibration");
            AddPartDropdown(mod, clutchSection, working, PartCatalog.ClutchWeights, render);
            AddPartDropdown(mod, clutchSection, working, PartCatalog.RatioFeel, render);
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
                value => fine.clutchTrimPercent = value);
            content.Add(trimSection);
        }

        private static void BuildSetupTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action render,
            Action<string> setStatus)
        {
            content.Add(MutedLabel(AlpineNativeUiConfig.FineTuneHelpText));

            var fine = working.fineTune ?? (working.fineTune = new FineTuneSettings());

            var handlingSection = Section("Chassis / Stance");
            AddPartDropdown(mod, handlingSection, working, PartCatalog.Suspension, render, "Handling Setup");
            AddPartDropdown(mod, handlingSection, working, PartCatalog.Chassis, render);
            AddPartDropdown(mod, handlingSection, working, PartCatalog.Skis, render);

            if (ShouldShowNativeAccessoriesCategory())
                AddPartDropdown(mod, handlingSection, working, PartCatalog.Accessories, render);

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
                value => fine.powerTrimPercent = value);

            AddSlider(
                driveSection,
                "Traction Trim",
                AlpineNativeUiConfig.TractionTrimMin,
                AlpineNativeUiConfig.TractionTrimMax,
                fine.tractionTrimPercent,
                "F1",
                "%",
                value => fine.tractionTrimPercent = value);

            AddSlider(
                driveSection,
                "Weight Trim",
                AlpineNativeUiConfig.WeightTrimMin,
                AlpineNativeUiConfig.WeightTrimMax,
                fine.weightTrimPercent,
                "F1",
                "%",
                value => fine.weightTrimPercent = value);
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
                value => fine.centerOfMassYTrim = value);

            AddSlider(
                balanceSection,
                "Center of Mass Forward",
                AlpineNativeUiConfig.CenterOfMassZMin,
                AlpineNativeUiConfig.CenterOfMassZMax,
                fine.centerOfMassZTrim,
                "F3",
                " m",
                value => fine.centerOfMassZTrim = value);

            AddSlider(
                balanceSection,
                "Ski Stance",
                AlpineNativeUiConfig.SkiStanceMin,
                AlpineNativeUiConfig.SkiStanceMax,
                fine.skiStanceTrim,
                "F3",
                " m",
                value => fine.skiStanceTrim = value);
            content.Add(balanceSection);

            Button previewButton = SmallButton("Preview Stats", () =>
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
            Action render)
        {
            var lightSection = Section("Headlights");
            AddPartDropdown(mod, lightSection, working, PartCatalog.HeadlightColor, render);
            AddPartDropdown(mod, lightSection, working, PartCatalog.HeadlightBrightness, render);
            AddPartDropdown(mod, lightSection, working, PartCatalog.HeadlightBeam, render);
            AddPartDropdown(mod, lightSection, working, PartCatalog.HeadlightAim, render);

            if (mod != null && mod.HasActiveHeadlightRuntimeBinding())
                lightSection.Add(MutedLabel("Runtime headlight binding active for the current sled."));
            else
                lightSection.Add(MutedLabel("Runtime headlight binding is unavailable until a sled with native HeadLight components is active."));

            content.Add(lightSection);
        }

        private static void BuildLibraryTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action<TuneProfile> setWorking,
            Action render,
            Action<string> setStatus,
            Func<string> getSelectedProfileId,
            Action<string> setSelectedProfileId,
            Func<string> getPendingDeleteProfileId,
            Action<string> setPendingDeleteProfileId,
            Func<bool> getFactoryResetArmed,
            Action<bool> setFactoryResetArmed)
        {
            var currentSection = Section("Current Tune");
            AddButtonRow(currentSection,
                SmallButton("Save Current Tune", () =>
                {
                    setPendingDeleteProfileId?.Invoke(null);
                    setStatus(mod.SaveProfile(working, target, true)
                        ? AlpineNativeUiConfig.ActiveProfileSavedText
                        : AlpineNativeUiConfig.SaveFailedText);
                    render();
                }));
            content.Add(currentSection);

            var restoreSection = Section("Restore");
            bool factoryResetArmed = getFactoryResetArmed != null && getFactoryResetArmed();
            restoreSection.Add(MutedLabel("Factory restore clears Alpine's active profile for this sled and reapplies captured stock defaults."));
            AddButtonRow(restoreSection,
                DangerButton(factoryResetArmed ? "Confirm Factory Restore" : "Factory Restore", () =>
                {
                    setPendingDeleteProfileId?.Invoke(null);

                    if (!factoryResetArmed)
                    {
                        setFactoryResetArmed?.Invoke(true);
                        setStatus("Press Factory Restore again to restore factory tuning for this sled.");
                        render();
                        return;
                    }

                    setFactoryResetArmed?.Invoke(false);
                    if (mod.ResetToFactory(target, true))
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
                content.Add(MutedLabel("Use 'Save Current Tune' to create your first saved tune for this sled."));
                return;
            }

            string selectedId = getSelectedProfileId != null ? getSelectedProfileId() : null;
            string pendingDeleteId = getPendingDeleteProfileId != null ? getPendingDeleteProfileId() : null;

            var savedSection = Section("Saved Tunes");

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

                var name = CardTitle(captured.name ?? "(unnamed tune)");
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
                card.Add(StatsPreview(preview.resolvedStats, preview.requiresReload));
                card.Add(MutedLabel($"Author: {DisplayOrUnknown(captured.author)} | Updated: {FormatUnixTime(captured.updatedUnixTime)}"));

                if (isSelected)
                {
                    Button loadButton = SmallButton("Load/Edit", () =>
                    {
                        setPendingDeleteProfileId?.Invoke(null);
                        setWorking(TuneStore.Clone(captured));
                        setStatus($"Loaded {captured.name} for editing.");
                        render();
                    });

                    Button applyButton = PrimaryButton("Apply", () =>
                    {
                        setPendingDeleteProfileId?.Invoke(null);
                        string message;
                        bool applied = mod.ApplyProfile(TuneStore.Clone(captured), target, true, false, out message);
                        setStatus(applied
                            ? $"Applied {captured.name}."
                            : (string.IsNullOrWhiteSpace(message) ? AlpineNativeUiConfig.ApplyFailedText : message));
                        render();
                    });

                    Button shareButton = SmallButton("Share", () =>
                    {
                        setPendingDeleteProfileId?.Invoke(null);
                        var toShare = TuneStore.Clone(captured);
                        if (!mod.SaveProfile(toShare, target, true))
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

                    Button deleteButton = DangerButton(isPendingDelete ? "Confirm Delete" : "Delete", () =>
                    {
                        if (!isPendingDelete)
                        {
                            setPendingDeleteProfileId?.Invoke(captured.profileId);
                            setStatus($"Press Delete again to remove {captured.name}.");
                            render();
                            return;
                        }

                        mod.DeleteProfile(captured.profileId);
                        if (string.Equals(selectedId, captured.profileId, StringComparison.OrdinalIgnoreCase))
                            setSelectedProfileId?.Invoke(null);

                        setPendingDeleteProfileId?.Invoke(null);
                        setStatus($"Deleted {captured.name}.");
                        render();
                    });

                    AddSplitButtonRow(card,
                        new[] { loadButton, applyButton, shareButton },
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
            var sharingSection = Section("Peer Sharing");

            if (mod.Sharing == null || !mod.Sharing.IsAvailable)
                sharingSection.Add(MutedLabel(AlpineNativeUiConfig.SharingUnavailableText));
            else
                sharingSection.Add(MutedLabel("Steam P2P relay available."));

            if (mod.Sharing != null && !string.IsNullOrWhiteSpace(mod.Sharing.StatusMessage))
                sharingSection.Add(MutedLabel(mod.Sharing.StatusMessage));

            AddButtonRow(sharingSection,
                SmallButton("Publish Current Tune", () =>
                {
                    if (!mod.SaveProfile(working, target, true))
                    {
                        setStatus(AlpineNativeUiConfig.SaveFailedText);
                    }
                    else
                    {
                        setStatus(mod.PublishProfile(working, target)
                            ? AlpineNativeUiConfig.PublishedTuneText
                            : (mod.Sharing != null && !string.IsNullOrWhiteSpace(mod.Sharing.StatusMessage)
                                ? mod.Sharing.StatusMessage
                                : AlpineNativeUiConfig.SharingUnavailableText));
                    }
                    render();
                }),
                SmallButton("Refresh Peer List", () =>
                {
                    bool sent = mod.Sharing != null && mod.Sharing.BroadcastHello();
                    setStatus(sent
                        ? AlpineNativeUiConfig.PeerHelloText
                        : (mod.Sharing != null && !string.IsNullOrWhiteSpace(mod.Sharing.StatusMessage)
                            ? mod.Sharing.StatusMessage
                            : AlpineNativeUiConfig.SharingUnavailableText));
                    render();
                }));

            content.Add(sharingSection);

            var peerReplication = new Toggle("Replicate Peers")
            {
                value = _peerReplicationEnabled
            };
            ApplyControlStyle(peerReplication);
            peerReplication.SetEnabled(AlpineNativeUiConfig.EnablePeerReplicationToggle);
            peerReplication.RegisterValueChangedCallback(evt =>
            {
                _peerReplicationEnabled = evt.newValue;
                setStatus(AlpineNativeUiConfig.PeerReplicationUnavailableText);
                render();
            });

            var replicationSection = Section("Replication");
            replicationSection.Add(peerReplication);
            if (!AlpineNativeUiConfig.EnablePeerReplicationToggle)
                replicationSection.Add(MutedLabel(AlpineNativeUiConfig.PeerReplicationUnavailableText));
            content.Add(replicationSection);

            var summaries = mod.Sharing != null
                ? mod.Sharing.RemoteSummaries.ToList()
                : new List<RemoteTuneSummary>();

            if (summaries.Count == 0)
            {
                content.Add(MutedLabel(AlpineNativeUiConfig.NoSharedTunesText));
                content.Add(MutedLabel("If you have published a tune, ask a peer to open the Share tab and press 'Refresh Peer List'."));
                return;
            }

            var remoteSection = Section("Shared Tunes");

            foreach (var summary in summaries.OrderByDescending(s => s.receivedUnixTime))
            {
                RemoteTuneSummary captured = summary;
                var card = Card(false);

                var header = new VisualElement();
                header.style.flexDirection = FlexDirection.Row;
                header.style.flexWrap = Wrap.Wrap;
                header.style.alignItems = Align.Center;

                var profileName = CardTitle(string.IsNullOrWhiteSpace(captured.profileName)
                    ? "(unnamed shared tune)"
                    : captured.profileName);
                profileName.style.flexGrow = 1;
                header.Add(profileName);
                header.Add(Badge(captured.hasPayload ? "Payload available" : "Summary received"));
                card.Add(header);

                card.Add(MutedLabel($"Sled: {DisplayOrUnknown(captured.targetSledKey)}"));
                card.Add(MutedLabel($"Sender: {DisplayOrUnknown(captured.senderName)}"));
                card.Add(MutedLabel($"Received: {FormatUnixTime(captured.receivedUnixTime)}"));

                Button action = captured.hasPayload
                    ? PrimaryButton("Apply", () =>
                    {
                        string message;
                        bool applied = mod.ApplySharedProfile(captured.senderId, captured.profileId, out message);
                        setStatus(applied
                            ? $"Applied shared tune {captured.profileName}."
                            : (string.IsNullOrWhiteSpace(message) ? AlpineNativeUiConfig.SharedPayloadMissingText : message));

                        render();
                    })
                    : SmallButton("Request", () =>
                    {
                        bool requested = mod.RequestSharedProfile(captured.senderId, captured.profileId);
                        setStatus(requested
                            ? $"Requested {captured.profileName} from {captured.senderName}."
                            : (mod.Sharing != null && !string.IsNullOrWhiteSpace(mod.Sharing.StatusMessage)
                                ? mod.Sharing.StatusMessage
                                : AlpineNativeUiConfig.SharingUnavailableText));

                        render();
                    });

                if (!captured.hasPayload && (mod.Sharing == null || !mod.Sharing.IsAvailable))
                    action.SetEnabled(false);

                AddButtonRow(card, action);
                remoteSection.Add(card);
            }

            content.Add(remoteSection);
        }

        private static void BuildUiSettingsTab(
            VisualElement content,
            Action render,
            Action<string> setStatus)
        {
            var debugSection = Section("Runtime Layout Debug");
            debugSection.Add(MutedLabel("Runtime-only layout controls for testing the native Alpine panel. Changes are not saved."));

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
            Action render)
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
            dropdown.RegisterValueChangedCallback(evt =>
            {
                int index = options.IndexOf(evt.newValue);

                working.donorSledKey = index > 0 && index - 1 < sleds.Count
                    ? AlpineTuningMod.GetSledKey(sleds[index - 1])
                    : null;

                render();
            });

            content.Add(dropdown);
        }

        private static void AddProfileNameField(VisualElement content, TuneProfile working)
        {
            var nameField = new TextField("Build Name")
            {
                value = working.name ?? "Alpine Tune"
            };

            ApplyControlStyle(nameField);
            nameField.RegisterValueChangedCallback(evt => working.name = evt.newValue);
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

        private static void AddPartDropdown(
            AlpineTuningMod mod,
            VisualElement content,
            TuneProfile working,
            string category,
            Action render,
            string labelOverride = null)
        {
            var parts = mod.Catalog.PartsForCategory(category).ToList();
            if (parts.Count == 0)
                return;

            var options = parts.Select(p => p.name).ToList();
            string selectedPartId = working.GetPartId(category);
            int selectedIndex = Mathf.Max(0, parts.FindIndex(p => p.id == selectedPartId));

            var dropdown = Dropdown(labelOverride ?? mod.Catalog.LabelForCategory(category), options, selectedIndex);
            dropdown.RegisterValueChangedCallback(evt =>
            {
                int index = options.IndexOf(evt.newValue);

                if (index >= 0 && index < parts.Count)
                    working.SetPartId(category, parts[index].id);

                render();
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
            Action<float> changed)
        {
            string ValueText(float sliderValue)
            {
                return $"{sliderValue.ToString(valueFormat)}{suffix}";
            }

            var slider = new Slider(label, min, max)
            {
                value = Mathf.Clamp(value, min, max)
            };

            slider.label = $"{label}: {ValueText(slider.value)}";
            ApplyControlStyle(slider);

            slider.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp(evt.newValue, min, max);
                changed(clamped);
                slider.label = $"{label}: {ValueText(clamped)}";
            });

            content.Add(slider);
        }

        private static Foldout BuildPartDetailsFoldout(AlpineTuningMod mod, TuneProfile working)
        {
            var foldout = new Foldout
            {
                text = "Part Details",
                value = false
            };

            foldout.style.marginTop = AlpineNativeUiConfig.DefaultButtonRowMarginTop;

            if (mod == null || working == null)
            {
                foldout.Add(MutedLabel("No tune loaded."));
                return foldout;
            }

            foreach (string category in PartCatalog.OrderedCategories)
            {
                if (!AlpineNativeUiConfig.ShowNativeAccessoriesCategory &&
                    string.Equals(category, PartCatalog.Accessories, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string partId = working.GetPartId(category);
                var part = mod.Catalog.Find(partId) ?? mod.Catalog.Find(mod.Catalog.DefaultPartId(category));
                if (part == null)
                    continue;

                foldout.Add(MutedLabel($"{mod.Catalog.LabelForCategory(category)}: {part.name}"));
                if (!string.IsNullOrWhiteSpace(part.description))
                    foldout.Add(MutedLabel(part.description));
            }

            return foldout;
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

        private static VisualElement StatsPreview(ResolvedStats stats, bool requiresReload)
        {
            var row = new VisualElement();
            ApplyStatRowStyle(row);

            if (stats != null)
            {
                AddStatChip(row, "HP", $"{stats.horsePower:F1}");
                AddStatChip(row, "Power", $"{stats.powerFactor:F2}");
                AddStatChip(row, "Paddle", TrackSpecResolver.FormatPaddleHeight(stats.lugHeight));
                AddStatChip(row, "Friction", $"{stats.friction:F2}");
                AddStatChip(row, "Weight", $"{stats.weight:F1}");
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

            row.Add(Chip(text, true));
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
