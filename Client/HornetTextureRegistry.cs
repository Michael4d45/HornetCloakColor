using System.Collections.Generic;
using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// <see cref="Texture.GetInstanceID"/> values for atlases seen on the local hero
    /// (<see cref="CloakRecolor"/>) for diagnostics / <see cref="TextureDumper"/>.
    /// <see cref="CloakSceneScanner"/> no longer uses this; orphan sprites match only
    /// <c>cloak_palette.json</c> allowlist substrings.
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
