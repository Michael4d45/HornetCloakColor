using System.Collections.Generic;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Set of <see cref="Texture.GetInstanceID"/> values for atlases we have seen used by a
    /// known-Hornet renderer (i.e. one walked by <see cref="CloakRecolor"/> from inside the
    /// hero hierarchy). The scene scanner uses this to decide whether an "orphan" sprite
    /// elsewhere in the scene is also a Hornet sprite.
    ///
    /// Identifying Hornet atlases by name is unreliable because the game ships them as
    /// <c>atlas0</c> / <c>atlas1</c> / <c>atlas2</c> / <c>atlas3</c> — names also used by
    /// many non-Hornet sprite collections. Instance IDs uniquely identify the actual
    /// atlas asset in memory, so this gives an exact match without false positives.
    /// </summary>
    internal static class HornetTextureRegistry
    {
        private static readonly HashSet<int> _ids = new();
        private static readonly HashSet<int> _logged = new();

        public static int Count => _ids.Count;

        /// <summary>Returns true if this texture is newly registered.</summary>
        public static bool Register(Texture? tex)
        {
            if (tex == null) return false;
            var id = tex.GetInstanceID();
            if (!_ids.Add(id)) return false;

            if (CloakPaletteConfig.DebugLogging && _logged.Add(id))
                Log.Info($"[Registry] Registered Hornet texture '{tex.name}' (id={id}); total={_ids.Count}.");

            return true;
        }

        public static bool Contains(Texture? tex)
        {
            if (tex == null) return false;
            return _ids.Contains(tex.GetInstanceID());
        }
    }
}
