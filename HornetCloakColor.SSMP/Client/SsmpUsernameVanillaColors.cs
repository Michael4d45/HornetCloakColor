using SSMP.Game;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Mirrors <see cref="SSMP.Game.Client.PlayerManager"/> name colors so we can restore vanilla
    /// team tints when a player turns off custom username sync (white / removed).
    /// </summary>
    internal static class SsmpUsernameVanillaColors
    {
        internal static void ApplyTeamColor(Component? textMeshObject, Team team)
        {
            Color c;
            switch (team)
            {
                case Team.Moss:
                    c = new Color(0f / 255f, 150f / 255f, 0f / 255f);
                    break;
                case Team.Hive:
                    c = new Color(200f / 255f, 150f / 255f, 0f / 255f);
                    break;
                case Team.Grimm:
                    c = new Color(250f / 255f, 50f / 255f, 50f / 255f);
                    break;
                case Team.Lifeblood:
                    c = new Color(50f / 255f, 150f / 255f, 200f / 255f);
                    break;
                default:
                    c = Color.white;
                    break;
            }

            UsernameTmpCompat.SetColor(textMeshObject, c);
        }
    }
}
