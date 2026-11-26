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

        public Material Material(int key)
        {
            if (materials.TryGetValue(key, out var m)) return m;
            m = new Material(baseMaterial) { name = $"Level texture {key}", hideFlags = HideFlags.DontSave };
            m.mainTexture = key == White ? Texture2D.whiteTexture : Texture(key);
            materials[key] = m;
            return m;
        }

        public Texture2D Texture(int key)
        {
            if (textures.TryGetValue(key, out var t)) return t;
            if (key == LevelTextures.Placeholder || res == null || palette == null)
                t = Make(2, 2, new byte[] { 255, 0, 255, 255, 40, 0, 40, 255, 40, 0, 40, 255, 255, 0, 255, 255 }, "placeholder");
            else
            {
                var img = WallTexture.Decode(res, res.Entries[key]).Image;
                t = Make(img.Width, img.Height, img.ToRgba(palette), $"RES4 entry {key}");
            }
            textures[key] = t;
            return t;
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
