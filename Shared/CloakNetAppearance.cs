using System;
using UnityEngine;

namespace HornetCloakColor.Shared
{
    /// <summary>
    /// Cloak color plus texture saturation boost for SSMP sync (same semantics as the mod menu
    /// “texture saturation” slider on <c>CloakColorConfig</c>).
    /// </summary>
    public readonly struct CloakNetAppearance : IEquatable<CloakNetAppearance>
    {
        public CloakColor Color { get; }
        /// <summary>Multiplier × 100, clamped 0–200 (100 = 1.0).</summary>
        public byte TextureSaturationCenti { get; }

        public CloakNetAppearance(CloakColor color, byte textureSaturationCenti)
        {
            Color = color;
            TextureSaturationCenti = textureSaturationCenti;
        }

        public static CloakNetAppearance Default => new(CloakColor.Default, 100);

        public float TextureSaturationMultiplier => TextureSaturationCenti / 100f;

        public static byte CentiFromMultiplier(float multiplier) =>
            (byte)Mathf.Clamp(Mathf.RoundToInt(multiplier * 100f), 0, 200);

        public bool Equals(CloakNetAppearance other) =>
            Color.Equals(other.Color) && TextureSaturationCenti == other.TextureSaturationCenti;

        public override bool Equals(object? obj) => obj is CloakNetAppearance other && Equals(other);

        public override int GetHashCode() => (Color.GetHashCode() * 397) ^ TextureSaturationCenti;

        public static bool operator ==(CloakNetAppearance a, CloakNetAppearance b) => a.Equals(b);
        public static bool operator !=(CloakNetAppearance a, CloakNetAppearance b) => !a.Equals(b);
    }
}
