using System.Collections.Generic;
using HornetCloakColor.Shared;

namespace HornetCloakColor.Server
{
    /// <summary>
    /// Authoritative server-side cosmetics store (mirrors <c>SSMP.Essentials.Data.PlayerDataTracker</c> /
    /// <c>Server.Modules.Colors</c> pattern). Cloak entries include RGB + texture saturation centi.
    /// </summary>
    internal sealed class PlayerCosmeticsTracker
    {
        private readonly Dictionary<ushort, CloakNetAppearance> _cloaks = new();
        private readonly Dictionary<ushort, CloakColor> _usernameTints = new();

        internal IReadOnlyDictionary<ushort, CloakNetAppearance> Cloaks => _cloaks;
        internal IReadOnlyDictionary<ushort, CloakColor> UsernameTints => _usernameTints;

        internal void SetCloak(ushort playerId, CloakNetAppearance appearance) => _cloaks[playerId] = appearance;

        internal void SetUsernameTint(ushort playerId, bool hasCustomTint, CloakColor color)
        {
            if (!hasCustomTint)
                _usernameTints.Remove(playerId);
            else
                _usernameTints[playerId] = color;
        }

        internal void RemovePlayer(ushort playerId)
        {
            _cloaks.Remove(playerId);
            _usernameTints.Remove(playerId);
        }
    }
}
