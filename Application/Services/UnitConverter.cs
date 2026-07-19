namespace Application.Services
{
    /// <summary>
    /// Converts quantities between compatible units for recipe consumption.
    ///
    /// Why: Ingredient.Unit ("kg", "l", ...) is the canonical unit for
    /// stock and cost. RecipeLine.Unit can now differ ("g", "ml", ...) so
    /// the chef can express recipes naturally. Before touching stock or
    /// computing cost we normalise everything to the ingredient's Unit.
    ///
    /// Rules:
    ///   - Same unit  → no-op
    ///   - g ↔ kg     → ×1000 / ÷1000
    ///   - ml ↔ l     → ×1000 / ÷1000
    ///   - pcs        → no conversion possible, only pcs ↔ pcs
    ///   - Anything else → returns the quantity unchanged and reports
    ///     `converted=false` so the caller can log a warning.
    ///
    /// Comparisons are case-insensitive so "kg", "Kg", "KG" all work.
    /// </summary>
    public static class UnitConverter
    {
        /// <summary>
        /// Convert <paramref name="qty"/> from <paramref name="fromUnit"/>
        /// into <paramref name="toUnit"/>. Returns the original qty
        /// unchanged with <paramref name="converted"/> = false if the units
        /// aren't compatible — caller can then log or fall back.
        /// </summary>
        public static decimal Convert(decimal qty, string? fromUnit, string? toUnit, out bool converted)
        {
            converted = true;

            var f = Normalise(fromUnit);
            var t = Normalise(toUnit);

            if (f == t) return qty;                      // same unit
            if (string.IsNullOrEmpty(f) || string.IsNullOrEmpty(t))
            {
                // Missing unit somewhere — treat as no-op to preserve
                // legacy behavior; caller decides whether to warn.
                converted = false;
                return qty;
            }

            // Mass
            if (f == "g"  && t == "kg") return qty / 1000m;
            if (f == "kg" && t == "g")  return qty * 1000m;

            // Volume
            if (f == "ml" && t == "l")  return qty / 1000m;
            if (f == "l"  && t == "ml") return qty * 1000m;

            // Unsupported pair (e.g. g → l, pcs → kg). Bail out safely.
            converted = false;
            return qty;
        }

        /// <summary>
        /// Convenience for cases where the caller has already ensured the
        /// units are compatible.
        /// </summary>
        public static decimal Convert(decimal qty, string? fromUnit, string? toUnit)
            => Convert(qty, fromUnit, toUnit, out _);

        private static string Normalise(string? unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) return "";
            var u = unit.Trim().ToLowerInvariant();
            // Common aliases people write in the ingredient/unit picker.
            return u switch
            {
                "gram" or "grams"        => "g",
                "kilo" or "kilos"
                    or "kilogram" or "kilograms" => "kg",
                "milliliter" or "milliliters"
                    or "millilitre" or "millilitres" => "ml",
                "liter" or "liters"
                    or "litre" or "litres" => "l",
                "piece" or "pieces" or "pc" or "pieces." => "pcs",
                _ => u,
            };
        }
    }
}
