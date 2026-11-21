using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Stratum.Game
{
    // A single block as a mesh (for the hand and for icons), and the icon for every item.
    public static class BlockMesh
    {
        static readonly Vector3Int[] N = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };
        static readonly int[,,] C =
        {
            { { 1, 0, 0 }, { 1, 1, 0 }, { 1, 1, 1 }, { 1, 0, 1 } },
            { { 0, 0, 1 }, { 0, 1, 1 }, { 0, 1, 0 }, { 0, 0, 0 } },
            { { 0, 1, 0 }, { 0, 1, 1 }, { 1, 1, 1 }, { 1, 1, 0 } },
            { { 0, 0, 0 }, { 1, 0, 0 }, { 1, 0, 1 }, { 0, 0, 1 } },
            { { 1, 0, 1 }, { 1, 1, 1 }, { 0, 1, 1 }, { 0, 0, 1 } },
            { { 0, 0, 0 }, { 0, 1, 0 }, { 1, 1, 0 }, { 1, 0, 0 } },
        };
        public static readonly Color32 DefaultGrass = new Color32(102, 168, 61, 255);

        static readonly Dictionary<ushort, Mesh> _cache = new Dictionary<ushort, Mesh>();

        // A block as a mesh centred on the origin: a whole cube, or its boxes (a bed shows both halves, shrunk to fit).
        public static Mesh Cube(ushort id)
        {
            if (_cache.TryGetValue(id, out var m)) return m;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var col = new List<Color32>();
            var uv0 = new List<Vector3>(); var uv1 = new List<Vector4>(); var tri = new List<int>();
            var parts = new List<(ushort id, Vector3 offset)> { (id, Vector3.zero) };
            if (Blocks.Get(id).BaseId == Blocks.Bed) parts = new List<(ushort, Vector3)> { ((ushort)(Blocks.Bed + 0), new Vector3(0, 0, -0.5f)), ((ushort)(Blocks.Bed + 1), new Vector3(0, 0, 0.5f)) };
            foreach (var (pid, off) in parts)
            {
                var d = Blocks.Get(pid);
                var boxes = d.Shape == Shape.Boxes && d.Boxes != null ? (d.Connect != Connect.None ? Blocks.ConnectBoxes(d.Connect, 2 | 8) : d.Boxes) : new[] { new Core.Box(0, 0, 0, 1, 1, 1) };
                foreach (var bx in boxes)
                    for (int f = 0; f < 6; f++)
                    {
                        var t = d.Face(f);
                        int flags = 0;
                        if (t == Tex.GrassTop || t == Tex.OakLeaves) flags |= Flag.Tint;
                        if (t == Tex.GrassSide) flags |= Flag.TintMask;
                        if (d.Cutout) flags |= Flag.Clip;
                        if (t == Tex.Sunstone || t == Tex.Lava) flags |= Flag.Glow;
                        if (t == Tex.SunstoneOre || t == Tex.FurnaceFront || t == Tex.Hearth || t == Tex.Lantern) flags |= Flag.GlowMask;
                        int b = v.Count;
                        for (int k = 0; k < 4; k++)
                        {
                            float px = C[f, k, 0] == 1 ? bx.X1 : bx.X0, py = C[f, k, 1] == 1 ? bx.Y1 : bx.Y0, pz = C[f, k, 2] == 1 ? bx.Z1 : bx.Z0;
                            v.Add(new Vector3(px, py, pz) - Vector3.one * 0.5f + off);
                            n.Add(N[f]);
                            col.Add(new Color32(255, 0, 255, 255));
                            float u, vv;
                            switch (f)
                            {
                                case 0: u = pz; vv = py; break;
                                case 1: u = 1 - pz; vv = py; break;
                                case 4: u = 1 - px; vv = py; break;
                                case 5: u = px; vv = py; break;
                                default:
                                {
                                    float lx = px, lz = pz;
                                    for (int r = 0; r < (d.Facing & 3); r++) { float tt = lx; lx = 1 - lz; lz = tt; }
                                    u = lx; vv = lz; break;
                                }
                            }
                            uv0.Add(new Vector3(u, vv, (float)t));
                            var tc = DefaultGrass; tc.a = (byte)flags;
                            if ((flags & (Flag.Tint | Flag.TintMask)) == 0) tc = new Color32(255, 255, 255, (byte)flags);
                            uv1.Add(new Vector4(tc.r / 255f, tc.g / 255f, tc.b / 255f, tc.a / 255f));
                        }
                        tri.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                    }
            }
            if (parts.Count > 1) for (int i = 0; i < v.Count; i++) v[i] *= 0.62f;
            m = new Mesh { name = "block " + Blocks.Get(id).Name };
            m.SetVertices(v); m.SetNormals(n); m.SetColors(col); m.SetUVs(0, uv0); m.SetUVs(1, uv1); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            _cache[id] = m;
            return m;
        }

        public static bool IsFlat(ushort id)
        {
            var d = Blocks.Get(id);
            return d.Shape == Shape.Cross || d.Shape == Shape.Torch || d.FlatIcon;
        }
    }

    public static class Icons
    {
        static readonly Dictionary<string, Texture2D> _icons = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        const int Size = 96;

        static Texture2D Glint(Texture2D plain)
        {
            Color[] px;
            try { px = plain.GetPixels(); } catch { return plain; }
            int w = plain.width, h = plain.height;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    ref var c = ref px[x + y * w];
                    if (c.a < 0.05f) continue;
                    float band = Mathf.Pow(0.5f + 0.5f * Mathf.Sin((x + y) * 0.16f), 6);
                    var glow = new Color(0.72f, 0.45f, 1f);
                    c = new Color(Mathf.Lerp(c.r, glow.r, 0.22f + band * 0.4f), Mathf.Lerp(c.g, glow.g, 0.22f + band * 0.4f), Mathf.Lerp(c.b, glow.b, 0.22f + band * 0.4f), c.a);
                }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = plain.filterMode, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels(px); t.Apply();
            return t;
        }

        public static Sprite Get(string item)
        {
            if (item == null) return null;
            if (_sprites.TryGetValue(item, out var s)) return s;
            if (!_icons.TryGetValue(item, out var t))
            {
                // Enchanted things: the plain picture with a violet shimmer.
                int at = item.IndexOf('@');
                if (at < 0 || !_icons.TryGetValue(item.Substring(0, at), out var plain)) return null;
                t = Glint(plain);
                _icons[item] = t;
            }
            s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100);
            _sprites[item] = s;
            return s;
        }

        // Renders every block item as a small three-quarter view cube and paints the rest.
        public static void Build(Material blockMat)
        {
            if (_icons.Count > 0) return;
            const int Layer = 31;
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var camGo = new GameObject("IconCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 0.92f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << Layer; cam.targetTexture = rt; cam.allowHDR = false; cam.allowMSAA = true;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 20;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false; data.renderShadows = false;
            camGo.transform.position = new Vector3(0, -5000, 0) + Quaternion.Euler(30, 45, 0) * new Vector3(0, 0, -6);
            camGo.transform.rotation = Quaternion.Euler(30, 45, 0);

            var obj = new GameObject("IconBlock") { layer = Layer };
            obj.transform.position = new Vector3(0, -5000, 0);
            var mf = obj.AddComponent<MeshFilter>();
            var mr = obj.AddComponent<MeshRenderer>();
            var mat = new Material(blockMat);
            mat.SetVector("_LightOverride", new Vector4(1, 0, 0, 1));
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;

            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            Shader.SetGlobalColor("_SkyAmbient", new Color(1.05f, 1.05f, 1.05f));
            Shader.SetGlobalColor("_TorchColor", Color.black);
            Shader.SetGlobalFloat("_Wind", 0);

            foreach (var item in Items.Order)
            {
                if (item.Block == 0) continue;
                if (Blocks.Get(item.Block).FlatIcon) { _icons[item.Id] = item.Id == "ladder" ? Flat(Tex.Ladder) : item.Id == "rail" ? Flat(Tex.Rail) : item.Id == "booster_rail" ? Flat(Tex.BoosterRail) : item.Id == "repeater" ? Flat(Tex.RepeaterOn) : ItemArt.Paint(item); continue; }
                if (BlockMesh.IsFlat(item.Block)) { _icons[item.Id] = Flat(Blocks.Get(item.Block).Top); continue; }
                mf.sharedMesh = BlockMesh.Cube(item.Block);
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, name = item.Id };
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = prev;
                FixAlpha(tex);
                tex.Apply();
                _icons[item.Id] = tex;
            }
            RenderSettings.fog = fog;
            Object.Destroy(obj); Object.Destroy(camGo); Object.Destroy(mat);
            rt.Release(); Object.Destroy(rt);

            foreach (var item in Items.Order) if (item.Block == 0) _icons[item.Id] = ItemArt.Paint(item);
        }

        // If the pipeline dropped the alpha channel, key out the black clear colour instead.
        static void FixAlpha(Texture2D t)
        {
            var px = t.GetPixels32();
            bool anyClear = false;
            foreach (var p in px) if (p.a < 250) { anyClear = true; break; }
            if (anyClear) return;
            for (int i = 0; i < px.Length; i++)
                if (px[i].r < 3 && px[i].g < 3 && px[i].b < 3) px[i].a = 0;
            t.SetPixels32(px);
        }

        static Texture2D Flat(Tex layer)
        {
            var src = BlockTextures.Pixels(layer);
            var t = new Texture2D(Paint.S, Paint.S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var c = src[i];
                if (layer == Tex.TallGrass || layer == Tex.Fern) { c.r *= BlockMesh.DefaultGrass.r / 255f; c.g *= BlockMesh.DefaultGrass.g / 255f; c.b *= BlockMesh.DefaultGrass.b / 255f; }
                px[i] = c;
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        public static Texture2D TextureOf(string item) => item != null && _icons.TryGetValue(item, out var t) ? t : null;
    }
}
