using MelonLoader;
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AlpineTuning
{
    internal struct AlpineHeadPose
    {
        public Vector3 positionMeters;
        public Vector3 rotationDegrees;

        public static AlpineHeadPose operator -(AlpineHeadPose left, AlpineHeadPose right)
        {
            return new AlpineHeadPose
            {
                positionMeters = left.positionMeters - right.positionMeters,
                rotationDegrees = left.rotationDegrees - right.rotationDegrees
            };
        }
    }

    internal interface IAlpineHeadTrackingProvider : IDisposable
    {
        string Name { get; }
        bool IsAvailable { get; }
        bool TryRead(out AlpineHeadPose pose);
    }

    internal sealed class AlpineHeadTrackingPoseFilter
    {
        private AlpineHeadPose _center;
        private AlpineHeadPose _pose;
        private bool _hasCenter;

        internal bool HasPose { get; private set; }
        internal AlpineHeadPose Pose => _pose;

        internal AlpineHeadPose Update(AlpineHeadPose raw, HeadTrackingSettings settings, float blend)
        {
            if (!_hasCenter)
            {
                _center = raw;
                _hasCenter = true;
            }

            AlpineHeadPose relative = AlpineHeadTrackingSystem.TransformRelativePose(raw, _center, settings);
            blend = Mathf.Clamp01(blend);
            if (!HasPose)
                _pose = relative;
            else
            {
                _pose.positionMeters = Vector3.Lerp(_pose.positionMeters, relative.positionMeters, blend);
                _pose.rotationDegrees = Vector3.Lerp(_pose.rotationDegrees, relative.rotationDegrees, blend);
            }
            HasPose = true;
            return _pose;
        }

        internal AlpineHeadPose Update(AlpineHeadPose raw, float blend)
        {
            if (!_hasCenter)
            {
                _center = raw;
                _hasCenter = true;
            }
            AlpineHeadPose relative = AlpineHeadTrackingSystem.ClampRelativePose(raw, _center);
            blend = Mathf.Clamp01(blend);
            if (!HasPose) _pose = relative;
            else
            {
                _pose.positionMeters = Vector3.Lerp(_pose.positionMeters, relative.positionMeters, blend);
                _pose.rotationDegrees = Vector3.Lerp(_pose.rotationDegrees, relative.rotationDegrees, blend);
            }
            HasPose = true;
            return _pose;
        }

        internal void Recenter()
        {
            _hasCenter = false;
            HasPose = false;
            _pose = default;
        }
    }

    internal struct AlpineCameraPoseState
    {
        private Vector3 _position;
        private Quaternion _rotation;
        internal bool IsCaptured { get; private set; }

        internal void Capture(Vector3 position, Quaternion rotation)
        {
            _position = position;
            _rotation = rotation;
            IsCaptured = true;
        }

        internal Vector3 ApplyPosition(Vector3 offset) => _position + offset;
        internal Quaternion ApplyRotation(Vector3 offsetDegrees) =>
            _rotation * Quaternion.Euler(offsetDegrees);

        internal bool TryRestore(out Vector3 position, out Quaternion rotation)
        {
            position = _position;
            rotation = _rotation;
            bool captured = IsCaptured;
            IsCaptured = false;
            return captured;
        }
    }

    /// <summary>
    /// Optional late camera offset. Providers are loaded dynamically so a normal
    /// Alpine install never gains a hard TrackIR/OpenTrack dependency.
    /// </summary>
    internal sealed class AlpineHeadTrackingSystem
    {
        private readonly AlpineTuningMod _mod;
        private IAlpineHeadTrackingProvider _provider;
        private AlpineHeadTrackingSource _selectedSource = (AlpineHeadTrackingSource)(-1);
        private readonly AlpineHeadTrackingPoseFilter _filter = new AlpineHeadTrackingPoseFilter();
        private Camera _appliedCamera;
        private AlpineCameraPoseState _cameraPose;
        private bool _poseApplied;
        private Vector2 _leanOutput;
        private Vector2 _leanInputSnapshot;
        private bool _hasLeanInputSnapshot;
        private SnowmobileController _leanController;
        private float _nextProviderProbe;
        private string _status = "Disabled";

        internal AlpineHeadTrackingSystem(AlpineTuningMod mod)
        {
            _mod = mod;
        }

        internal string Status =>
            _mod.Settings.headTrackingEnabled &&
            (AlpineNativeUi.HasAttachedMenus || Time.timeScale <= 0.0001f)
                ? "Suspended in menu"
                : _status;
        internal string ActiveSource => _provider != null ? _provider.Name : "None";
        internal AlpineHeadPose LivePose => _filter.HasPose ? _filter.Pose : default;

        internal void Update()
        {
            RestoreCameraPose();
            if (!_mod.Settings.headTrackingEnabled)
            {
                _status = "Disabled";
                DisposeProvider();
                return;
            }

            AlpineHeadTrackingSource requested = _mod.Settings.headTrackingSource;
            if (_provider == null || requested != _selectedSource)
            {
                if (Time.unscaledTime < _nextProviderProbe && requested == _selectedSource)
                    return;
                SelectProvider(requested);
            }

            if (_provider == null || !_provider.IsAvailable)
            {
                _status = "No provider detected";
                _nextProviderProbe = Time.unscaledTime + 2f;
                DisposeProvider();
                return;
            }

            if (!_provider.TryRead(out AlpineHeadPose raw))
            {
                _status = _provider.Name + " waiting for pose";
                return;
            }

            HeadTrackingSettings settings = _mod.Settings.headTracking ?? new HeadTrackingSettings();
            settings.Normalize();
            float blend = 1f - Mathf.Exp(-settings.smoothingResponse * Mathf.Max(0f, Time.unscaledDeltaTime));
            AlpineHeadPose pose = _filter.Update(raw, settings, blend);
            UpdateLeanOutput(pose, settings.lean);
            _status = _provider.Name + " active";
        }

        internal void LateUpdate()
        {
            if (!_mod.Settings.headTrackingEnabled || !_filter.HasPose || AlpineTuningMod.ActiveController == null ||
                AlpineNativeUi.HasAttachedMenus || Time.timeScale <= 0.0001f ||
                (_mod.ExperimentalSystems != null && _mod.ExperimentalSystems.IsWalking &&
                 !(_mod.Settings.headTracking?.allowWhileWalking ?? false)))
            {
                return;
            }

            Camera camera = ResolveActiveCamera();
            if (camera == null || !camera.isActiveAndEnabled)
                return;

            if (!CameraModeEnabled(camera, _mod.Settings.headTracking))
                return;

            _appliedCamera = camera;
            _cameraPose.Capture(camera.transform.localPosition, camera.transform.localRotation);
            camera.transform.localPosition = _cameraPose.ApplyPosition(_filter.Pose.positionMeters);
            camera.transform.localRotation = _cameraPose.ApplyRotation(_filter.Pose.rotationDegrees);
            _poseApplied = true;
        }

        internal void Recenter()
        {
            _filter.Recenter();
            _status = _provider != null ? _provider.Name + " recentering" : "No provider detected";
        }

        internal void Suspend()
        {
            RestoreLeanInput();
            RestoreCameraPose();
            _filter.Recenter();
            DisposeProvider();
            _status = "Suspended";
        }

        internal void Shutdown()
        {
            Suspend();
        }

        private void SelectProvider(AlpineHeadTrackingSource source)
        {
            DisposeProvider();
            _selectedSource = source;
            _nextProviderProbe = Time.unscaledTime + 2f;
            _provider = SelectAvailableProvider(
                source,
                () => new TrackIrProvider(),
                () => new OpenTrackProvider());

            _filter.Recenter();
            _status = _provider != null ? _provider.Name + " detected" : "No provider detected";
        }

        internal static IAlpineHeadTrackingProvider SelectAvailableProvider(
            AlpineHeadTrackingSource source,
            Func<IAlpineHeadTrackingProvider> createTrackIr,
            Func<IAlpineHeadTrackingProvider> createOpenTrack)
        {
            if (source == AlpineHeadTrackingSource.Auto || source == AlpineHeadTrackingSource.TrackIr)
            {
                IAlpineHeadTrackingProvider candidate = SafeCreate(createTrackIr);
                if (candidate != null && candidate.IsAvailable)
                    return candidate;
                try { candidate?.Dispose(); } catch { }
            }

            if (source == AlpineHeadTrackingSource.Auto || source == AlpineHeadTrackingSource.OpenTrack)
            {
                IAlpineHeadTrackingProvider candidate = SafeCreate(createOpenTrack);
                if (candidate != null && candidate.IsAvailable)
                    return candidate;
                try { candidate?.Dispose(); } catch { }
            }

            return null;
        }

        internal static AlpineHeadPose ClampRelativePose(AlpineHeadPose raw, AlpineHeadPose center)
        {
            AlpineHeadPose relative = raw - center;
            relative.positionMeters = new Vector3(
                Mathf.Clamp(relative.positionMeters.x, -0.20f, 0.20f),
                Mathf.Clamp(relative.positionMeters.y, -0.20f, 0.20f),
                Mathf.Clamp(relative.positionMeters.z, -0.20f, 0.20f));
            relative.rotationDegrees = new Vector3(
                Mathf.Clamp(Mathf.DeltaAngle(0f, relative.rotationDegrees.x), -45f, 45f),
                Mathf.Clamp(Mathf.DeltaAngle(0f, relative.rotationDegrees.y), -60f, 60f),
                Mathf.Clamp(Mathf.DeltaAngle(0f, relative.rotationDegrees.z), -30f, 30f));
            return relative;
        }

        internal static AlpineHeadPose TransformRelativePose(
            AlpineHeadPose raw,
            AlpineHeadPose center,
            HeadTrackingSettings settings)
        {
            settings = settings ?? new HeadTrackingSettings();
            settings.Normalize();
            AlpineHeadPose relative = raw - center;
            relative.rotationDegrees = new Vector3(
                Mathf.DeltaAngle(0f, relative.rotationDegrees.x),
                Mathf.DeltaAngle(0f, relative.rotationDegrees.y),
                Mathf.DeltaAngle(0f, relative.rotationDegrees.z));

            relative.positionMeters = ProcessVector(
                relative.positionMeters,
                settings.translationSensitivity,
                settings.translationDeadzoneMeters,
                settings.translationClampMeters,
                settings.motionCurve,
                settings.invertTranslationX,
                settings.invertTranslationY,
                settings.invertTranslationZ);
            relative.rotationDegrees = ProcessVector(
                relative.rotationDegrees,
                settings.rotationSensitivity,
                settings.rotationDeadzoneDegrees,
                settings.rotationClampDegrees,
                settings.motionCurve,
                settings.invertPitch,
                settings.invertYaw,
                settings.invertRoll);
            return relative;
        }

        internal void BeforeSimulation(SnowmobileController controller)
        {
            RestoreLeanInput();
            HeadTrackingSettings tracking = _mod.Settings.headTracking;
            if (controller == null || controller != AlpineTuningMod.ActiveController ||
                !_mod.Settings.headTrackingEnabled || tracking == null || tracking.lean == null ||
                !tracking.lean.enabled || !_filter.HasPose)
                return;

            object input = SleddersGameBindings.GetFieldValue<object>(controller, "GJKCDNOBELI");
            if (input == null || !SleddersGameBindings.TryGetFieldValue(input, "AKLGOILBLKI", out Vector2 nativeLean))
                return;

            Vector2 addition = _leanOutput;
            bool hasGrounded = SleddersGameBindings.TryGetFieldValue(controller, "isGrounded", out bool grounded);
            if (hasGrounded && !grounded)
                addition *= tracking.lean.airborneMultiplier;
            Vector2 combined = new Vector2(
                Mathf.Clamp(nativeLean.x + addition.x, -1f, 1f),
                Mathf.Clamp(nativeLean.y + addition.y, -1f, 1f));
            _leanInputSnapshot = nativeLean;
            _hasLeanInputSnapshot = true;
            _leanController = controller;
            if (!SleddersGameBindings.SetFieldValue(input, "AKLGOILBLKI", combined) ||
                !SleddersGameBindings.SetFieldValue(controller, "GJKCDNOBELI", input))
            {
                _hasLeanInputSnapshot = false;
                _leanController = null;
            }
        }

        internal void AfterSimulation(SnowmobileController controller)
        {
            if (_leanController == controller)
                RestoreLeanInput();
        }

        private void UpdateLeanOutput(AlpineHeadPose pose, TrackingLeanSettings settings)
        {
            if (settings == null || !settings.enabled)
            {
                _leanOutput = Vector2.zero;
                return;
            }

            float side = pose.positionMeters.x / 0.20f * settings.lateralTranslationGain +
                         pose.rotationDegrees.z / 30f * settings.rollGain;
            float foreAft = -pose.positionMeters.z / 0.20f * settings.forwardTranslationGain +
                            pose.rotationDegrees.x / 45f * settings.pitchGain;
            side = ApplyLeanDeadzone(side, settings.deadzone);
            foreAft = ApplyLeanDeadzone(foreAft, settings.deadzone);
            Vector2 target = Vector2.ClampMagnitude(new Vector2(side, foreAft), settings.maximumOutput);
            float blend = 1f - Mathf.Exp(-settings.smoothingResponse * Mathf.Max(0f, Time.unscaledDeltaTime));
            _leanOutput = Vector2.Lerp(_leanOutput, target, blend);
        }

        private static float ApplyLeanDeadzone(float value, float deadzone)
        {
            float magnitude = Mathf.Abs(value);
            if (magnitude <= deadzone)
                return 0f;
            return Mathf.Sign(value) * (magnitude - deadzone) / Mathf.Max(0.0001f, 1f - deadzone);
        }

        private void RestoreLeanInput()
        {
            if (_leanController != null && _hasLeanInputSnapshot)
            {
                object current = SleddersGameBindings.GetFieldValue<object>(_leanController, "GJKCDNOBELI");
                if (current != null && SleddersGameBindings.SetFieldValue(current, "AKLGOILBLKI", _leanInputSnapshot))
                    SleddersGameBindings.SetFieldValue(_leanController, "GJKCDNOBELI", current);
            }
            _hasLeanInputSnapshot = false;
            _leanController = null;
        }

        private static Camera ResolveActiveCamera()
        {
            Camera best = null;
            float bestDepth = float.MinValue;
            foreach (Camera camera in Camera.allCameras)
            {
                if (camera == null || !camera.isActiveAndEnabled || camera.targetTexture != null)
                    continue;
                string name = camera.name ?? string.Empty;
                if (name.IndexOf("preview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("ui", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (camera.depth >= bestDepth)
                {
                    best = camera;
                    bestDepth = camera.depth;
                }
            }
            return best;
        }

        private static bool CameraModeEnabled(Camera camera, HeadTrackingSettings settings)
        {
            if (settings == null)
                return true;
            string path = HierarchyName(camera != null ? camera.transform : null);
            if (path.Contains("drone") || path.Contains("freecam"))
                return settings.droneMode;
            if (path.Contains("first") || path.Contains("fpv"))
                return settings.firstPerson;
            if (path.Contains("far") || path.Contains("thirdperson"))
                return settings.thirdPersonFar;
            return settings.thirdPersonNear;
        }

        private static string HierarchyName(Transform transform)
        {
            string path = string.Empty;
            for (Transform current = transform; current != null; current = current.parent)
                path = (current.name + "/" + path).ToLowerInvariant();
            return path;
        }

        private static Vector3 ProcessVector(
            Vector3 value,
            Vec3Data sensitivity,
            Vec3Data deadzone,
            Vec3Data clamp,
            float curve,
            bool invertX,
            bool invertY,
            bool invertZ)
        {
            return new Vector3(
                ProcessAxis(value.x, sensitivity.x, deadzone.x, clamp.x, curve, invertX),
                ProcessAxis(value.y, sensitivity.y, deadzone.y, clamp.y, curve, invertY),
                ProcessAxis(value.z, sensitivity.z, deadzone.z, clamp.z, curve, invertZ));
        }

        private static float ProcessAxis(float value, float sensitivity, float deadzone, float clamp, float curve, bool invert)
        {
            float sign = value < 0f ? -1f : 1f;
            float magnitude = Mathf.Max(0f, Mathf.Abs(value) - deadzone) * Mathf.Max(0f, sensitivity);
            float normalized = clamp > 0.0001f ? Mathf.Clamp01(magnitude / clamp) : 0f;
            float curved = Mathf.Pow(normalized, Mathf.Clamp(curve, 0.5f, 3f)) * clamp;
            return Mathf.Clamp(curved * sign * (invert ? -1f : 1f), -clamp, clamp);
        }

        private static IAlpineHeadTrackingProvider SafeCreate(
            Func<IAlpineHeadTrackingProvider> factory)
        {
            if (factory == null)
                return null;
            try { return factory(); } catch { return null; }
        }

        private void RestoreCameraPose()
        {
            if (!_poseApplied)
                return;
            if (_cameraPose.TryRestore(out Vector3 position, out Quaternion rotation) &&
                _appliedCamera != null)
            {
                _appliedCamera.transform.localPosition = position;
                _appliedCamera.transform.localRotation = rotation;
            }
            _poseApplied = false;
            _appliedCamera = null;
        }

        private void DisposeProvider()
        {
            try { _provider?.Dispose(); } catch { }
            _provider = null;
        }

        private sealed class OpenTrackProvider : IAlpineHeadTrackingProvider
        {
            private MemoryMappedFile _map;
            private MemoryMappedViewAccessor _view;
            public string Name => "OpenTrack / FreeTrack";
            public bool IsAvailable => _view != null;

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            private struct FreeTrackData
            {
                public int dataId;
                public int camWidth;
                public int camHeight;
                public float yaw;
                public float pitch;
                public float roll;
                public float x;
                public float y;
                public float z;
            }

            public OpenTrackProvider()
            {
                try
                {
                    _map = MemoryMappedFile.OpenExisting("FT_SharedMem", MemoryMappedFileRights.Read);
                    _view = _map.CreateViewAccessor(0, Marshal.SizeOf(typeof(FreeTrackData)), MemoryMappedFileAccess.Read);
                }
                catch
                {
                    Dispose();
                }
            }

            public bool TryRead(out AlpineHeadPose pose)
            {
                pose = default;
                if (_view == null)
                    return false;
                try
                {
                    _view.Read(0, out FreeTrackData data);
                    pose.positionMeters = new Vector3(data.x, data.y, -data.z) * 0.001f;
                    pose.rotationDegrees = new Vector3(-data.pitch, data.yaw, -data.roll) * Mathf.Rad2Deg;
                    return IsFinite(pose.positionMeters) && IsFinite(pose.rotationDegrees);
                }
                catch { return false; }
            }

            public void Dispose()
            {
                try { _view?.Dispose(); } catch { }
                try { _map?.Dispose(); } catch { }
                _view = null;
                _map = null;
            }
        }

        private sealed class TrackIrProvider : IAlpineHeadTrackingProvider
        {
            private IntPtr _module;
            private NpGetData _getData;
            private NpRegisterWindowHandle _registerWindow;
            private NpUnregisterWindowHandle _unregisterWindow;
            private NpRequestData _requestData;
            private NpStartDataTransmission _startTransmission;
            private NpStopDataTransmission _stopTransmission;
            private bool _registered;
            private bool _transmitting;

            public string Name => "TrackIR / NPClient";
            public bool IsAvailable => _module != IntPtr.Zero && _getData != null;

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int NpGetData(ref TrackIrData data);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int NpRegisterWindowHandle(IntPtr window);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int NpUnregisterWindowHandle();
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int NpRequestData(ushort fields);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int NpStartDataTransmission();
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int NpStopDataTransmission();

            [StructLayout(LayoutKind.Sequential, Pack = 2)]
            private struct TrackIrData
            {
                public ushort status;
                public ushort frameSignature;
                public uint checksum;
                public float roll;
                public float pitch;
                public float yaw;
                public float x;
                public float y;
                public float z;
                public float rawX;
                public float rawY;
                public float rawZ;
                public float deltaX;
                public float deltaY;
                public float deltaZ;
                public float smoothX;
                public float smoothY;
                public float smoothZ;
            }

            public TrackIrProvider()
            {
                foreach (string candidate in CandidateLibraries())
                {
                    if (string.IsNullOrWhiteSpace(candidate))
                        continue;
                    _module = LoadLibrary(candidate);
                    if (_module != IntPtr.Zero)
                        break;
                }
                if (_module == IntPtr.Zero)
                    return;

                _getData = GetDelegate<NpGetData>("NP_GetData");
                _registerWindow = GetDelegate<NpRegisterWindowHandle>("NP_RegisterWindowHandle");
                _unregisterWindow = GetDelegate<NpUnregisterWindowHandle>("NP_UnregisterWindowHandle");
                _requestData = GetDelegate<NpRequestData>("NP_RequestData");
                _startTransmission = GetDelegate<NpStartDataTransmission>("NP_StartDataTransmission");
                _stopTransmission = GetDelegate<NpStopDataTransmission>("NP_StopDataTransmission");
                if (_getData == null || _registerWindow == null || _requestData == null ||
                    _startTransmission == null)
                {
                    Dispose();
                    return;
                }
                try
                {
                    IntPtr window = GetActiveWindow();
                    _registered = window != IntPtr.Zero && _registerWindow(window) == 0;
                    const ushort sixAxisFields = 0x0077;
                    _transmitting = _registered && _requestData(sixAxisFields) == 0 &&
                                    _startTransmission() == 0;
                    if (!_transmitting)
                        Dispose();
                }
                catch { Dispose(); }
            }

            public bool TryRead(out AlpineHeadPose pose)
            {
                pose = default;
                if (_getData == null)
                    return false;
                try
                {
                    var data = new TrackIrData();
                    if (_getData(ref data) != 0 || data.status != 0)
                        return false;
                    const float rotationScale = 180f / 16383f;
                    const float translationScale = 0.00003052f;
                    pose.rotationDegrees = new Vector3(-data.pitch, data.yaw, -data.roll) * rotationScale;
                    pose.positionMeters = new Vector3(data.x, data.y, -data.z) * translationScale;
                    return IsFinite(pose.positionMeters) && IsFinite(pose.rotationDegrees);
                }
                catch { return false; }
            }

            public void Dispose()
            {
                if (_transmitting)
                {
                    try { _stopTransmission?.Invoke(); } catch { }
                }
                _transmitting = false;
                if (_registered)
                {
                    try { _unregisterWindow?.Invoke(); } catch { }
                }
                _registered = false;
                _getData = null;
                _registerWindow = null;
                _unregisterWindow = null;
                _requestData = null;
                _startTransmission = null;
                _stopTransmission = null;
                if (_module != IntPtr.Zero)
                {
                    try { FreeLibrary(_module); } catch { }
                    _module = IntPtr.Zero;
                }
            }

            private T GetDelegate<T>(string name) where T : class
            {
                IntPtr address = GetProcAddress(_module, name);
                return address == IntPtr.Zero
                    ? null
                    : Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
            }

            private static string[] CandidateLibraries()
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                return new[]
                {
                    "NPClient64.dll",
                    "NPClient.dll",
                    Path.Combine(programFiles ?? string.Empty, "NaturalPoint", "TrackIR5", "NPClient64.dll"),
                    Path.Combine(x86 ?? string.Empty, "NaturalPoint", "TrackIR5", "NPClient.dll")
                };
            }

            [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr LoadLibrary(string fileName);
            [DllImport("kernel32", SetLastError = true)]
            private static extern IntPtr GetProcAddress(IntPtr module, string name);
            [DllImport("kernel32")]
            private static extern bool FreeLibrary(IntPtr module);
            [DllImport("user32")]
            private static extern IntPtr GetActiveWindow();
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
