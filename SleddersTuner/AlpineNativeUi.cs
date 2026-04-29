using MelonLoader;
using System;
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
        public const string PanelName = "alpine-tuning-panel";
        public const string GarageTabName = "Tab_AlpineTuning";
        public const string GarageTabButtonName = "AlpineTuningTabButton";
        public const string PauseButtonName = "AlpineTuningPauseButton";

        // Button / tab labels.
        public const string ModTitle = "ALPINE TUNING 2.0";
        public const string BuildTabLabel = "Build";
        public const string FineTuneTabLabel = "Fine Tune";
        public const string LibraryTabLabel = "Library";
        public const string ShareTabLabel = "Share";
        public const string UiSettingsTabLabel = "UI Settings";
        public const string RefreshSledLabel = "Refresh Sled";

        // Feature switches.
        public const bool EnableRuntimeUiSettingsTab = true;
        public const bool ShowRefreshSledButton = true;
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
        public const float DefaultPanelMaxWidth = 800f;
        public const float DefaultPanelMaxHeight = 560f;
        public const float DefaultRootMarginTop = 6f;
        public const float DefaultRootMarginBottom = 8f;
        public const float DefaultRootMarginLeft = 4f;
        public const float DefaultRootMarginRight = 4f;
        public const float DefaultPanelPadding = 10f;
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
        public const float DefaultTitleFontSize = 15f;

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

        // Text.
        public const string NoSavedProfilesText = "No saved profiles for this sled yet.";
        public const string NoSharedTunesText = "No shared tunes discovered yet. Both players need the mod.";
        public const string FineTuneHelpText = "Fine tune trims are intentionally clamped so shared builds stay sane.";
        public const string ReloadRequiredHintText = " | reload required for selected parts";
        public const string RefreshedSledText = "Refreshed current sled context.";
        public const string PreviewUpdatedText = "Preview updated.";
        public const string FactoryDefaultsRestoredText = "Factory defaults restored.";
        public const string ActiveProfileSavedText = "Saved as active profile.";
        public const string AppliedSavedActiveText = "Applied and saved as active profile.";
        public const string AppliedSavedReloadedText = "Applied, saved, and reloaded if required.";
        public const string FineTuneAppliedText = "Fine tune applied and saved.";
        public const string PublishedTuneText = "Published tune summary to discovered lobby peers.";
        public const string PeerHelloText = "Sent peer discovery hello.";
        public const string PeerReplicationUnavailableText = "Replicate Peers Coming Soon!";
        public const string SharedPayloadMissingText = "Shared payload not available.";
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
        private static readonly BindingFlags BF =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly AlpineNativeUiRuntimeSettings RuntimeUi = new AlpineNativeUiRuntimeSettings();
        private static bool _peerReplicationEnabled;

        public static void TryAttachOpenMenus(AlpineTuningMod mod)
        {
            if (mod == null)
                return;

            try
            {
                foreach (var vehicleMenu in Resources.FindObjectsOfTypeAll<VehicleSelectionUiController>())
                    AttachToVehicleSelection(mod, vehicleMenu);

                foreach (var pauseMenu in Resources.FindObjectsOfTypeAll<PauseUIController>())
                    AttachToPause(mod, pauseMenu);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Native UI scan skipped: {ex.Message}");
            }
        }

        public static void AttachToVehicleSelection(AlpineTuningMod mod, VehicleSelectionUiController controller)
        {
            AttachToGarage(mod, controller);
        }

        public static void AttachToPause(AlpineTuningMod mod, PauseUIController controller)
        {
            AttachToPauseMenu(mod, controller);
        }

        private static void AttachToGarage(AlpineTuningMod mod, VehicleSelectionUiController controller)
        {
            if (mod == null || controller == null)
                return;

            VisualElement menuRoot = FindVisualRoot(controller);
            if (menuRoot == null || menuRoot.Q<VisualElement>(AlpineNativeUiConfig.RootName) != null)
                return;

            VisualElement tabsButtons = menuRoot.Q<VisualElement>(AlpineNativeUiConfig.GarageTabsButtonsName);
            VisualElement tabs = menuRoot.Q<VisualElement>(AlpineNativeUiConfig.GarageTabsName);

            if (tabsButtons == null || tabs == null)
            {
                AttachInlineFallback(mod, controller, "Garage", menuRoot);
                return;
            }

            Action render;
            VisualElement surface = CreateTuningSurface(mod, controller, "Garage", out render);

            var tabPanel = new VisualElement { name = AlpineNativeUiConfig.GarageTabName };
            tabPanel.Add(surface);
            tabPanel.style.display = DisplayStyle.None;

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

            object nativeTabManager = GetFieldValue<object>(
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
        }

        private static void AttachToPauseMenu(AlpineTuningMod mod, PauseUIController controller)
        {
            if (mod == null || controller == null)
                return;

            VisualElement menuRoot = FindVisualRoot(controller);
            if (menuRoot == null || menuRoot.Q<VisualElement>(AlpineNativeUiConfig.RootName) != null)
                return;

            Button anchor =
                menuRoot.Q<Button>(AlpineNativeUiConfig.PauseSelectVehicleButtonName) ??
                menuRoot.Q<Button>(AlpineNativeUiConfig.PauseOptionsButtonName) ??
                FirstDescendant<Button>(menuRoot);

            VisualElement parent = anchor != null && anchor.parent != null
                ? anchor.parent
                : menuRoot;

            Action render;
            VisualElement surface = CreateTuningSurface(mod, controller, "Pause", out render);
            surface.style.display = DisplayStyle.None;

            var button = new Button
            {
                name = AlpineNativeUiConfig.PauseButtonName,
                text = AlpineNativeUiConfig.ModTitle
            };

            CopyClasses(anchor, button);
            ApplyButtonStyle(button);

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
        }

        private static void AttachInlineFallback(AlpineTuningMod mod, object menuContext, string source, VisualElement parent)
        {
            if (mod == null || menuContext == null)
                return;

            if (parent == null || parent.Q<VisualElement>(AlpineNativeUiConfig.RootName) != null)
                return;

            Action render;
            VisualElement surface = CreateTuningSurface(mod, menuContext, source, out render);
            surface.style.display = DisplayStyle.None;

            var button = new Button { text = AlpineNativeUiConfig.ModTitle };
            ApplyButtonStyle(button);

            button.clicked += () =>
            {
                bool open = surface.style.display == DisplayStyle.None;
                surface.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

                if (open)
                    render();
            };

            parent.Add(button);
            parent.Add(surface);
        }

        private static VisualElement CreateTuningSurface(
            AlpineTuningMod mod,
            object menuContext,
            string source,
            out Action renderAction)
        {
            var target = mod.ResolveTargetSled(menuContext);
            var working = target != null ? mod.CreateWorkingProfile(target) : null;
            string activeTab = AlpineNativeUiConfig.BuildTabLabel;
            string librarySelectedProfileId = null;

            var root = new VisualElement { name = AlpineNativeUiConfig.RootName };
            var panel = new VisualElement { name = AlpineNativeUiConfig.PanelName };
            var status = new Label();
            var header = new VisualElement();
            var headerLeft = new VisualElement();
            var headerRight = new VisualElement();
            var content = new ScrollView();

            ApplyRootStyle(root);
            ApplyPanelStyle(panel);
            ApplyStatusStyle(status);
            ApplyTabsStyle(header);
            ApplyContentStyle(content);

            Action render = null;

            Action<string> setStatus = message =>
            {
                status.text = message ?? string.Empty;
            };

            Action<TuneProfile> setWorking = profile =>
            {
                working = profile;
            };

            Action refreshTarget = () =>
            {
                var refreshed = mod.ResolveTargetSled(menuContext);
                if (refreshed != null && refreshed != target)
                {
                    target = refreshed;
                    working = mod.CreateWorkingProfile(target);
                }
            };

            render = () =>
            {
                refreshTarget();

                ApplyRootStyle(root);
                ApplyPanelStyle(panel);
                ApplyStatusStyle(status);
                ApplyTabsStyle(header);
                ApplyContentStyle(content);

                content.Clear();

                if (target == null || working == null)
                {
                    content.Add(new Label($"No sled detected for {source}."));
                    return;
                }

                mod.PreviewProfile(working, target);
                BuildSummary(content, source, target, working);

                switch (activeTab)
                {
                    case AlpineNativeUiConfig.FineTuneTabLabel:
                        BuildFineTuneTab(mod, content, target, working, render, setStatus);
                        break;

                    case AlpineNativeUiConfig.LibraryTabLabel:
                        BuildLibraryTab(mod, content, target, working, setWorking, render, setStatus,
                            () => librarySelectedProfileId,
                            id => librarySelectedProfileId = id);
                        break;

                    case AlpineNativeUiConfig.ShareTabLabel:
                        BuildShareTab(mod, content, target, working, render, setStatus);
                        break;

                    case AlpineNativeUiConfig.UiSettingsTabLabel:
                        BuildUiSettingsTab(content, render, setStatus);
                        break;

                    default:
                        BuildBuildTab(mod, content, target, working, setWorking, render, setStatus);
                        break;
                }
            };

            ApplyHeaderRowStyle(header, headerLeft, headerRight);

            foreach (string tab in RuntimeTabLabels())
            {
                string captured = tab;
                headerLeft.Add(SmallButton(captured, () =>
                {
                    activeTab = captured;
                    render();
                }));
            }

            if (AlpineNativeUiConfig.ShowRefreshSledButton)
            {
                headerLeft.Add(SmallButton(AlpineNativeUiConfig.RefreshSledLabel, () =>
                {
                    target = mod.ResolveTargetSled(menuContext);
                    working = target != null ? mod.CreateWorkingProfile(target) : null;
                    setStatus(AlpineNativeUiConfig.RefreshedSledText);
                    render();
                }));
            }

            headerRight.Add(SmallButton("Apply + Reload", () =>
            {
                if (target == null || working == null)
                    return;

                mod.ApplyProfile(working, target, true, true);
                setStatus(AlpineNativeUiConfig.AppliedSavedReloadedText);
                render();
            }));

            headerRight.Add(SmallButton("Save", () =>
            {
                if (target == null || working == null)
                    return;

                mod.SaveProfile(working, target, true);
                setStatus(AlpineNativeUiConfig.ActiveProfileSavedText);
                render();
            }));

            headerRight.Add(SmallButton("Factory Reset", () =>
            {
                if (target == null)
                    return;

                mod.ResetToFactory(target, true);
                var resetProfile = mod.CreateWorkingProfile(target);
                setWorking(resetProfile);
                working = resetProfile;
                setStatus(AlpineNativeUiConfig.FactoryDefaultsRestoredText);
                render();
            }));

            header.Add(headerLeft);
            header.Add(headerRight);

            panel.Add(header);
            panel.Add(status);
            panel.Add(content);
            root.Add(panel);

            renderAction = render;
            return root;
        }

        private static IEnumerable<string> RuntimeTabLabels()
        {
            yield return AlpineNativeUiConfig.BuildTabLabel;
            yield return AlpineNativeUiConfig.FineTuneTabLabel;
            yield return AlpineNativeUiConfig.LibraryTabLabel;
            yield return AlpineNativeUiConfig.ShareTabLabel;

            if (AlpineNativeUiConfig.EnableRuntimeUiSettingsTab)
                yield return AlpineNativeUiConfig.UiSettingsTabLabel;
        }

        private static void BuildSummary(
            VisualElement content,
            string source,
            VehicleScriptableObject sled,
            TuneProfile profile)
        {
            string sledName = !string.IsNullOrWhiteSpace(sled.displayName)
                ? sled.displayName
                : sled.name;

            var title = new Label($"{source}: {sledName}");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = RuntimeUi.TitleFontSize;
            title.style.color = AlpineNativeUiConfig.TitleTextColor;
            content.Add(title);

            var stats = profile.resolvedStats;
            string reload = profile.requiresReload ? AlpineNativeUiConfig.ReloadRequiredHintText : string.Empty;

            content.Add(MutedLabel(
                $"HP {stats.horsePower:F1} | PF {stats.powerFactor:F2} | Lug {stats.lugHeight:F1} | Friction {stats.friction:F2} | Weight {stats.weight:F1}{reload}"));
        }

        private static void BuildBuildTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action<TuneProfile> setWorking,
            Action render,
            Action<string> setStatus)
        {
            var nameField = new TextField("Profile Name")
            {
                value = working.name ?? "Alpine Tune"
            };

            nameField.style.marginTop = AlpineNativeUiConfig.DefaultControlMarginTop;
            nameField.RegisterValueChangedCallback(evt => working.name = evt.newValue);
            content.Add(nameField);

            AddDonorDropdown(mod, content, working, render);

            foreach (string category in PartCatalog.OrderedCategories)
            {
                if (!AlpineNativeUiConfig.ShowNativeAccessoriesCategory &&
                    string.Equals(category, PartCatalog.Accessories, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddPartDropdown(mod, content, working, category, render);
            }

            content.Add(BuildPartDetailsFoldout(mod, working));
        }

        private static void BuildFineTuneTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action render,
            Action<string> setStatus)
        {
            content.Add(MutedLabel(AlpineNativeUiConfig.FineTuneHelpText));

            var fine = working.fineTune ?? (working.fineTune = new FineTuneSettings());

            AddSlider(
                content,
                "Power Trim %",
                AlpineNativeUiConfig.PowerTrimMin,
                AlpineNativeUiConfig.PowerTrimMax,
                fine.powerTrimPercent,
                value => fine.powerTrimPercent = value);

            AddSlider(
                content,
                "Traction Trim %",
                AlpineNativeUiConfig.TractionTrimMin,
                AlpineNativeUiConfig.TractionTrimMax,
                fine.tractionTrimPercent,
                value => fine.tractionTrimPercent = value);

            AddSlider(
                content,
                "Weight Trim %",
                AlpineNativeUiConfig.WeightTrimMin,
                AlpineNativeUiConfig.WeightTrimMax,
                fine.weightTrimPercent,
                value => fine.weightTrimPercent = value);

            AddSlider(
                content,
                "Clutch Trim %",
                AlpineNativeUiConfig.ClutchTrimMin,
                AlpineNativeUiConfig.ClutchTrimMax,
                fine.clutchTrimPercent,
                value => fine.clutchTrimPercent = value);

            AddSlider(
                content,
                "Center of Grav. Height",
                AlpineNativeUiConfig.CenterOfMassYMin,
                AlpineNativeUiConfig.CenterOfMassYMax,
                fine.centerOfMassYTrim,
                value => fine.centerOfMassYTrim = value);

            AddSlider(
                content,
                "Center of Grav. Front",
                AlpineNativeUiConfig.CenterOfMassZMin,
                AlpineNativeUiConfig.CenterOfMassZMax,
                fine.centerOfMassZTrim,
                value => fine.centerOfMassZTrim = value);

            AddSlider(
                content,
                "Ski Stance",
                AlpineNativeUiConfig.SkiStanceMin,
                AlpineNativeUiConfig.SkiStanceMax,
                fine.skiStanceTrim,
                value => fine.skiStanceTrim = value);

            AddButtonRow(content,
                SmallButton("Preview Stats", () =>
                {
                    mod.PreviewProfile(working, target);
                    setStatus(AlpineNativeUiConfig.PreviewUpdatedText);
                    render();
                }),
                SmallButton("Apply", () =>
                {
                    mod.ApplyProfile(working, target, true, false);
                    setStatus(AlpineNativeUiConfig.FineTuneAppliedText);
                    render();
                }));
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
            Action<string> setSelectedProfileId)
        {
            AddButtonRow(content,
                SmallButton("Save Current Tune", () =>
                {
                    mod.SaveProfile(working, target, true);
                    setStatus(AlpineNativeUiConfig.ActiveProfileSavedText);
                    render();
                }));

            var profiles = mod.ProfilesForSled(target);
            if (profiles.Count == 0)
            {
                content.Add(MutedLabel(AlpineNativeUiConfig.NoSavedProfilesText));
                content.Add(MutedLabel("Use 'Save Current Tune' to create your first saved tune for this sled."));
                return;
            }

            string selectedId = getSelectedProfileId != null ? getSelectedProfileId() : null;

            foreach (var profile in profiles)
            {
                TuneProfile captured = profile;
                bool isSelected = !string.IsNullOrWhiteSpace(selectedId) &&
                                  string.Equals(selectedId, captured.profileId, StringComparison.OrdinalIgnoreCase);

                var card = new VisualElement();
                card.style.flexDirection = FlexDirection.Column;
                card.style.marginTop = AlpineNativeUiConfig.DefaultRowMarginTop;
                card.style.paddingLeft = 6;
                card.style.paddingRight = 6;
                card.style.paddingTop = 4;
                card.style.paddingBottom = 6;
                card.style.backgroundColor = isSelected
                    ? new Color(0.12f, 0.12f, 0.12f, 0.8f)
                    : new Color(0f, 0f, 0f, 0.0f);

                var select = SmallButton(captured.name ?? "(unnamed tune)", () =>
                {
                    setSelectedProfileId?.Invoke(captured.profileId);
                    render();
                });
                select.style.flexGrow = 0;
                card.Add(select);

                var preview = TuneStore.Clone(captured);
                mod.PreviewProfile(preview, target);
                card.Add(MutedLabel($"HP {preview.resolvedStats.horsePower:F1} | PF {preview.resolvedStats.powerFactor:F2} | Lug {preview.resolvedStats.lugHeight:F1} | Friction {preview.resolvedStats.friction:F2} | Weight {preview.resolvedStats.weight:F1}"));

                if (isSelected)
                {
                    card.Add(MutedLabel($"Author: {captured.author ?? "unknown"} | Updated: {captured.updatedUnixTime}"));

                    AddButtonRow(card,
                        SmallButton("Load/Edit", () =>
                        {
                            setWorking(TuneStore.Clone(captured));
                            setStatus($"Loaded {captured.name} for editing.");
                            render();
                        }),
                        SmallButton("Apply", () =>
                        {
                            mod.ApplyProfile(TuneStore.Clone(captured), target, true, false);
                            setStatus($"Applied {captured.name}.");
                            render();
                        }),
                        SmallButton("Share", () =>
                        {
                            var toShare = TuneStore.Clone(captured);
                            mod.SaveProfile(toShare, target, true);
                            mod.PublishProfile(toShare, target);
                            setStatus(AlpineNativeUiConfig.PublishedTuneText);
                            render();
                        }),
                        SmallButton("Delete", () =>
                        {
                            mod.DeleteProfile(captured.profileId);
                            if (string.Equals(selectedId, captured.profileId, StringComparison.OrdinalIgnoreCase))
                                setSelectedProfileId?.Invoke(null);
                            setStatus($"Deleted {captured.name}.");
                            render();
                        }));
                }

                content.Add(card);
            }
        }

        private static void BuildShareTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action render,
            Action<string> setStatus)
        {
            AddButtonRow(content,
                SmallButton("Publish Current Tune", () =>
                {
                    mod.SaveProfile(working, target, true);
                    mod.PublishProfile(working, target);
                    setStatus(AlpineNativeUiConfig.PublishedTuneText);
                }),
                SmallButton("Refresh Peer List", () =>
                {
                    mod.Sharing?.BroadcastHello();
                    setStatus(AlpineNativeUiConfig.PeerHelloText);
                    render();
                }));

            var peerReplication = new Toggle("Replicate Peers")
            {
                value = _peerReplicationEnabled
            };
            peerReplication.style.marginTop = AlpineNativeUiConfig.DefaultControlMarginTop;
            peerReplication.SetEnabled(AlpineNativeUiConfig.EnablePeerReplicationToggle);
            peerReplication.RegisterValueChangedCallback(evt =>
            {
                _peerReplicationEnabled = evt.newValue;
                setStatus(AlpineNativeUiConfig.PeerReplicationUnavailableText);
                render();
            });
            content.Add(peerReplication);
            if (!AlpineNativeUiConfig.EnablePeerReplicationToggle)
                content.Add(MutedLabel(AlpineNativeUiConfig.PeerReplicationUnavailableText));

            var summaries = mod.Sharing != null
                ? mod.Sharing.RemoteSummaries.ToList()
                : new List<RemoteTuneSummary>();

            if (summaries.Count == 0)
            {
                content.Add(MutedLabel(AlpineNativeUiConfig.NoSharedTunesText));
                content.Add(MutedLabel("If you have published a tune, ask a peer to open the Share tab and press 'Refresh Peer List'."));
                return;
            }

            foreach (var summary in summaries.OrderByDescending(s => s.receivedUnixTime))
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginTop = AlpineNativeUiConfig.DefaultRowMarginTop;

                var label = new Label($"{summary.profileName} | {summary.targetSledKey} | {summary.senderName}");
                label.style.flexGrow = 1;
                label.style.color = AlpineNativeUiConfig.RowTextColor;
                row.Add(label);

                RemoteTuneSummary captured = summary;

                row.Add(SmallButton(captured.hasPayload ? "Apply" : "Request", () =>
                {
                    if (captured.hasPayload)
                    {
                        bool applied = mod.ApplySharedProfile(captured.profileId);
                        setStatus(applied
                            ? $"Applied shared tune {captured.profileName}."
                            : AlpineNativeUiConfig.SharedPayloadMissingText);
                    }
                    else
                    {
                        mod.RequestSharedProfile(captured.senderId, captured.profileId);
                        setStatus($"Requested {captured.profileName} from {captured.senderName}.");
                    }

                    render();
                }));

                content.Add(row);
            }
        }

        private static void BuildUiSettingsTab(
            VisualElement content,
            Action render,
            Action<string> setStatus)
        {
            content.Add(MutedLabel("These settings affect only the native Alpine Tuning panel layout. They are runtime-only unless you wire them into MelonPreferences or another save system."));

            AddRuntimeSlider(
                content,
                "Panel Max Width",
                AlpineNativeUiConfig.RuntimePanelWidthMin,
                AlpineNativeUiConfig.RuntimePanelWidthMax,
                RuntimeUi.PanelMaxWidth,
                value => RuntimeUi.PanelMaxWidth = value,
                render);

            AddRuntimeSlider(
                content,
                "Panel Max Height",
                AlpineNativeUiConfig.RuntimePanelHeightMin,
                AlpineNativeUiConfig.RuntimePanelHeightMax,
                RuntimeUi.PanelMaxHeight,
                value => RuntimeUi.PanelMaxHeight = value,
                render);

            AddRuntimeSlider(
                content,
                "Panel Padding",
                AlpineNativeUiConfig.RuntimePaddingMin,
                AlpineNativeUiConfig.RuntimePaddingMax,
                RuntimeUi.PanelPadding,
                value => RuntimeUi.PanelPadding = value,
                render);

            AddRuntimeSlider(
                content,
                "Button Height",
                AlpineNativeUiConfig.RuntimeButtonHeightMin,
                AlpineNativeUiConfig.RuntimeButtonHeightMax,
                RuntimeUi.ButtonHeight,
                value => RuntimeUi.ButtonHeight = value,
                render);

            AddRuntimeSlider(
                content,
                "Title Font Size",
                AlpineNativeUiConfig.RuntimeFontSizeMin,
                AlpineNativeUiConfig.RuntimeFontSizeMax,
                RuntimeUi.TitleFontSize,
                value => RuntimeUi.TitleFontSize = value,
                render);

            AddRuntimeSlider(
                content,
                "Panel Opacity",
                AlpineNativeUiConfig.RuntimeOpacityMin,
                AlpineNativeUiConfig.RuntimeOpacityMax,
                RuntimeUi.PanelOpacity,
                value => RuntimeUi.PanelOpacity = value,
                render);

            AddButtonRow(content,
                SmallButton("Reset UI Defaults", () =>
                {
                    RuntimeUi.ResetToDefaults();
                    setStatus("UI settings reset to hardcoded defaults.");
                    render();
                }));
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

        private static void AddPartDropdown(
            AlpineTuningMod mod,
            VisualElement content,
            TuneProfile working,
            string category,
            Action render)
        {
            var parts = mod.Catalog.PartsForCategory(category).ToList();
            if (parts.Count == 0)
                return;

            var options = parts.Select(p => p.name).ToList();
            string selectedPartId = working.GetPartId(category);
            int selectedIndex = Mathf.Max(0, parts.FindIndex(p => p.id == selectedPartId));

            var dropdown = Dropdown(mod.Catalog.LabelForCategory(category), options, selectedIndex);
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

            dropdown.style.marginTop = AlpineNativeUiConfig.DefaultControlMarginTop;
            return dropdown;
        }

        private static void AddSlider(
            VisualElement content,
            string label,
            float min,
            float max,
            float value,
            Action<float> changed)
        {
            var slider = new Slider(label, min, max)
            {
                value = Mathf.Clamp(value, min, max)
            };

            slider.style.marginTop = AlpineNativeUiConfig.DefaultControlMarginTop;
            var valueLabel = MutedLabel($"{slider.value:F3}");

            slider.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp(evt.newValue, min, max);
                changed(clamped);
                valueLabel.text = $"{clamped:F3}";
            });

            content.Add(slider);
            content.Add(valueLabel);
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

        private static void ApplyHeaderRowStyle(VisualElement header, VisualElement left, VisualElement right)
        {
            ApplyTabsStyle(header);

            if (left != null)
            {
                left.style.flexDirection = FlexDirection.Row;
                left.style.flexGrow = 1;
                left.style.flexWrap = Wrap.Wrap;
            }

            if (right != null)
            {
                right.style.flexDirection = FlexDirection.Row;
                right.style.flexGrow = 0;
                right.style.flexWrap = Wrap.Wrap;
                right.style.justifyContent = Justify.FlexEnd;
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

            slider.style.marginTop = AlpineNativeUiConfig.DefaultControlMarginTop;

            var valueLabel = MutedLabel($"{label}: {slider.value:F2}");

            slider.RegisterValueChangedCallback(evt =>
            {
                float clamped = Mathf.Clamp(evt.newValue, min, max);
                changed(clamped);
                valueLabel.text = $"{label}: {clamped:F2}";
                render();
            });

            content.Add(slider);
            content.Add(valueLabel);
        }

        private static void AddButtonRow(VisualElement content, params Button[] buttons)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = AlpineNativeUiConfig.DefaultButtonRowMarginTop;

            foreach (var button in buttons)
                row.Add(button);

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

        private static Label MutedLabel(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.style.color = AlpineNativeUiConfig.MutedTextColor;
            label.style.marginTop = AlpineNativeUiConfig.DefaultMutedLabelMarginTop;
            return label;
        }

        private static void ApplyRootStyle(VisualElement root)
        {
            if (root == null)
                return;

            root.style.maxWidth = RuntimeUi.PanelMaxWidth;
            root.style.marginTop = AlpineNativeUiConfig.DefaultRootMarginTop;
            root.style.marginBottom = AlpineNativeUiConfig.DefaultRootMarginBottom;
            root.style.marginLeft = AlpineNativeUiConfig.DefaultRootMarginLeft;
            root.style.marginRight = AlpineNativeUiConfig.DefaultRootMarginRight;
        }

        private static void ApplyPanelStyle(VisualElement panel)
        {
            if (panel == null)
                return;

            Color panelColor = AlpineNativeUiConfig.PanelBackgroundColor;
            panelColor.a = RuntimeUi.PanelOpacity;

            panel.style.paddingTop = RuntimeUi.PanelPadding;
            panel.style.paddingBottom = RuntimeUi.PanelPadding;
            panel.style.paddingLeft = RuntimeUi.PanelPadding;
            panel.style.paddingRight = RuntimeUi.PanelPadding;
            panel.style.marginTop = AlpineNativeUiConfig.DefaultPanelMarginTop;
            panel.style.backgroundColor = panelColor;
        }

        private static void ApplyTabsStyle(VisualElement tabs)
        {
            if (tabs == null)
                return;

            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.marginBottom = AlpineNativeUiConfig.DefaultTabsMarginBottom;
        }

        private static void ApplyStatusStyle(Label status)
        {
            if (status == null)
                return;

            status.style.marginTop = AlpineNativeUiConfig.DefaultStatusMarginTop;
            status.style.color = AlpineNativeUiConfig.StatusTextColor;
        }

        private static void ApplyContentStyle(ScrollView content)
        {
            if (content == null)
                return;

            content.style.maxHeight = RuntimeUi.PanelMaxHeight;
        }

        private static void ApplyButtonStyle(Button button)
        {
            if (button == null)
                return;

            button.style.marginRight = AlpineNativeUiConfig.DefaultButtonMarginRight;
            button.style.marginTop = AlpineNativeUiConfig.DefaultButtonMarginTop;
            button.style.marginBottom = AlpineNativeUiConfig.DefaultButtonMarginBottom;
            button.style.height = RuntimeUi.ButtonHeight;
        }

        private static VisualElement FindVisualRoot(object controller)
        {
            if (controller == null)
                return null;

            VisualElement preferred = GetFieldValue<VisualElement>(
                controller,
                AlpineNativeUiConfig.VehicleRootFieldName);

            if (preferred != null)
                return preferred;

            foreach (FieldInfo field in controller.GetType().GetFields(BF))
            {
                if (!typeof(VisualElement).IsAssignableFrom(field.FieldType))
                    continue;

                var element = field.GetValue(controller) as VisualElement;
                if (element != null)
                    return element;
            }

            return null;
        }

        private static bool TryRegisterNativeTab(
            object nativeTabManager,
            VisualElement tabPanel,
            Button tabButton,
            int insertIndex,
            Action selected,
            out int nativeIndex)
        {
            nativeIndex = -1;

            if (nativeTabManager == null || tabPanel == null || tabButton == null)
                return false;

            var tabPanels = GetFieldValue<List<VisualElement>>(
                nativeTabManager,
                AlpineNativeUiConfig.NativeTabPanelsFieldName);

            var tabButtons = GetFieldValue<List<Button>>(
                nativeTabManager,
                AlpineNativeUiConfig.NativeTabButtonsFieldName);

            if (tabPanels == null || tabButtons == null || tabPanels.Count != tabButtons.Count)
                return false;

            nativeIndex = Mathf.Clamp(insertIndex, 0, tabPanels.Count);

            tabPanels.Insert(nativeIndex, tabPanel);
            tabButtons.Insert(nativeIndex, tabButton);

            var callbacks = GetFieldValue<Dictionary<int, Action>>(
                nativeTabManager,
                AlpineNativeUiConfig.NativeTabCallbacksFieldName);

            if (callbacks != null)
                callbacks[nativeIndex] = selected;

            return true;
        }

        private static void SelectNativeTab(object nativeTabManager, int index)
        {
            if (nativeTabManager == null || index < 0)
                return;

            MethodInfo select = nativeTabManager.GetType().GetMethod(
                AlpineNativeUiConfig.NativeSelectTabMethodName,
                BF,
                null,
                new[] { typeof(int) },
                null);

            select?.Invoke(nativeTabManager, new object[] { index });
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

        private static T GetFieldValue<T>(object target, string fieldName) where T : class
        {
            if (target == null)
                return null;

            var field = target.GetType().GetField(fieldName, BF);
            return field?.GetValue(target) as T;
        }
    }
}
