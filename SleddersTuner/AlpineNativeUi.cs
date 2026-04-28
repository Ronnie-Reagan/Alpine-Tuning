using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlpineTuning
{
    internal static class AlpineNativeUi
    {
        private const string RootName = "alpine-tuning-root";
        private const string PanelName = "alpine-tuning-panel";
        private const string GarageTabName = "Tab_AlpineTuning";
        private const string GarageTabButtonName = "AlpineTuningTabButton";
        private const string PauseButtonName = "AlpineTuningPauseButton";

        // Layout knobs for the embedded panel only. Placement is handled by the native
        // menu containers so Alpine does not depend on absolute offsets.
        private const float PanelMaxWidth = 800f;
        private const float PanelMaxHeight = 560f;

        private static readonly BindingFlags BF =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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
            if (menuRoot == null || menuRoot.Q<VisualElement>(RootName) != null)
                return;

            VisualElement tabsButtons = menuRoot.Q<VisualElement>("TabsButtons");
            VisualElement tabs = menuRoot.Q<VisualElement>("Tabs");
            if (tabsButtons == null || tabs == null)
            {
                AttachInlineFallback(mod, controller, "Garage", menuRoot);
                return;
            }

            Action render;
            VisualElement surface = CreateTuningSurface(mod, controller, "Garage", out render);

            var tabPanel = new VisualElement { name = GarageTabName };
            tabPanel.Add(surface);
            tabPanel.style.display = DisplayStyle.None;

            var tabButton = new Button { name = GarageTabButtonName, text = "ALPINE TUNING 2.0" };
            tabButton.focusable = false;
            CopyClasses(LastButtonChild(tabsButtons), tabButton);

            int insertIndex = Mathf.Min(tabsButtons.childCount, tabs.childCount);
            tabsButtons.Insert(insertIndex, tabButton);
            tabs.Insert(insertIndex, tabPanel);

            object nativeTabManager = GetFieldValue<object>(controller, "CDOJAEOEMDH");
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
            if (menuRoot == null || menuRoot.Q<VisualElement>(RootName) != null)
                return;

            Button anchor =
                menuRoot.Q<Button>("SelectVehicle") ??
                menuRoot.Q<Button>("Options") ??
                FirstDescendant<Button>(menuRoot);

            VisualElement parent = anchor != null && anchor.parent != null
                ? anchor.parent
                : menuRoot;

            Action render;
            VisualElement surface = CreateTuningSurface(mod, controller, "Pause", out render);
            surface.style.display = DisplayStyle.None;

            var button = new Button { name = PauseButtonName, text = "ALPINE TUNING 2.0" };
            CopyClasses(anchor, button);

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

            if (parent == null || parent.Q<VisualElement>(RootName) != null)
                return;

            Action render;
            VisualElement surface = CreateTuningSurface(mod, menuContext, source, out render);
            surface.style.display = DisplayStyle.None;

            var button = new Button { text = "ALPINE TUNING 2.0" };
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

        private static VisualElement CreateTuningSurface(AlpineTuningMod mod, object menuContext, string source, out Action renderAction)
        {
            var target = mod.ResolveTargetSled(menuContext);
            var working = target != null ? mod.CreateWorkingProfile(target) : null;
            string activeTab = "Build";

            var root = new VisualElement { name = RootName };
            root.style.maxWidth = PanelMaxWidth;
            root.style.marginTop = 6;
            root.style.marginBottom = 8;
            root.style.marginLeft = 4;
            root.style.marginRight = 4;

            var panel = new VisualElement { name = PanelName };
            panel.style.paddingTop = 10;
            panel.style.paddingBottom = 10;
            panel.style.paddingLeft = 10;
            panel.style.paddingRight = 10;
            panel.style.marginTop = 4;
            panel.style.backgroundColor = new Color(0.07f, 0.09f, 0.11f, 0.92f);

            var status = new Label();
            status.style.marginTop = 6;
            status.style.color = new Color(0.74f, 0.88f, 1f, 1f);

            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.marginBottom = 8;

            var content = new ScrollView();
            content.style.maxHeight = PanelMaxHeight;

            Action render = null;
            Action<string> setStatus = message => status.text = message ?? string.Empty;
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
                    case "Fine Tune":
                        BuildFineTuneTab(mod, content, target, working, render, setStatus);
                        break;
                    case "Library":
                        BuildLibraryTab(mod, content, target, working, profile => working = profile, render, setStatus);
                        break;
                    case "Share":
                        BuildShareTab(mod, content, target, working, render, setStatus);
                        break;
                    default:
                        BuildBuildTab(mod, content, target, working, render, setStatus);
                        break;
                }
            };

            foreach (string tab in new[] { "Build", "Fine Tune", "Library", "Share" })
            {
                string captured = tab;
                var button = SmallButton(captured, () =>
                {
                    activeTab = captured;
                    render();
                });
                tabs.Add(button);
            }

            var refresh = SmallButton("Refresh Sled", () =>
            {
                target = mod.ResolveTargetSled(menuContext);
                working = target != null ? mod.CreateWorkingProfile(target) : null;
                setStatus("Refreshed current sled context.");
                render();
            });
            tabs.Add(refresh);

            panel.Add(tabs);
            panel.Add(status);
            panel.Add(content);
            root.Add(panel);
            renderAction = render;
            return root;
        }

        private static void BuildSummary(VisualElement content, string source, VehicleScriptableObject sled, TuneProfile profile)
        {
            string sledName = !string.IsNullOrWhiteSpace(sled.displayName) ? sled.displayName : sled.name;
            var title = new Label($"{source}: {sledName}");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 15;
            title.style.color = Color.white;
            content.Add(title);

            var stats = profile.resolvedStats;
            string reload = profile.requiresReload ? " | reload needed" : string.Empty;
            content.Add(MutedLabel(
                $"HP {stats.horsePower:F1} | PF {stats.powerFactor:F2} | Lug {stats.lugHeight:F1} | Friction {stats.friction:F2} | Weight {stats.weight:F1}{reload}"));
        }

        private static void BuildBuildTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action render,
            Action<string> setStatus)
        {
            var nameField = new TextField("Profile Name") { value = working.name ?? "Alpine Tune" };
            nameField.RegisterValueChangedCallback(evt => working.name = evt.newValue);
            content.Add(nameField);

            AddDonorDropdown(mod, content, working, render);

            foreach (string category in PartCatalog.OrderedCategories)
                AddPartDropdown(mod, content, working, category, render);

            AddButtonRow(content,
                SmallButton("Apply", () =>
                {
                    working.name = nameField.value;
                    mod.ApplyProfile(working, target, true, false);
                    setStatus("Applied and saved as active profile.");
                    render();
                }),
                SmallButton("Apply + Reload", () =>
                {
                    working.name = nameField.value;
                    mod.ApplyProfile(working, target, true, true);
                    setStatus("Applied, saved, and reloaded if required.");
                    render();
                }),
                SmallButton("Save", () =>
                {
                    working.name = nameField.value;
                    mod.SaveProfile(working, target, true);
                    setStatus("Saved as active profile.");
                    render();
                }),
                SmallButton("Factory Reset", () =>
                {
                    mod.ResetToFactory(target, true);
                    working = mod.CreateWorkingProfile(target);
                    setStatus("Factory defaults restored.");
                    render();
                }));
        }

        private static void BuildFineTuneTab(
            AlpineTuningMod mod,
            VisualElement content,
            VehicleScriptableObject target,
            TuneProfile working,
            Action render,
            Action<string> setStatus)
        {
            content.Add(MutedLabel("Fine tune trims are intentionally clamped so shared builds stay sane."));
            var fine = working.fineTune ?? (working.fineTune = new FineTuneSettings());

            AddSlider(content, "Power Trim %", -10f, 10f, fine.powerTrimPercent, value => fine.powerTrimPercent = value);
            AddSlider(content, "Traction Trim %", -10f, 10f, fine.tractionTrimPercent, value => fine.tractionTrimPercent = value);
            AddSlider(content, "Weight Trim %", -8f, 8f, fine.weightTrimPercent, value => fine.weightTrimPercent = value);
            AddSlider(content, "Clutch Trim %", -10f, 10f, fine.clutchTrimPercent, value => fine.clutchTrimPercent = value);
            AddSlider(content, "COM Height", -0.08f, 0.08f, fine.centerOfMassYTrim, value => fine.centerOfMassYTrim = value);
            AddSlider(content, "COM Fore/Aft", -0.12f, 0.12f, fine.centerOfMassZTrim, value => fine.centerOfMassZTrim = value);
            AddSlider(content, "Ski Stance", -0.08f, 0.08f, fine.skiStanceTrim, value => fine.skiStanceTrim = value);

            AddButtonRow(content,
                SmallButton("Preview Stats", () =>
                {
                    mod.PreviewProfile(working, target);
                    setStatus("Preview updated.");
                    render();
                }),
                SmallButton("Apply", () =>
                {
                    mod.ApplyProfile(working, target, true, false);
                    setStatus("Fine tune applied and saved.");
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
            Action<string> setStatus)
        {
            var profiles = mod.ProfilesForSled(target);
            if (profiles.Count == 0)
            {
                content.Add(MutedLabel("No saved profiles for this sled yet."));
                return;
            }

            foreach (var profile in profiles)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginTop = 4;

                var label = new Label($"{profile.name}  HP {profile.resolvedStats.horsePower:F1}");
                label.style.flexGrow = 1;
                label.style.color = Color.white;
                row.Add(label);

                TuneProfile captured = profile;
                row.Add(SmallButton("Load", () =>
                {
                    setWorking(TuneStore.Clone(captured));
                    setStatus($"Loaded {captured.name} for editing.");
                    render();
                }));
                row.Add(SmallButton("Apply", () =>
                {
                    mod.ApplyProfile(TuneStore.Clone(captured), target, true, false);
                    setStatus($"Applied {captured.name}.");
                    render();
                }));
                row.Add(SmallButton("Delete", () =>
                {
                    mod.DeleteProfile(captured.profileId);
                    setStatus($"Deleted {captured.name}.");
                    render();
                }));

                content.Add(row);
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
                    setStatus("Published tune summary to discovered lobby peers.");
                }),
                SmallButton("Refresh Peer List", () =>
                {
                    mod.Sharing?.BroadcastHello();
                    setStatus("Sent peer discovery hello.");
                    render();
                }));

            var summaries = mod.Sharing != null
                ? mod.Sharing.RemoteSummaries.ToList()
                : new List<RemoteTuneSummary>();

            if (summaries.Count == 0)
            {
                content.Add(MutedLabel("No shared tunes discovered yet. Both players need the mod."));
                return;
            }

            foreach (var summary in summaries.OrderByDescending(s => s.receivedUnixTime))
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginTop = 4;

                var label = new Label($"{summary.profileName} | {summary.targetSledKey} | {summary.senderName}");
                label.style.flexGrow = 1;
                label.style.color = Color.white;
                row.Add(label);

                RemoteTuneSummary captured = summary;
                row.Add(SmallButton(captured.hasPayload ? "Apply" : "Request", () =>
                {
                    if (captured.hasPayload)
                    {
                        bool applied = mod.ApplySharedProfile(captured.profileId);
                        setStatus(applied ? $"Applied shared tune {captured.profileName}." : "Shared payload not available.");
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

        private static void AddDonorDropdown(AlpineTuningMod mod, VisualElement content, TuneProfile working, Action render)
        {
            var sleds = mod.SelectableSleds.ToList();
            var options = new List<string> { "None" };
            options.AddRange(sleds.Select(s => !string.IsNullOrWhiteSpace(s.displayName) ? s.displayName : s.name));

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

        private static void AddPartDropdown(AlpineTuningMod mod, VisualElement content, TuneProfile working, string category, Action render)
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

            var selected = parts[selectedIndex];
            content.Add(MutedLabel(selected.description));
        }

        private static DropdownField Dropdown(string label, List<string> choices, int selectedIndex)
        {
            selectedIndex = Mathf.Clamp(selectedIndex, 0, choices.Count - 1);
            var dropdown = new DropdownField(label);
            dropdown.choices = choices;
            dropdown.value = choices.Count > 0 ? choices[selectedIndex] : string.Empty;
            dropdown.style.marginTop = 6;
            return dropdown;
        }

        private static void AddSlider(VisualElement content, string label, float min, float max, float value, Action<float> changed)
        {
            var slider = new Slider(label, min, max) { value = Mathf.Clamp(value, min, max) };
            slider.style.marginTop = 6;
            slider.RegisterValueChangedCallback(evt => changed(evt.newValue));
            content.Add(slider);
        }

        private static void AddButtonRow(VisualElement content, params Button[] buttons)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 8;

            foreach (var button in buttons)
                row.Add(button);

            content.Add(row);
        }

        private static Button SmallButton(string text, Action clicked)
        {
            var button = new Button(clicked) { text = text };
            button.style.marginRight = 4;
            button.style.marginTop = 2;
            button.style.marginBottom = 2;
            button.style.height = 28;
            return button;
        }

        private static Label MutedLabel(string text)
        {
            var label = new Label(text ?? string.Empty);
            label.style.color = new Color(0.72f, 0.78f, 0.84f, 1f);
            label.style.marginTop = 2;
            return label;
        }

        private static VisualElement FindVisualRoot(object controller)
        {
            if (controller == null)
                return null;

            VisualElement preferred = GetFieldValue<VisualElement>(controller, "NPAACPBJNOL");
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

            var tabPanels = GetFieldValue<List<VisualElement>>(nativeTabManager, "FLNOHFIPDDN");
            var tabButtons = GetFieldValue<List<Button>>(nativeTabManager, "PJBNPIEGJFB");
            if (tabPanels == null || tabButtons == null || tabPanels.Count != tabButtons.Count)
                return false;

            nativeIndex = Mathf.Clamp(insertIndex, 0, tabPanels.Count);
            tabPanels.Insert(nativeIndex, tabPanel);
            tabButtons.Insert(nativeIndex, tabButton);

            var callbacks = GetFieldValue<Dictionary<int, Action>>(nativeTabManager, "BFLLJAMNBEK");
            if (callbacks != null)
                callbacks[nativeIndex] = selected;

            return true;
        }

        private static void SelectNativeTab(object nativeTabManager, int index)
        {
            if (nativeTabManager == null || index < 0)
                return;

            MethodInfo select = nativeTabManager.GetType().GetMethod(
                "PJFAFBFMOIK",
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
