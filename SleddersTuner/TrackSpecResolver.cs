using UnityEngine;

namespace AlpineTuning
{
    internal static class TrackSpecResolver
    {
        public static bool HasExplicitPaddleHeight(PartEffect effect)
        {
            return effect != null && effect.lugHeightTargetMm > 0.01f;
        }

        public static float ResolveLugHeightMillimeters(SledDefaults baseDefaults, PartEffect effect)
        {
            if (baseDefaults == null)
                return 0f;

            if (effect == null)
                return baseDefaults.lugHeight;

            if (HasExplicitPaddleHeight(effect))
                return Mathf.Max(0f, effect.lugHeightTargetMm + effect.lugHeightOffset);

            return baseDefaults.lugHeight * effect.lugHeightMultiplier + effect.lugHeightOffset;
        }

        public static string FormatPaddleHeight(float lugHeightMillimeters)
        {
            return UnitConversion.FormatInchesAndMillimeters(lugHeightMillimeters);
        }
    }
}
