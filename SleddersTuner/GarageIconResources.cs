using MelonLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace AlpineTuning
{
    /// <summary>
    /// Decodes embedded PNG artwork through Unity's image decoder. This keeps PNG
    /// row orientation intact and avoids the upside-down raw RGBA path used by the
    /// deprecated Alpine garage logo.
    /// </summary>
    internal static class GarageIconResources
    {
        private const string GaragePrefix = "AlpineTuning.GarageIcons.";
        private const string BrandMarkResource = "AlpineTuning.Brand.Mark.png";

        private static readonly Dictionary<string, Texture2D> Cache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> MissingResources =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> IconAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "settings.metric", "settings.display" },
                { "settings.imperial", "settings.display" },
                { "settings.enabled", "settings.hotkey" },
                { "settings.disabled", "settings.hotkey" },
                { "settings.keyboard", "settings.hotkey" },
                { "settings.controller", "settings.hotkey" },
                { "settings.clear", "settings.hotkey" },
                { "settings.confirm-clear", "settings.hotkey" },
                { "part.brake.stock", "type.brake-calibration" },
                { "part.brake.progressive", "type.brake-calibration" },
                { "part.brake.trail", "type.brake-calibration" },
                { "part.brake.aggressive", "type.brake-calibration" },
                { "part.geometry.stock", "type.steering-geometry" },
                { "part.geometry.reduced_toe", "type.steering-geometry" },
                { "part.geometry.increased_toe", "type.steering-geometry" },
                { "part.geometry.responsive", "type.steering-geometry" }
            };
        private static MethodInfo _loadImageMethod;

        public static Texture2D LoadGarageIcon(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            string requested = key.Trim();
            string resourceKey = IconAliases.TryGetValue(requested, out string alias)
                ? alias
                : requested;
            // Aliases intentionally share one decoded texture. Caching by the
            // requested tile key decoded the same 400x320 PNG once per alias.
            return Load(resourceKey, GaragePrefix + resourceKey + ".png");
        }

        public static Texture2D LoadBrandMark()
        {
            return Load("brand.mark", BrandMarkResource);
        }

        public static void Release()
        {
            foreach (Texture2D texture in Cache.Values)
            {
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
            }

            Cache.Clear();
            MissingResources.Clear();
            _loadImageMethod = null;
        }

        private static Texture2D Load(string cacheKey, string resourceName)
        {
            if (Cache.TryGetValue(cacheKey, out Texture2D cached) && cached != null)
                return cached;

            Assembly assembly = typeof(GarageIconResources).Assembly;
            try
            {
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        LogMissingOnce(resourceName);
                        return null;
                    }

                    var bytes = new byte[stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0)
                            break;
                        offset += read;
                    }

                    if (offset != bytes.Length)
                        throw new EndOfStreamException($"Read {offset} of {bytes.Length} bytes.");

                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        name = "AlpineGarageIcon-" + cacheKey,
                        hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    if (!DecodePng(texture, bytes))
                    {
                        UnityEngine.Object.Destroy(texture);
                        throw new InvalidDataException("Unity rejected the embedded PNG data.");
                    }

                    Cache[cacheKey] = texture;
                    return texture;
                }
            }
            catch (Exception ex)
            {
                if (MissingResources.Add(resourceName))
                    MelonLogger.Warning($"Garage icon '{resourceName}' could not be loaded: {ex.GetType().Name}");
                return null;
            }
        }

        private static void LogMissingOnce(string resourceName)
        {
            if (MissingResources.Add(resourceName))
                MelonLogger.Warning($"Garage icon resource is missing: {resourceName}");
        }

        private static bool DecodePng(Texture2D texture, byte[] bytes)
        {
            if (_loadImageMethod == null)
            {
                Type imageConversion = Type.GetType(
                    "UnityEngine.ImageConversion, UnityEngine.ImageConversionModule",
                    false);
                _loadImageMethod = imageConversion?.GetMethod(
                    "LoadImage",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) },
                    null);
            }

            if (_loadImageMethod == null)
                throw new MissingMethodException(
                    "UnityEngine.ImageConversion.LoadImage(Texture2D, Byte[], Boolean)");

            object decoded = _loadImageMethod.Invoke(null, new object[] { texture, bytes, true });
            return decoded is bool success && success;
        }
    }
}
