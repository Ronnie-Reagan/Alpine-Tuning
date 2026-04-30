namespace AlpineTuning
{
    internal static class UnitConversion
    {
        public const float MillimetersPerInch = 25.4f;
        public const float KilopascalsPerPsi = 6.89476f;

        public static float InchesToMillimeters(float inches)
        {
            return inches * MillimetersPerInch;
        }

        public static float MillimetersToInches(float millimeters)
        {
            return millimeters / MillimetersPerInch;
        }

        public static string FormatInchesAndMillimeters(float millimeters)
        {
            return $"{MillimetersToInches(millimeters):F2}\" / {millimeters:F1} mm";
        }
    }
}
