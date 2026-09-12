using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace AlpineTuning
{
    /// <summary>
    /// Physical gamepad input backed by the Unity Input System shipped with
    /// Sledders. Layout paths survive reconnects and equivalent controllers.
    /// </summary>
    internal sealed class AlpineControllerInput
    {
        internal const string Prefix = "inputsystem:v1:";
        private const string GenericGamepad = "<Gamepad>";
        private int _captureStartFrame;
        private bool _captureArmed;

        internal void BeginCapture()
        {
            _captureStartFrame = Time.frameCount;
            _captureArmed = false;
        }

        internal bool CancelPressed()
        {
            foreach (InputDevice device in PhysicalControllers())
            {
                Gamepad gamepad = device as Gamepad;
                if (gamepad != null && gamepad.buttonEast != null && gamepad.buttonEast.wasPressedThisFrame)
                    return true;
                foreach (ButtonControl button in DeviceButtons(device))
                    if (IsCancelButton(device, button) && button.wasPressedThisFrame) return true;
            }
            return false;
        }

        internal bool TryCapture(out string binding)
        {
            binding = null;
            if (Time.frameCount <= _captureStartFrame)
                return false;

            // Do not capture the control that opened the binding page.
            if (!_captureArmed)
            {
                if (AnyBindableButtonPressed())
                    return false;
                _captureArmed = true;
                return false;
            }

            foreach (InputDevice device in PhysicalControllers())
            {
                foreach (ButtonControl button in DeviceButtons(device))
                {
                    if (button == null || !button.wasPressedThisFrame)
                        continue;
                    string path = PortablePath(device, button);
                    if (string.IsNullOrWhiteSpace(path))
                        continue;
                    binding = Prefix + path;
                    return true;
                }
            }
            return false;
        }

        internal bool BindingPressed(string binding)
        {
            if (string.IsNullOrWhiteSpace(binding))
                return false;
            if (!TryParse(binding, out string path))
            {
                return Enum.TryParse(binding, true, out KeyCode legacy) && Input.GetKeyDown(legacy);
            }

            using (var controls = InputSystem.FindControls<ButtonControl>(path))
            {
                foreach (ButtonControl button in controls)
                {
                    if (button != null && button.device != null && button.device.enabled &&
                        button.wasPressedThisFrame)
                        return true;
                }
            }
            return false;
        }

        internal bool BindingHeld(string binding)
        {
            if (string.IsNullOrWhiteSpace(binding))
                return false;
            if (!TryParse(binding, out string path))
            {
                return Enum.TryParse(binding, true, out KeyCode legacy) && Input.GetKey(legacy);
            }

            using (var controls = InputSystem.FindControls<ButtonControl>(path))
            {
                foreach (ButtonControl button in controls)
                {
                    if (button != null && button.device != null && button.device.enabled &&
                        button.isPressed)
                        return true;
                }
            }
            return false;
        }

        internal static string FormatBinding(string binding)
        {
            if (!TryParse(binding, out string path))
                return FormatLegacyOrRewired(binding);
            try
            {
                string display = InputControlPath.ToHumanReadableString(
                    path, InputControlPath.HumanReadableStringOptions.OmitDevice);
                if (!string.IsNullOrWhiteSpace(display))
                    return display;
            }
            catch { }
            int separator = path.LastIndexOf('/');
            return Humanize(separator >= 0 ? path.Substring(separator + 1) : path);
        }

        internal static bool TryMigrateRewiredBinding(string binding, out string migrated)
        {
            migrated = null;
            if (string.IsNullOrWhiteSpace(binding) ||
                !binding.StartsWith("rewired|", StringComparison.OrdinalIgnoreCase))
                return false;
            string[] parts = binding.Split('|');
            if (parts.Length != 4)
                return false;
            string name;
            try { name = Uri.UnescapeDataString(parts[3] ?? string.Empty); }
            catch { return false; }

            string control = null;
            switch (NormalizeButtonName(name))
            {
                case "cross": case "a": case "buttonsouth": control = "buttonSouth"; break;
                case "circle": case "b": case "buttoneast": return false;
                case "square": case "x": case "buttonwest": control = "buttonWest"; break;
                case "triangle": case "y": case "buttonnorth": control = "buttonNorth"; break;
                case "l1": case "leftbumper": case "leftshoulder": control = "leftShoulder"; break;
                case "r1": case "rightbumper": case "rightshoulder": control = "rightShoulder"; break;
                case "l2": case "lefttrigger": control = "leftTrigger"; break;
                case "r2": case "righttrigger": control = "rightTrigger"; break;
                case "l3": case "leftstick": case "leftstickpress": control = "leftStickPress"; break;
                case "r3": case "rightstick": case "rightstickpress": control = "rightStickPress"; break;
                case "options": case "start": case "menubutton": control = "start"; break;
                case "share": case "select": case "viewbutton": control = "select"; break;
                case "dpadup": control = "dpad/up"; break;
                case "dpaddown": control = "dpad/down"; break;
                case "dpadleft": control = "dpad/left"; break;
                case "dpadright": control = "dpad/right"; break;
                case "touchpad": case "touchpadbutton":
                    migrated = Prefix + "<DualShockGamepad>/touchpadButton";
                    return true;
            }
            if (string.IsNullOrWhiteSpace(control))
                return false;
            migrated = Prefix + GenericGamepad + "/" + control;
            return true;
        }

        internal static bool IsInputSystemBinding(string binding)
        {
            return TryParse(binding, out _);
        }

        private static bool TryParse(string binding, out string path)
        {
            path = null;
            if (string.IsNullOrWhiteSpace(binding) ||
                !binding.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            path = binding.Substring(Prefix.Length).Trim();
            return path.StartsWith("<", StringComparison.Ordinal) &&
                   path.IndexOf(">/", StringComparison.Ordinal) > 1;
        }

        private static bool AnyBindableButtonPressed()
        {
            foreach (InputDevice device in PhysicalControllers())
            {
                foreach (ButtonControl button in DeviceButtons(device))
                    if (button != null && button.isPressed) return true;
            }
            return false;
        }

        private static IEnumerable<InputDevice> PhysicalControllers()
        {
            foreach (InputDevice device in InputSystem.devices)
            {
                if (device == null || !device.enabled || device is Keyboard || device is Mouse)
                    continue;
                string layout = (device.layout ?? string.Empty).ToLowerInvariant();
                string description = ((device.displayName ?? string.Empty) + " " +
                                      (device.description.product ?? string.Empty)).ToLowerInvariant();
                if (device is Gamepad || device is Joystick ||
                    layout.Contains("gamepad") || layout.Contains("joystick") ||
                    layout.Contains("dualshock") || layout.Contains("dualsense") ||
                    layout.Contains("xinput") || description.Contains("controller") ||
                    description.Contains("gamepad") || description.Contains("joystick"))
                    yield return device;
            }
        }

        private static IEnumerable<ButtonControl> DeviceButtons(InputDevice device)
        {
            Gamepad gamepad = device as Gamepad;
            if (gamepad != null)
            {
                foreach (ButtonControl button in BindableButtons(gamepad))
                    yield return button;
                yield break;
            }
            var seen = new HashSet<InputControl>();
            foreach (InputControl control in device.allControls)
            {
                ButtonControl button = control as ButtonControl;
                if (button == null || button.synthetic || button.noisy ||
                    IsCancelButton(device, button) || !seen.Add(button))
                    continue;
                string relative = RelativePath(device, button);
                if (!string.IsNullOrWhiteSpace(relative))
                    yield return button;
            }
        }

        private static IEnumerable<ButtonControl> BindableButtons(Gamepad gamepad)
        {
            if (gamepad == null) yield break;
            var seen = new HashSet<InputControl>();
            ButtonControl[] canonical =
            {
                gamepad.buttonSouth, gamepad.buttonWest, gamepad.buttonNorth,
                gamepad.leftShoulder, gamepad.rightShoulder,
                gamepad.leftTrigger, gamepad.rightTrigger,
                gamepad.leftStickButton, gamepad.rightStickButton,
                gamepad.startButton, gamepad.selectButton,
                gamepad.dpad.up, gamepad.dpad.down, gamepad.dpad.left, gamepad.dpad.right
            };
            foreach (ButtonControl button in canonical)
            {
                if (button != null && seen.Add(button)) yield return button;
            }
            foreach (InputControl control in gamepad.allControls)
            {
                ButtonControl button = control as ButtonControl;
                if (IsBindableButton(gamepad, button) && seen.Add(button)) yield return button;
            }
        }

        private static bool IsBindableButton(Gamepad gamepad, ButtonControl button)
        {
            if (gamepad == null || button == null || button.synthetic || button.noisy)
                return false;
            if (ReferenceEquals(button, gamepad.buttonEast))
                return false;
            string relative = RelativePath(gamepad, button);
            return !string.IsNullOrWhiteSpace(relative) &&
                   !relative.EndsWith("/buttonEast", StringComparison.OrdinalIgnoreCase);
        }

        private static string PortablePath(InputDevice device, ButtonControl button)
        {
            string relative = RelativePath(device, button);
            if (string.IsNullOrWhiteSpace(relative))
                return null;
            Gamepad gamepad = device as Gamepad;
            string generic = gamepad != null ? GenericControlName(gamepad, button) : null;
            if (!string.IsNullOrWhiteSpace(generic))
                return GenericGamepad + "/" + generic;
            string layout = string.IsNullOrWhiteSpace(device.layout) ? "Joystick" : device.layout;
            return "<" + layout + ">/" + relative;
        }

        private static string GenericControlName(Gamepad gamepad, ButtonControl button)
        {
            if (ReferenceEquals(button, gamepad.buttonSouth)) return "buttonSouth";
            if (ReferenceEquals(button, gamepad.buttonWest)) return "buttonWest";
            if (ReferenceEquals(button, gamepad.buttonNorth)) return "buttonNorth";
            if (ReferenceEquals(button, gamepad.leftShoulder)) return "leftShoulder";
            if (ReferenceEquals(button, gamepad.rightShoulder)) return "rightShoulder";
            if (ReferenceEquals(button, gamepad.leftTrigger)) return "leftTrigger";
            if (ReferenceEquals(button, gamepad.rightTrigger)) return "rightTrigger";
            if (ReferenceEquals(button, gamepad.leftStickButton)) return "leftStickPress";
            if (ReferenceEquals(button, gamepad.rightStickButton)) return "rightStickPress";
            if (ReferenceEquals(button, gamepad.startButton)) return "start";
            if (ReferenceEquals(button, gamepad.selectButton)) return "select";
            if (ReferenceEquals(button, gamepad.dpad.up)) return "dpad/up";
            if (ReferenceEquals(button, gamepad.dpad.down)) return "dpad/down";
            if (ReferenceEquals(button, gamepad.dpad.left)) return "dpad/left";
            if (ReferenceEquals(button, gamepad.dpad.right)) return "dpad/right";
            return null;
        }

        private static string RelativePath(InputDevice device, InputControl control)
        {
            string devicePath = device?.path;
            string controlPath = control?.path;
            if (string.IsNullOrWhiteSpace(devicePath) || string.IsNullOrWhiteSpace(controlPath) ||
                !controlPath.StartsWith(devicePath + "/", StringComparison.OrdinalIgnoreCase))
                return null;
            return controlPath.Substring(devicePath.Length + 1);
        }

        private static bool IsCancelButton(InputDevice device, ButtonControl button)
        {
            if (button == null)
                return false;
            Gamepad gamepad = device as Gamepad;
            if (gamepad != null && ReferenceEquals(button, gamepad.buttonEast))
                return true;
            string name = NormalizeButtonName((button.name ?? string.Empty) + " " +
                                              (button.displayName ?? string.Empty) + " " +
                                              (button.shortDisplayName ?? string.Empty));
            return name.Contains("circle") || name.Contains("buttoneast");
        }

        private static string FormatLegacyOrRewired(string binding)
        {
            if (string.IsNullOrWhiteSpace(binding))
                return binding;
            if (!binding.StartsWith("rewired|", StringComparison.OrdinalIgnoreCase))
                return binding.StartsWith("Joystick", StringComparison.OrdinalIgnoreCase)
                    ? binding + " (Legacy - rebind recommended)"
                    : binding;
            string[] parts = binding.Split('|');
            if (parts.Length != 4)
                return "Controller binding (Rebind required)";
            try
            {
                string name = Uri.UnescapeDataString(parts[3]);
                return string.IsNullOrWhiteSpace(name) ? "Controller binding (Rebind required)" : name;
            }
            catch { return "Controller binding (Rebind required)"; }
        }

        private static string NormalizeButtonName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var characters = new List<char>(value.Length);
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character))
                    characters.Add(char.ToLowerInvariant(character));
            }
            return new string(characters.ToArray());
        }

        private static string Humanize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;
            var result = new List<char>(value.Length + 4) { char.ToUpperInvariant(value[0]) };
            for (int i = 1; i < value.Length; i++)
            {
                if (char.IsUpper(value[i]) && !char.IsWhiteSpace(value[i - 1]))
                    result.Add(' ');
                result.Add(value[i]);
            }
            return new string(result.ToArray());
        }
    }
}
