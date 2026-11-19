using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Stratum.Game
{
    // The other players: a blocky figure in a hooded tunic coloured by their name, lit by the world's own light
    // where they stand, eased toward each position the server sends, with a name tag overhead.
    public sealed class Avatars : MonoBehaviour
    {
        sealed class Avatar
        {
            public int Id; public string Name;
            public Transform Root, Head, ArmL, ArmR, LegL, LegR, Body;
            public Renderer[] Parts; public Color[] Colors;
            public Vector3 Pos, Target; public float Yaw, TargetYaw, Pitch, TargetPitch;
            public byte Flags;
            public float Phase, Speed, Swing;
            public Vector2 Light = new Vector2(1, 0);
            public RectTransform Tag;
        }

        public VoxelWorld World;
        public Sky Sky;
        public Camera Cam;
        readonly Dictionary<int, Avatar> _all = new Dictionary<int, Avatar>();
        Material _mat;
        Mesh _cube;
        RectTransform _tags;
        MaterialPropertyBlock _mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int LightId = Shader.PropertyToID("_Light");

        public static Avatars Create(VoxelWorld world, Sky sky, Camera cam)
        {
            var go = new GameObject("Avatars");
            var a = go.AddComponent<Avatars>();
            a.World = world; a.Sky = sky; a.Cam = cam;
            a._mat = new Material(Shader.Find("Stratum/Avatar")) { name = "Avatar" };
            a._cube = Cube();
            a._mpb = new MaterialPropertyBlock();
            var canvas = UIKit.Root("NameTags", 5);
            canvas.transform.SetParent(go.transform, false);
            a._tags = (RectTransform)canvas.transform;
            return a;
        }

        public int Count => _all.Count;
        public IEnumerable<int> Ids => new List<int>(_all.Keys);
        public IEnumerable<string> Names { get { foreach (var a in _all.Values) yield return a.Name; } }

        public bool TryGet(int id, out Vector3 pos) { pos = default; if (!_all.TryGetValue(id, out var a)) return false; pos = a.Pos; return true; }

        public void Add(int id, string name, Vector3 pos, float yaw)
        {
            Remove(id);
            var a = Build(id, name);
            a.Pos = a.Target = pos; a.Yaw = a.TargetYaw = yaw;
            _all[id] = a;
            Place(a, 0);
        }

        public void Remove(int id)
        {
            if (!_all.TryGetValue(id, out var a)) return;
            Destroy(a.Root.gameObject);
            if (a.Tag != null) Destroy(a.Tag.gameObject);
            _all.Remove(id);
        }

        public void Clear() { foreach (var id in new List<int>(_all.Keys)) Remove(id); }

        public void Moved(int id, Vector3 pos, float yaw, float pitch, byte flags)
        {
            if (!_all.TryGetValue(id, out var a)) return;
            if ((pos - a.Pos).sqrMagnitude > 12 * 12) a.Pos = pos;        // a jump this big is a teleport, not a walk
            a.Target = pos; a.TargetYaw = yaw; a.TargetPitch = pitch; a.Flags = flags;
            if ((flags & Net.Swinging) != 0) a.Swing = 1;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var a in _all.Values) Place(a, dt);
        }

        void Place(Avatar a, float dt)
        {
            var before = a.Pos;
            float k = 1 - Mathf.Exp(-dt * 12);
            a.Pos = Vector3.Lerp(a.Pos, a.Target, k);
            a.Yaw = Mathf.LerpAngle(a.Yaw, a.TargetYaw, k);
            a.Pitch = Mathf.Lerp(a.Pitch, a.TargetPitch, k);
            var flat = a.Pos - before; flat.y = 0;
            float speed = dt > 0 ? flat.magnitude / dt : 0;
            a.Speed = Mathf.Lerp(a.Speed, speed, 1 - Mathf.Exp(-dt * 8));

            bool sneak = (a.Flags & Net.Sneaking) != 0, fly = (a.Flags & Net.Flying) != 0;
            a.Root.position = a.Pos;
            a.Root.rotation = Quaternion.Euler(0, a.Yaw, 0);
            a.Body.localRotation = Quaternion.Euler(sneak ? 22 : 0, 0, 0);
            a.Head.localRotation = Quaternion.Euler(Mathf.Clamp(a.Pitch, -60, 60), 0, 0);

            float walk = fly ? 0 : Mathf.Clamp01(a.Speed / 4.3f);
            a.Phase += dt * (4 + a.Speed * 2.2f) * (walk > 0.05f ? 1 : 0);
            float sw = Mathf.Sin(a.Phase) * 38 * walk;
            a.LegL.localRotation = Quaternion.Euler(sw, 0, 0);
            a.LegR.localRotation = Quaternion.Euler(-sw, 0, 0);
            a.Swing = Mathf.Max(0, a.Swing - dt * 3.5f);
            float hit = Mathf.Sin(a.Swing * Mathf.PI) * 75;
            a.ArmL.localRotation = Quaternion.Euler(-sw * 0.8f, 0, 0);
            a.ArmR.localRotation = Quaternion.Euler(sw * 0.8f - hit, 0, 0);

            // The world's stored light where they stand (the brighter of feet and head, so leaves overhead don't
            // black them out), eased so walking under a roof dims them smoothly. The shader lights them from it.
            var want = a.Light;
            if (World != null)
            {
                bool any = false; float sk = 0, bl = 0;
                for (int hk = 0; hk < 2; hk++)
                {
                    var q = a.Pos + Vector3.up * (hk == 0 ? 0.5f : 1.5f);
                    if (!World.LightAt(Mathf.FloorToInt(q.x), Mathf.FloorToInt(q.y), Mathf.FloorToInt(q.z), out float s1, out float b1)) continue;
                    any = true; sk = Mathf.Max(sk, s1); bl = Mathf.Max(bl, b1);
                }
                if (any) want = new Vector2(sk, bl);
            }
            a.Light = dt <= 0 ? want : Vector2.Lerp(a.Light, want, 1 - Mathf.Exp(-dt * 6));
            for (int i = 0; i < a.Parts.Length; i++)
            {
                _mpb.SetColor(BaseColor, a.Colors[i]);
                _mpb.SetVector(LightId, new Vector4(a.Light.x, a.Light.y, 0, 0));
                a.Parts[i].SetPropertyBlock(_mpb);
            }

            // Name tag: over the head, hidden behind the camera, far away, or while they sneak.
            if (a.Tag != null && Cam != null)
            {
                var top = a.Pos + Vector3.up * 2.15f;
                var sp = Cam.WorldToScreenPoint(top);
                float dist = Vector3.Distance(Cam.transform.position, top);
                bool show = sp.z > 0.2f && dist < 64 && !sneak;
                a.Tag.gameObject.SetActive(show);
                if (show)
                {
                    var canvas = _tags.GetComponent<Canvas>();
                    float scale = canvas.scaleFactor > 0 ? canvas.scaleFactor : 1;
                    a.Tag.anchoredPosition = new Vector2(sp.x / scale, sp.y / scale);
                    float s = Mathf.Clamp(14f / Mathf.Max(dist, 1), 0.55f, 1.15f);
                    a.Tag.transform.localScale = Vector3.one * s;
                }
            }
        }

        // ------------------------------------------------------------------ building a figure

        Avatar Build(int id, string name)
        {
            var a = new Avatar { Id = id, Name = name };
            uint h = 2166136261; foreach (char ch in name) h = (h ^ ch) * 16777619;
            float hue = (h % 360) / 360f;
            var tunic = Color.HSVToRGB(hue, 0.55f, 0.62f);
            var hood = Color.HSVToRGB(hue, 0.62f, 0.42f);
            var trim = Color.HSVToRGB((hue + 0.1f) % 1, 0.35f, 0.85f);
            Color[] skins = { new Color(0.93f, 0.76f, 0.62f), new Color(0.80f, 0.60f, 0.45f), new Color(0.62f, 0.44f, 0.30f), new Color(0.42f, 0.29f, 0.20f) };
            var skin = skins[(h >> 9) % skins.Length];
            var trousers = new Color(0.20f, 0.19f, 0.23f);
            var boots = new Color(0.30f, 0.20f, 0.13f);
            var dark = new Color(0.08f, 0.07f, 0.07f);

            var parts = new List<Renderer>(); var cols = new List<Color>();
            a.Root = new GameObject("Player " + name).transform;
            a.Root.SetParent(transform, false);

            // Pivots at the hips, shoulders and neck so limbs swing from the right place. Sizes are in blocks:
            // a figure 1.8 tall, like the player's own body.
            a.Body = Pivot(a.Root, "Body", new Vector3(0, 0.75f, 0));
            Box(a.Body, new Vector3(0, 0.36f, 0), new Vector3(0.50f, 0.72f, 0.28f), tunic, parts, cols);
            Box(a.Body, new Vector3(0, 0.06f, 0), new Vector3(0.52f, 0.08f, 0.30f), boots, parts, cols);             // belt
            Box(a.Body, new Vector3(0, 0.66f, -0.145f), new Vector3(0.20f, 0.10f, 0.02f), trim, parts, cols);         // clasp
            a.Head = Pivot(a.Body, "Head", new Vector3(0, 0.74f, 0));
            Box(a.Head, new Vector3(0, 0.24f, 0), new Vector3(0.44f, 0.44f, 0.44f), skin, parts, cols);
            Box(a.Head, new Vector3(0, 0.30f, 0.03f), new Vector3(0.50f, 0.38f, 0.44f), hood, parts, cols);          // hood, open at the face
            Box(a.Head, new Vector3(0, 0.50f, 0), new Vector3(0.50f, 0.08f, 0.50f), hood, parts, cols);
            Box(a.Head, new Vector3(-0.10f, 0.25f, -0.225f), new Vector3(0.07f, 0.07f, 0.01f), dark, parts, cols);   // eyes
            Box(a.Head, new Vector3(0.10f, 0.25f, -0.225f), new Vector3(0.07f, 0.07f, 0.01f), dark, parts, cols);
            a.ArmL = Pivot(a.Body, "ArmL", new Vector3(-0.36f, 0.66f, 0));
            Box(a.ArmL, new Vector3(0, -0.30f, 0), new Vector3(0.20f, 0.66f, 0.22f), tunic * 0.9f, parts, cols);
            Box(a.ArmL, new Vector3(0, -0.60f, 0), new Vector3(0.18f, 0.14f, 0.20f), skin, parts, cols);
            a.ArmR = Pivot(a.Body, "ArmR", new Vector3(0.36f, 0.66f, 0));
            Box(a.ArmR, new Vector3(0, -0.30f, 0), new Vector3(0.20f, 0.66f, 0.22f), tunic * 0.9f, parts, cols);
            Box(a.ArmR, new Vector3(0, -0.60f, 0), new Vector3(0.18f, 0.14f, 0.20f), skin, parts, cols);
            a.LegL = Pivot(a.Root, "LegL", new Vector3(-0.13f, 0.75f, 0));
            Box(a.LegL, new Vector3(0, -0.32f, 0), new Vector3(0.23f, 0.64f, 0.25f), trousers, parts, cols);
            Box(a.LegL, new Vector3(0, -0.68f, -0.02f), new Vector3(0.25f, 0.14f, 0.30f), boots, parts, cols);
            a.LegR = Pivot(a.Root, "LegR", new Vector3(0.13f, 0.75f, 0));
            Box(a.LegR, new Vector3(0, -0.32f, 0), new Vector3(0.23f, 0.64f, 0.25f), trousers, parts, cols);
            Box(a.LegR, new Vector3(0, -0.68f, -0.02f), new Vector3(0.25f, 0.14f, 0.30f), boots, parts, cols);
            a.Parts = parts.ToArray(); a.Colors = cols.ToArray();

            // The tag: a dark rounded plate with the name on it (plate first, so the name draws over it).
            a.Tag = UIKit.Rect(_tags, "Tag " + name);
            a.Tag.anchorMin = a.Tag.anchorMax = Vector2.zero;
            a.Tag.pivot = new Vector2(0.5f, 0);
            var label = UIKit.Label(a.Tag, name, 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            float w = label.preferredWidth + 26;
            a.Tag.sizeDelta = new Vector2(w, 38);
            var bg = UIKit.Panel(a.Tag, "Plate", new Color(0, 0, 0, 0.5f), UIKit.RoundSmall);
            bg.rectTransform.Fill();
            bg.transform.SetAsFirstSibling();
            label.rectTransform.Fill();
            bg.raycastTarget = false; label.raycastTarget = false;
            return a;
        }

        static Transform Pivot(Transform parent, string name, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            return t;
        }

        void Box(Transform parent, Vector3 at, Vector3 size, Color c, List<Renderer> parts, List<Color> cols)
        {
            var go = new GameObject("Box");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = _cube;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
            parts.Add(r); cols.Add(c);
        }

        // A unit cube whose faces are shaded a little differently, so the figure reads as solid without lights.
        static Mesh Cube()
        {
            var v = new List<Vector3>(); var col = new List<Color>(); var tri = new List<int>();
            void Face(Vector3 n, Vector3 u, Vector3 w, float shade)
            {
                int b = v.Count;
                var c = n * 0.5f;
                v.Add(c - u * 0.5f - w * 0.5f); v.Add(c + u * 0.5f - w * 0.5f); v.Add(c + u * 0.5f + w * 0.5f); v.Add(c - u * 0.5f + w * 0.5f);
                for (int i = 0; i < 4; i++) col.Add(new Color(shade, shade, shade, 1));
                tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            Face(Vector3.up, Vector3.right, Vector3.forward, 1.0f);
            Face(Vector3.down, Vector3.forward, Vector3.right, 0.55f);
            Face(Vector3.right, Vector3.forward, Vector3.up, 0.82f);
            Face(Vector3.left, Vector3.up, Vector3.forward, 0.82f);
            Face(Vector3.forward, Vector3.up, Vector3.right, 0.72f);
            Face(Vector3.back, Vector3.right, Vector3.up, 0.72f);
            var m = new Mesh { name = "AvatarCube" };
            m.SetVertices(v); m.SetColors(col); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
