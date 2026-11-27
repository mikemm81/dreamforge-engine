// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using DreamForge.Formats;
using UnityEngine;

namespace DreamForge.World
{
    /// <summary>
    /// Decodes RES4 textures on demand into in-memory Texture2Ds (point filtered, sRGB, no mipmaps) and
    /// one material per texture. Nothing is saved: everything is HideFlags.DontSave and destroyed with the cache.
    /// Keys: RES4 entry index, <see cref="White"/> (untextured), or <see cref="LevelTextures.Placeholder"/>.
    /// </summary>
    public sealed class LevelTextureCache : IDisposable
    {
        public const int White = -1;

        readonly ResFile res;
        readonly Palette palette;
        readonly Material baseMaterial;
        readonly Dictionary<int, Texture2D> textures = new Dictionary<int, Texture2D>();
        readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();

        /// <param name="res4">RES4; may be null when only White is needed (debug colours).</param>
        public LevelTextureCache(ResFile res4, Palette palette, Material baseMaterial)
        {
            res = res4;
            this.palette = palette;
            this.baseMaterial = baseMaterial;
        }

        /// <summary>Problems met while decoding (e.g. a shape whose canvas is not 64 x 192).</summary>
        public System.Collections.Generic.List<string> Warnings { get; } = new System.Collections.Generic.List<string>();

        /// <summary>True when the key is a column shape drawn as a masked (cut-out) wall.</summary>
        public bool IsMasked(int key) =>
            key >= 0 && res != null && key < res.Entries.Count &&
            res.Entries[key].Kind == ResEntryKind.Eh && res.Entries[key].Type == 6;

        public Material Material(int key)
        {
            if (materials.TryGetValue(key, out var m)) return m;
            m = new Material(baseMaterial) { name = $"Level texture {key}", hideFlags = HideFlags.DontSave };
            m.mainTexture = key == White ? Texture2D.whiteTexture : Texture(key);
            if (IsMasked(key))
            {
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_AlphaClip", 1f);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            materials[key] = m;
            return m;
        }

        public Texture2D Texture(int key)
        {
            if (textures.TryGetValue(key, out var t)) return t;
            if (key == LevelTextures.Placeholder || res == null || palette == null)
                t = Make(2, 2, new byte[] { 255, 0, 255, 255, 40, 0, 40, 255, 40, 0, 40, 255, 255, 0, 255, 255 }, "placeholder");
            else if (IsMasked(key))
                t = Make(WallTexture.WallWidth, WallTexture.WallHeight, MaskedWallRgba(key), $"RES4 entry {key} (masked)");
            else
            {
                var img = WallTexture.Decode(res, res.Entries[key]).Image;
                t = Make(img.Width, img.Height, img.ToRgba(palette), $"RES4 entry {key}");
            }
            textures[key] = t;
            return t;
        }

        /// <summary>
        /// A column shape as a 64 x 192 wall image with alpha: the shape's canvas from its top-left corner,
        /// which shares the walls' 192-row space. Pixels outside the canvas are transparent.
        /// </summary>
        byte[] MaskedWallRgba(int key)
        {
            var e = res.Entries[key];
            var r = ResCompression.Decode(res.ReadPayload(e), e.Compression, (int)e.DecodedSize, out byte[] data);
            if (!r.Ok) throw new System.IO.InvalidDataException($"entry {key}: {r.Message}");
            const int w = WallTexture.WallWidth, h = WallTexture.WallHeight;
            var o = new byte[w * h * 4];
            if (ColumnShape.IsStub(data)) { Warnings.Add($"masked wall {key} is an empty stub"); return o; }
            var s = ColumnShape.Decode(data);
            if (s.Width != w || s.Height != h)
                Warnings.Add($"masked wall {key}: canvas {s.Width}x{s.Height}, drawn as {w}x{h} from the top-left");
            byte[] src = s.ToRgba(palette);
            for (int y = 0; y < Math.Min(h, s.Height); y++)
                Buffer.BlockCopy(src, y * s.Width * 4, o, y * w * 4, Math.Min(w, s.Width) * 4);
            return o;
        }

        /// <summary>Our images are top row first; Unity textures start at the bottom row.</summary>
        static Texture2D Make(int w, int h, byte[] rgbaTopDown, string name)
        {
            var bottomUp = new byte[rgbaTopDown.Length];
            int row = w * 4;
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(rgbaTopDown, y * row, bottomUp, (h - 1 - y) * row, row);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.DontSave,
            };
            t.SetPixelData(bottomUp, 0);
            t.Apply(false, makeNoLongerReadable: false);
            return t;
        }

        public void Dispose()
        {
            foreach (var m in materials.Values) UnityEngine.Object.Destroy(m);
            foreach (var t in textures.Values) UnityEngine.Object.Destroy(t);
            materials.Clear();
            textures.Clear();
        }
    }
}
