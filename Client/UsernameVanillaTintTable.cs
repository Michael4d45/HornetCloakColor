using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Vanilla SSMP username colors (mirrors <c>PlayerManager.ChangeNameColor</c>) for local refresh
    /// when the user disables custom username tint.
    /// </summary>
    internal static class UsernameVanillaTintTable
    {
        internal static void Apply(Component? tmp, int teamOrdinal)
        {
            Color c;
            switch (teamOrdinal)
            {
                case 1: // Moss
                    c = new Color(0f / 255f, 150f / 255f, 0f / 255f);
                    break;
                case 2: // Hive
                    c = new Color(200f / 255f, 150f / 255f, 0f / 255f);
                    break;
                case 3: // Grimm
                    c = new Color(250f / 255f, 50f / 255f, 50f / 255f);
                    break;
                case 4: // Lifeblood
                    c = new Color(50f / 255f, 150f / 255f, 200f / 255f);
                    break;
                default:
                    c = Color.white;
                    break;
            }

            UsernameTmpCompat.SetColor(tmp, c);
        }
    }
}
