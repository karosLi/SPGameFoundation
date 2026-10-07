using System;

namespace SPF.Runtime.Configuration
{
    /// <summary>Cold authoring validation shared by source-isolated game configurations.
    /// No allocation budget or proof of runtime immutability is implied by these checks.</summary>
    public static class RuntimeConfigValidation
    {
        public static T Required<T>(T value, string field) where T : class =>
            value ?? throw new ArgumentException(field + " is required.");

        public static void Require(bool valid, string field)
        {
            if (!valid) throw new ArgumentException("Invalid configuration: " + field + ".");
        }

        public static void Finite(string field, params float[] values)
        {
            foreach (float value in values)
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new ArgumentException(field + " must be finite.");
        }

        // Use a wide intermediate before every downstream int product/addition. The bound leaves
        // room for the +1 sentinel used by spatial grids. It does not promise allocation success.
        public static int Length(long value, string field)
        {
            Require(value > 0 && value < int.MaxValue, field + " exceeds supported length");
            return (int)value;
        }

        public static void Grid(float width, float height, float cellSize, string field)
        {
            Finite(field, width, height, cellSize);
            Require(width > 0 && height > 0 && cellSize > 0, field);
            // Match the runtime float division before ceil; double intermediates could hide a
            // runtime overflow or produce a different boundary cell count.
            double x = Math.Ceiling(width / cellSize), y = Math.Ceiling(height / cellSize);
            Require(x > 0 && y > 0 && x * y < int.MaxValue, field + " cell count");
        }
    }
}
