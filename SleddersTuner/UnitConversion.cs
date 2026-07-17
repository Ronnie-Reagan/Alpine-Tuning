namespace AlpineTuning
{
    internal static class UnitConversion
    {
        public const float MillimetersPerInch = 25.4f;
        public const float PoundsPerKilogram = 2.20462262f;
        public const float KilowattsPerHorsepower = 0.745699872f;
        public const float PoundFeetPerNewtonMeter = 0.737562149f;

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

        public static float KilogramsToPounds(float kilograms)
        {
            return kilograms * PoundsPerKilogram;
        }

        public static float HorsepowerToKilowatts(float horsepower)
        {
            return horsepower * KilowattsPerHorsepower;
        }

        public static string FormatWeight(float kilograms, AlpineDisplayUnits units)
        {
            return units == AlpineDisplayUnits.Imperial
                ? $"{KilogramsToPounds(kilograms):F0} lb"
                : $"{kilograms:F0} kg";
        }

        public static string FormatLengthFromMeters(float meters, AlpineDisplayUnits units)
        {
            float millimeters = meters * 1000f;
            return units == AlpineDisplayUnits.Imperial
                ? $"{MillimetersToInches(millimeters):F2}\""
                : $"{millimeters:F0} mm";
        }

        public static string FormatPower(float horsepower, AlpineDisplayUnits units)
        {
            return units == AlpineDisplayUnits.Imperial
                ? $"{horsepower:F0} hp"
                : $"{HorsepowerToKilowatts(horsepower):F0} kW";
        }

    }
}
