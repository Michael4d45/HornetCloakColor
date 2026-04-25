using HornetCloakColor.Shared;
using UnityEngine;

namespace HornetCloakColor.Client
{
    /// <summary>
    /// GPU readback for non-readable atlases — used by <see cref="TextureDumper"/> PNG dumps.
    /// </summary>
    internal static class TextureReadback
    {
        internal static Color[]? CopyToPixelArray(Texture src)
        {
            var t2d = CopyToReadableTexture2D(src);
            if (t2d == null) return null;
            var px = t2d.GetPixels();
            UnityEngine.Object.Destroy(t2d);
            return px;
        }

        /// <summary>
        /// Same blit/read path as texture dumps; caller must <c>Destroy</c> the result when done.
        /// </summary>
        internal static Texture2D? CopyToReadableTexture2D(Texture src)
        {
            var w = src.width;
            var h = src.height;
            if (w <= 0 || h <= 0) return null;

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;

                var dst = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false, linear: false);
                dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                dst.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                return dst;
            }
            catch (System.Exception ex)
            {
                Log.Warn($"[TextureReadback] Failed for '{src.name}': {ex.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
