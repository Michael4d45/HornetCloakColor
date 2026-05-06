using System;
using System.Reflection;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// SSMP is compiled against the game's TextMeshPro type; our plugin may reference a different TMPro
    /// assembly identity. Resolve username labels and colors without tying to one TextMeshPro CLR type.
    /// </summary>
    internal static class UsernameTmpCompat
    {
        private static readonly BindingFlags PropFlags =
            BindingFlags.Public | BindingFlags.Instance;

        /// <summary>First component named <c>TextMeshPro</c> on the Username object under <paramref name="root"/>.</summary>
        internal static Component? FindUnderHero(GameObject root)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (tr.name != "Username") continue;
                return FindOnGameObject(tr.gameObject);
            }

            return null;
        }

        internal static Component? FindOnGameObject(GameObject usernameObject)
        {
            foreach (var comp in usernameObject.GetComponents<Component>())
            {
                if (comp != null && comp.GetType().Name == "TextMeshPro") return comp;
            }

            return null;
        }

        internal static void SetColor(Component? tmp, Color color)
        {
            if (tmp == null) return;
            var pi = tmp.GetType().GetProperty("color", PropFlags, null, typeof(Color), Type.EmptyTypes, null);
            pi?.SetValue(tmp, color, null);
        }
    }
}
