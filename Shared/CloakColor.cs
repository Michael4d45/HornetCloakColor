using System;
using UnityEngine;

namespace HornetCloakColor.Shared
{
    /// <summary>
    /// Compact RGB color representation transmitted over the network.
    /// Stored as three bytes (0-255) to keep packets small.
    /// </summary>
    public readonly struct CloakColor : IEquatable<CloakColor>
    {
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public CloakColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        /// <summary>
        /// Default (no tint) — pure white multiplier keeps the original sprite colors intact.
        /// </summary>
        public static CloakColor Default => new CloakColor(255, 255, 255);

        public Color ToUnityColor() => new Color(R / 255f, G / 255f, B / 255f, 1f);

        public static CloakColor FromUnityColor(Color color)
        {
            return new CloakColor(
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255));
        }

        /// <summary>
        /// Parse a color string. Accepts "#RRGGBB", "RRGGBB", or "r,g,b" (decimal 0-255).
        /// Returns false on failure.
        /// </summary>
        public static bool TryParse(string value, out CloakColor color)
        {
            color = Default;
            if (string.IsNullOrWhiteSpace(value)) return false;

            var trimmed = value.Trim();

            if (trimmed.Contains(","))
            {
                var parts = trimmed.Split(',');
                if (parts.Length != 3) return false;
                if (!byte.TryParse(parts[0].Trim(), out var r)) return false;
                if (!byte.TryParse(parts[1].Trim(), out var g)) return false;
                if (!byte.TryParse(parts[2].Trim(), out var b)) return false;
                color = new CloakColor(r, g, b);
                return true;
            }

            var hex = trimmed.StartsWith("#") ? trimmed.Substring(1) : trimmed;
            if (hex.Length != 6) return false;

            try
            {
                var r2 = Convert.ToByte(hex.Substring(0, 2), 16);
                var g2 = Convert.ToByte(hex.Substring(2, 2), 16);
                var b2 = Convert.ToByte(hex.Substring(4, 2), 16);
                color = new CloakColor(r2, g2, b2);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

        public bool Equals(CloakColor other) => R == other.R && G == other.G && B == other.B;
        public override bool Equals(object obj) => obj is CloakColor other && Equals(other);
        public override int GetHashCode() => (R << 16) | (G << 8) | B;

        public static bool operator ==(CloakColor a, CloakColor b) => a.Equals(b);
        public static bool operator !=(CloakColor a, CloakColor b) => !a.Equals(b);
    }
}
