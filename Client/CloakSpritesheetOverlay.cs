using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// Optional on-screen overlay (gated by <see cref="CloakPaletteConfig.SpritesheetOverlayText"/>)
    /// listing which mask PNG paths correspond to the hero's active spritesheets this frame.
    ///
    /// <para>
    /// Two sections: renderers under the hero hierarchy, and visible <b>orphan</b> sprites near the
    /// hero (prefixed <c>*</c>). Death animations (<c>Hero Death(Clone)</c>), enemy grab sequences
    /// (e.g. Dustroach) and other scripted poses draw Hornet from scene-level objects the hero does
    /// not own — walking only the hero hierarchy misses them entirely.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(33000)]
    [DisallowMultipleComponent]
    internal sealed class CloakSpritesheetOverlay : MonoBehaviour
    {
        public static CloakSpritesheetOverlay? Instance { get; private set; }

        /// <summary>Orphans further than this from the hero (or last hero position) are not listed.</summary>
        private const float OrphanRadius = 14f;

        /// <summary>Scene-wide orphan scans are throttled; hero-hierarchy lines refresh every frame.</summary>
        private const int OrphanRescanIntervalFrames = 10;

        private GUIStyle? _labelStyle;
        private GUIStyle? _shadowStyle;
        private string _displayText = string.Empty;
        private readonly List<string> _orphanLines = new();
        private int _orphanRescanCountdown;
        private Vector3 _lastHeroPosition;
        private bool _hasHeroPosition;

        public static void EnsureCreated()
        {
            if (!CloakPaletteConfig.SpritesheetOverlayText)
                return;

            if (Instance != null)
                return;

            var go = new GameObject("HornetCloakColorSpritesheetOverlay");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CloakSpritesheetOverlay>();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        private void LateUpdate()
        {
            if (!CloakPaletteConfig.SpritesheetOverlayText)
            {
                _displayText = string.Empty;
                return;
            }

            _displayText = BuildOverlayText();
        }

        /// <summary>
        /// Hero death despawns/disables the hero object while the death prefab plays; keep the last
        /// known position so nearby orphans stay listed through the animation.
        /// </summary>
        private Vector3? ResolveAnchorPosition(HeroController? hero)
        {
            if (hero != null && hero.gameObject.activeInHierarchy)
            {
                _lastHeroPosition = hero.transform.position;
                _hasHeroPosition = true;
            }

            return _hasHeroPosition ? _lastHeroPosition : null;
        }

        private void OnGUI()
        {
            if (!CloakPaletteConfig.SpritesheetOverlayText || string.IsNullOrEmpty(_displayText))
                return;

            EnsureStyles();

            const float pad = 10f;
            const float maxWidth = 900f;
            var content = new GUIContent(_displayText);
            var size = _labelStyle!.CalcSize(content);
            var width = Mathf.Min(maxWidth, size.x + 16f);
            var height = _labelStyle.CalcHeight(content, width) + 12f;
            var rect = new Rect(pad, pad, width, height);

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;

            var shadowRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height);
            GUI.Label(shadowRect, _displayText, _shadowStyle);
            GUI.Label(rect, _displayText, _labelStyle);
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null)
                return;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                richText = false,
                padding = new RectOffset(8, 8, 6, 6),
            };
            _labelStyle.normal.textColor = Color.white;

            _shadowStyle = new GUIStyle(_labelStyle);
            _shadowStyle.normal.textColor = Color.black;
        }

        private string BuildOverlayText()
        {
            var hero = HeroController.instance ?? HeroController.SilentInstance;
            var anchor = ResolveAnchorPosition(hero);

            var lines = new List<string>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (hero != null)
            {
                foreach (var meshRenderer in hero.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!IsRendererVisibleNow(meshRenderer))
                        continue;

                    if (TryFormatTk2dLine(meshRenderer, seenPaths, out var line))
                        lines.Add(line);
                }

                foreach (var spriteRenderer in hero.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (!IsRendererVisibleNow(spriteRenderer))
                        continue;

                    if (TryFormatSpriteRendererLine(spriteRenderer, seenPaths, out var line))
                        lines.Add(line);
                }
            }

            lines.Sort(StringComparer.OrdinalIgnoreCase);

            if (--_orphanRescanCountdown <= 0)
            {
                _orphanRescanCountdown = OrphanRescanIntervalFrames;
                RebuildOrphanLines(hero, anchor, seenPaths);
            }

            lines.AddRange(_orphanLines);

            if (lines.Count == 0)
                return hero == null ? "(no hero)" : "(no active spritesheet on hero)";

            var sb = new StringBuilder(lines.Count * 64);
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    sb.Append('\n');
                sb.Append(lines[i]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Visible sprites near the hero that are NOT under its hierarchy: death prefabs, enemy
        /// grab/eat sequences, scripted poses. Scene-wide walk, so throttled to
        /// <see cref="OrphanRescanIntervalFrames"/>; lines are prefixed with <c>*</c>.
        /// </summary>
        private void RebuildOrphanLines(HeroController? hero, Vector3? anchor, HashSet<string> seenPaths)
        {
            _orphanLines.Clear();
            if (anchor == null)
                return;

            var heroRoot = hero != null ? hero.transform : null;
            var radiusSqr = OrphanRadius * OrphanRadius;

            foreach (var sprite in FindObjectsByType<tk2dBaseSprite>(FindObjectsSortMode.None))
            {
                if (sprite == null)
                    continue;

                var meshRenderer = sprite.GetComponent<MeshRenderer>();
                if (!IsRendererVisibleNow(meshRenderer) || !meshRenderer!.isVisible)
                    continue;

                if (IsUnder(meshRenderer.transform, heroRoot))
                    continue;

                if ((meshRenderer.bounds.center - anchor.Value).sqrMagnitude > radiusSqr)
                    continue;

                if (TryFormatTk2dLine(meshRenderer, seenPaths, out var line))
                    _orphanLines.Add("* " + line);
            }

            foreach (var spriteRenderer in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                if (!IsRendererVisibleNow(spriteRenderer) || !spriteRenderer.isVisible)
                    continue;

                if (IsUnder(spriteRenderer.transform, heroRoot))
                    continue;

                if ((spriteRenderer.bounds.center - anchor.Value).sqrMagnitude > radiusSqr)
                    continue;

                if (TryFormatSpriteRendererLine(spriteRenderer, seenPaths, out var line))
                    _orphanLines.Add("* " + line);
            }

            _orphanLines.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryFormatTk2dLine(MeshRenderer meshRenderer, HashSet<string> seenPaths, out string line)
        {
            line = string.Empty;

            var shared = meshRenderer.sharedMaterial;
            if (shared == null || !shared.HasProperty(CloakShaderManager.MainTexId))
                return false;

            var mainTex = shared.mainTexture;
            if (mainTex == null)
                return false;

            var sprite = CloakMaterialApplier.ResolveTk2dSprite(meshRenderer);
            var collectionName = sprite?.Collection != null ? sprite.Collection.name : null;
            var path = CloakSpritesheetPaths.FormatTk2dMaskPath(collectionName, mainTex);
            if (!seenPaths.Add(path))
                return false;

            var collectionNote = string.IsNullOrEmpty(collectionName) ? "(no collection)" : collectionName;
            line = $"{meshRenderer.gameObject.name}: {path}  [{collectionNote}]";
            return true;
        }

        private static bool TryFormatSpriteRendererLine(SpriteRenderer spriteRenderer, HashSet<string> seenPaths, out string line)
        {
            line = string.Empty;

            var sprite = spriteRenderer.sprite;
            if (sprite == null)
                return false;

            var lookupStem = !string.IsNullOrEmpty(sprite.name)
                ? sprite.name
                : sprite.texture != null ? sprite.texture.name : null;
            var path = CloakSpritesheetPaths.FormatTexture2DMaskPath(lookupStem);
            if (!seenPaths.Add(path))
                return false;

            line = $"{spriteRenderer.gameObject.name}: {path}";
            return true;
        }

        private static bool IsRendererVisibleNow(Renderer? renderer) =>
            renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy;

        private static bool IsUnder(Transform t, Transform? root)
        {
            if (root == null)
                return false;

            for (var p = t; p != null; p = p.parent)
            {
                if (p == root)
                    return true;
            }

            return false;
        }
    }
}
