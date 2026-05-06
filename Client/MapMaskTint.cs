using System.Collections.Generic;
using System.Text;
using HornetCloakColor.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Tints the Hornet map icon (the compass mask) with the chosen cloak color.
    ///
    /// Design notes:
    /// <list type="bullet">
    ///   <item>The compass icon's MeshRenderer uses the <c>tk2d/BlendVertexColor</c> shader,
    ///         which has <b>no <c>_Color</c> material property</b> — it draws straight from
    ///         vertex colors. So <see cref="MaterialPropertyBlock.SetColor(int, Color)"/>
    ///         alone is not enough. The reliable channel is <see cref="tk2dSprite.color"/>,
    ///         which propagates to the mesh verts (and through the shader).</item>
    ///   <item>For renderers that <i>do</i> have a <c>_Color</c> property (e.g. the cloak
    ///         hue-shift shader on SSMP remote-player icons whose hierarchy is owned by
    ///         <see cref="CloakRecolor"/>), we still set <c>_Color</c> via a property block
    ///         so we don't clone the material.</item>
    ///   <item><b>Preserve the current alpha.</b> The map system fades icons in/out via
    ///         <see cref="NestedFadeGroupBase"/> (which writes <c>tk2dSprite.color.a</c>),
    ///         and overwriting that alpha pinned the wide-map icon visible on top of the
    ///         zoomed view. We read the live alpha each frame and only rewrite RGB.</item>
    ///   <item><see cref="DefaultExecutionOrderAttribute"/> = 20000 so our LateUpdate runs
    ///         <i>after</i> tk2d's animator and the NestedFadeGroup, otherwise we'd be
    ///         clobbered before the frame is rendered.</item>
    ///   <item><see cref="CloakSceneScanner"/> skips any renderer named "Compass Icon" so
    ///         the underlying shader is left untouched (its avoid-color list contains the
    ///         mask palette, so it would correctly produce no tint) and <see cref="tk2dSprite.color"/>
    ///         isn't reset to white every scan (which used to fight NestedFadeGroup).</item>
    /// </list>
    ///
    /// Supports <see cref="Renderer"/>, <see cref="tk2dSprite"/>, and UI <see cref="Image"/>.
    /// </summary>
    [DefaultExecutionOrder(20000)]
    [DisallowMultipleComponent]
    internal sealed class MapMaskTint : MonoBehaviour
    {
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

        private static readonly HashSet<MapMaskTint> LocalInstances = new();

        private MaterialPropertyBlock? _block;
        private readonly List<Renderer> _renderers = new();
        private readonly List<tk2dSprite> _sprites = new();
        private readonly List<Image> _images = new();

        private float _rescanTimer;
        private bool _diagDumped;

        private ushort? _networkPlayerId;
        private CloakColor _color = CloakColor.Default;

        /// <summary>Push the local player's current color to every active local map icon.</summary>
        internal static void BroadcastLocalColor(CloakColor color)
        {
            foreach (var inst in LocalInstances)
            {
                if (inst == null) continue;
                inst.SetColor(color);
            }
        }

        public void InitRemote(ushort networkPlayerId, CloakColor color)
        {
            if (_networkPlayerId.HasValue && _networkPlayerId.Value != networkPlayerId)
            {
                PlayerMapMaskTintRegistry.Unregister(_networkPlayerId.Value);
            }
            LocalInstances.Remove(this);
            _networkPlayerId = networkPlayerId;
            _color = color;
            PlayerMapMaskTintRegistry.Register(networkPlayerId, this);
            ApplyNow();
        }

        public void InitLocal(CloakColor color)
        {
            if (_networkPlayerId.HasValue)
            {
                PlayerMapMaskTintRegistry.Unregister(_networkPlayerId.Value);
                _networkPlayerId = null;
            }
            LocalInstances.Add(this);
            _color = color;
            ApplyNow();
        }

        public void SetColor(CloakColor color)
        {
            if (_networkPlayerId.HasValue
                && !_color.Equals(color)
                && CloakPaletteConfig.DebugLogging)
            {
                Log.Info($"MapMaskTint: remote player {_networkPlayerId.Value} color updated {_color} -> {color}");
            }
            _color = color;
            ApplyNow();
        }

        private void OnEnable()
        {
            ApplyNow();
        }

        private void LateUpdate()
        {
            // Periodically rescan children: the compass icon hierarchy can be rebuilt by
            // the FSM (corpse marker, fade groups, etc.). Between rescans we just push the
            // tint to the cached targets each frame.
            _rescanTimer -= Time.unscaledDeltaTime;
            if (_rescanTimer <= 0f)
            {
                _rescanTimer = 0.25f;
                ApplyNow();
            }
            else
            {
                ApplyToCachedTargets();
            }
        }

        private void ApplyNow()
        {
            _renderers.Clear();
            _sprites.Clear();
            _images.Clear();
            GetComponentsInChildren(true, _renderers);
            GetComponentsInChildren(true, _sprites);
            GetComponentsInChildren(true, _images);
            DumpHierarchyOnce();
            ApplyToCachedTargets();
        }

        private void ApplyToCachedTargets()
        {
            var tint = _color.ToUnityColor();

            // 1) Property block path. Only does anything for shaders that actually expose
            //    a `_Color` property (e.g. the cloak hue-shift shader on remote-player
            //    icons). For the compass icon's tk2d/BlendVertexColor shader this is a
            //    no-op — that path is handled below via tk2dSprite.color.
            if (_renderers.Count > 0)
            {
                _block ??= new MaterialPropertyBlock();
                foreach (var r in _renderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(_block);
                    _block.SetColor(ColorPropertyId, new Color(tint.r, tint.g, tint.b, 1f));
                    r.SetPropertyBlock(_block);
                }
            }

            // 2) tk2dSprite vertex-color path. The compass icon falls in here. We read the
            //    current alpha (driven by NestedFadeGroup) and only rewrite RGB so fades
            //    keep working. Skip the write if RGB already matches to avoid triggering
            //    an unnecessary mesh-color rebuild every frame.
            foreach (var sprite in _sprites)
            {
                if (sprite == null) continue;
                var current = sprite.color;
                if (current.r == tint.r && current.g == tint.g && current.b == tint.b)
                    continue;
                sprite.color = new Color(tint.r, tint.g, tint.b, current.a);
            }

            // 3) UI Image path (canvas-rendered icons). Same alpha-preserving rule.
            foreach (var img in _images)
            {
                if (img == null) continue;
                var current = img.color;
                img.color = new Color(tint.r, tint.g, tint.b, current.a);
            }
        }

        private void DumpHierarchyOnce()
        {
            if (_diagDumped || !CloakPaletteConfig.DebugLogging) return;
            _diagDumped = true;

            var sb = new StringBuilder();
            sb.Append("[HornetCloakColor] MapMaskTint attached to '").Append(name).Append("' — ");
            sb.Append("renderers=").Append(_renderers.Count)
              .Append(", sprites=").Append(_sprites.Count)
              .Append(", images=").Append(_images.Count);
            sb.AppendLine();

            for (var i = 0; i < _renderers.Count; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                var shaderName = r.sharedMaterial != null && r.sharedMaterial.shader != null
                    ? r.sharedMaterial.shader.name
                    : "<null>";
                sb.Append("  Renderer[").Append(i).Append("] type=").Append(r.GetType().Name)
                  .Append(" path=").Append(GetPath(r.transform))
                  .Append(" shader=").Append(shaderName)
                  .AppendLine();
            }
            for (var j = 0; j < _sprites.Count; j++)
            {
                var sprite = _sprites[j];
                if (sprite == null) continue;
                sb.Append("  Sprite[").Append(j).Append("] path=").Append(GetPath(sprite.transform))
                  .AppendLine();
            }
            for (var k = 0; k < _images.Count; k++)
            {
                var img = _images[k];
                if (img == null) continue;
                sb.Append("  Image[").Append(k).Append("] path=").Append(GetPath(img.transform))
                  .AppendLine();
            }

            HornetCloakColorPlugin.LogSource?.LogInfo(sb.ToString());
        }

        private static string GetPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var parent = t.parent;
            while (parent != null)
            {
                sb.Insert(0, parent.name + "/");
                parent = parent.parent;
            }
            return sb.ToString();
        }

        private void OnDisable()
        {
            ClearTargets();
        }

        private void OnDestroy()
        {
            if (_networkPlayerId.HasValue)
            {
                PlayerMapMaskTintRegistry.Unregister(_networkPlayerId.Value);
            }
            LocalInstances.Remove(this);
            ClearTargets();
        }

        private void ClearTargets()
        {
            foreach (var r in _renderers)
            {
                if (r != null) r.SetPropertyBlock(null);
            }
        }
    }

    /// <summary>Tracks remote-player map icons by network id so color updates can find them.</summary>
    internal static class PlayerMapMaskTintRegistry
    {
        private static readonly Dictionary<ushort, MapMaskTint> ByPlayer = new();

        internal static void Register(ushort id, MapMaskTint tint) => ByPlayer[id] = tint;

        internal static void Unregister(ushort id) => ByPlayer.Remove(id);

        internal static void SetColor(ushort playerId, CloakColor color)
        {
            if (ByPlayer.TryGetValue(playerId, out var tint) && tint != null)
            {
                tint.SetColor(color);
            }
        }
    }

    /// <summary>Convenience wrapper for attaching/refreshing the local-player tint component.</summary>
    internal static class LocalMapMaskTint
    {
        internal static void Refresh(GameMap? gameMap, CloakColor color)
        {
            if (gameMap == null || gameMap.compassIcon == null) return;
            RefreshObject(gameMap.compassIcon, color);
        }

        internal static void RefreshObject(GameObject? icon, CloakColor color)
        {
            if (icon == null) return;
            var tint = icon.GetComponent<MapMaskTint>() ?? icon.AddComponent<MapMaskTint>();
            tint.InitLocal(color);
        }
    }
}
