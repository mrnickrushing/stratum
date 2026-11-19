using Stratum.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Stratum.Game
{
    // Aiming, mining, placing and using blocks, plus what that looks like: the outline, cracks, the item in hand
    // and the dust a block leaves.
    public sealed class Interaction : MonoBehaviour
    {
        public Player P;
        public VoxelWorld World;
        public Sky Sky;
        public bool InputEnabled = true;
        public System.Action<string> OpenStation;      // "workbench" / "furnace"
        public System.Action<string> Toast;

        public bool HasTarget { get; private set; }
        public Vector3Int Target { get; private set; }
        public Vector3Int TargetNormal { get; private set; }
        public float Progress { get; private set; }

        GameObject _outline, _crack, _held;
        Material _crackMat, _heldBlockMat, _heldItemMat;
        MeshFilter _heldMf; MeshRenderer _heldMr;
        Texture2D[] _cracks;
        ParticleSystem _dust;
        string _heldItem;
        float _swing = 1, _repeat, _lightTimer;
        Vector2 _sway;
        Vector4 _light = new Vector4(1, 0, 0, 1);

        public static Interaction Create(Player p, VoxelWorld w, Material blockMat)
        {
            var i = p.gameObject.AddComponent<Interaction>();
            i.P = p; i.World = w;
            i.Build(blockMat);
            return i;
        }

        void Build(Material blockMat)
        {
            var overlay = Shader.Find("Stratum/Overlay");
            _outline = new GameObject("Outline");
            _outline.AddComponent<MeshFilter>().sharedMesh = Edges();
            var om = new Material(overlay); om.SetColor("_Color", new Color(0.02f, 0.02f, 0.03f, 0.7f)); om.SetFloat("_Mode", 0);
            var omr = _outline.AddComponent<MeshRenderer>(); omr.sharedMaterial = om; omr.shadowCastingMode = ShadowCastingMode.Off;
            _outline.SetActive(false);

            _cracks = CrackStages();
            _crack = new GameObject("Cracks");
            _crack.AddComponent<MeshFilter>().sharedMesh = UnitCube(1.003f);
            _crackMat = new Material(overlay); _crackMat.SetFloat("_Mode", 1);
            var cmr = _crack.AddComponent<MeshRenderer>(); cmr.sharedMaterial = _crackMat; cmr.shadowCastingMode = ShadowCastingMode.Off;
            _crack.SetActive(false);

            _held = new GameObject("Held");
            _held.transform.SetParent(P.Cam.transform, false);
            _heldMf = _held.AddComponent<MeshFilter>();
            _heldMr = _held.AddComponent<MeshRenderer>();
            _heldMr.shadowCastingMode = ShadowCastingMode.Off;
            _heldBlockMat = new Material(blockMat);
            _heldItemMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _heldItemMat.SetFloat("_AlphaClip", 1); _heldItemMat.SetFloat("_Cutoff", 0.5f); _heldItemMat.EnableKeyword("_ALPHATEST_ON");
            _heldItemMat.SetFloat("_Cull", 0);

            var dgo = new GameObject("Dust");
            _dust = dgo.AddComponent<ParticleSystem>();
            _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _dust.main; main.loop = false; main.playOnAwake = false; main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3.2f); main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.gravityModifier = 1.4f; main.maxParticles = 400; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = _dust.emission; em.enabled = false;
            var sh = _dust.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = Vector3.one * 0.8f;
            var col = _dust.collision; col.enabled = false;
            var pr = dgo.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Mesh; pr.mesh = UnitCube(1);
            var pm = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            pr.sharedMaterial = pm; pr.shadowCastingMode = ShadowCastingMode.Off;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Aim();
            if (InputEnabled && Cursor.lockState == CursorLockMode.Locked) Act(dt); else { Progress = 0; }
            Visuals(dt);
        }

        void Aim()
        {
            HasTarget = false;
            var o = P.Cam.transform.position; var d = P.Cam.transform.forward;
            float reach = P.Creative ? 7f : 5f;
            // Grid walk (Amanatides & Woo).
            int x = Mathf.FloorToInt(o.x), y = Mathf.FloorToInt(o.y), z = Mathf.FloorToInt(o.z);
            int sx = d.x > 0 ? 1 : -1, sy = d.y > 0 ? 1 : -1, sz = d.z > 0 ? 1 : -1;
            float tdx = Mathf.Abs(1 / d.x), tdy = Mathf.Abs(1 / d.y), tdz = Mathf.Abs(1 / d.z);
            float tx = (d.x > 0 ? x + 1 - o.x : o.x - x) * tdx, ty = (d.y > 0 ? y + 1 - o.y : o.y - y) * tdy, tz = (d.z > 0 ? z + 1 - o.z : o.z - z) * tdz;
            var n = Vector3Int.zero;
            float t = 0;
            while (t <= reach)
            {
                byte b = World.Get(x, y, z);
                if (b != 0 && b != Blocks.Water)
                {
                    bool hit = true;
                    if (hit) { HasTarget = true; Target = new Vector3Int(x, y, z); TargetNormal = n; return; }
                }
                if (tx < ty && tx < tz) { x += sx; t = tx; tx += tdx; n = new Vector3Int(-sx, 0, 0); }
                else if (ty < tz) { y += sy; t = ty; ty += tdy; n = new Vector3Int(0, -sy, 0); }
                else { z += sz; t = tz; tz += tdz; n = new Vector3Int(0, 0, -sz); }
            }
        }

        Vector3Int _mining;

        void Act(float dt)
        {
            var mouse = Mouse.current; var kb = Keyboard.current;
            if (mouse == null) return;

            // Hotbar.
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll > 0.1f) P.Inv.Selected = (P.Inv.Selected + Inventory.Hotbar - 1) % Inventory.Hotbar;
            else if (scroll < -0.1f) P.Inv.Selected = (P.Inv.Selected + 1) % Inventory.Hotbar;
            if (kb != null)
                for (int i = 0; i < 9; i++)
                    if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) P.Inv.Selected = i;

            _repeat -= dt;
            bool lmb = mouse.leftButton.isPressed, rmb = mouse.rightButton.isPressed;

            if (lmb && HasTarget)
            {
                if (_mining != Target) { _mining = Target; Progress = 0; }
                var b = Blocks.Get(World.Get(Target.x, Target.y, Target.z));
                var held = Items.Get(P.Inv.Held.Item);
                float time = Mining.Time(b, held, P.Creative);
                if (float.IsInfinity(time)) { Progress = 0; }
                else if (P.Creative)
                {
                    if (_repeat <= 0) { Break(Target); _repeat = 0.2f; }
                }
                else
                {
                    Progress += dt / time;
                    if (Mathf.Repeat(Progress * time, 0.25f) < dt) { Sfx.I?.Block("dig", b.Id, 0.45f); _swing = 0; }
                    if (Progress >= 1) { Break(Target); Progress = 0; _repeat = 0.15f; }
                }
            }
            else { Progress = 0; _mining = new Vector3Int(int.MinValue, 0, 0); }
            if (!lmb) _repeat = Mathf.Min(_repeat, 0);

            if (rmb && _repeat <= 0 && HasTarget)
            {
                _repeat = 0.22f;
                Use();
            }
            if (mouse.middleButton.wasPressedThisFrame && HasTarget) Pick();
        }

        void Break(Vector3Int p)
        {
            byte id = World.Get(p.x, p.y, p.z);
            var def = Blocks.Get(id);
            if (id == 0 || def.Hardness < 0 && !P.Creative) return;
            if (!World.Set(p.x, p.y, p.z, 0)) return;
            Sfx.I?.Block("break", id, 0.9f);
            Burst(p, def);
            _swing = 0;
            // A plant on top goes with it.
            byte above = World.Get(p.x, p.y + 1, p.z);
            if (above != 0 && (Blocks.Get(above).Shape == Shape.Cross || Blocks.Get(above).Shape == Shape.Torch)) Break(p + Vector3Int.up);
            if (P.Creative) return;
            var held = Items.Get(P.Inv.Held.Item);
            if (Mining.Drops(def, held))
            {
                var (item, n) = Mining.Drop(def);
                if (item != null)
                {
                    int left = P.Inv.Add(item, n);
                    if (left < n) Sfx.I?.Play("pickup", 0.35f, 1.4f);
                    if (left > 0) Toast?.Invoke("Your pack is full");
                }
            }
            else if (def.TierNeeded > 0) Toast?.Invoke("Needs a better pickaxe");
            if (held != null && held.Durability > 0 && def.Hardness > 0 && P.Inv.WearHeld())
            {
                Sfx.I?.Play("break_tool", 0.8f);
                Toast?.Invoke(held.Name + " broke");
            }
        }

        void Use()
        {
            var kb = Keyboard.current;
            bool sneak = kb != null && (kb.leftCtrlKey.isPressed || kb.cKey.isPressed);
            byte target = World.Get(Target.x, Target.y, Target.z);
            var tdef = Blocks.Get(target);
            if (!sneak)
            {
                if (target == Blocks.Workbench) { OpenStation?.Invoke("workbench"); return; }
                if (tdef.BaseId == Blocks.Furnace) { OpenStation?.Invoke("furnace"); return; }
            }
            var held = P.Inv.Held;
            var item = Items.Get(held.Item);
            if (item == null || item.Block == 0) return;
            byte id = item.Block;
            var def = Blocks.Get(id);
            // Into the block itself when it can be replaced (grass, water), else beside the face.
            var at = tdef.Replaceable && target != 0 ? Target : Target + TargetNormal;
            byte cur = World.Get(at.x, at.y, at.z);
            if (cur != 0 && !Blocks.Get(cur).Replaceable) return;
            if (at.y < 1 || at.y >= Chunk.Height) return;
            if (def.Solid && P.Overlaps(at.x, at.y, at.z)) return;
            byte below = World.Get(at.x, at.y - 1, at.z);
            if (def.Shape == Shape.Cross)
            {
                bool soil = below == Blocks.Grass || below == Blocks.Dirt || below == Blocks.SnowyGrass || (id == Blocks.DeadBush && below == Blocks.Sand);
                if (!soil) return;
            }
            if (def.Shape == Shape.Torch && !Blocks.Get(below).Solid) return;
            var look = P.Cam.transform.forward;
            byte place = Blocks.Facing(id, look.x, look.z);
            if (!World.Set(at.x, at.y, at.z, place)) return;
            Sfx.I?.Block("place", id, 0.8f);
            _swing = 0;
            if (!P.Creative) P.Inv.UseHeld();
        }

        void Pick()
        {
            byte id = World.Get(Target.x, Target.y, Target.z);
            var key = Blocks.Keys[Blocks.Get(id).BaseId];
            if (Items.Get(key) == null) return;
            // Already in the hotbar: select it.
            for (int i = 0; i < Inventory.Hotbar; i++) if (P.Inv.Slots[i].Item == key) { P.Inv.Selected = i; return; }
            if (P.Creative) P.Inv.Slots[P.Inv.Selected] = new Stack(key, Items.Get(key).MaxStack);
        }

        // ------------------------------------------------------------------ visuals

        void Visuals(float dt)
        {
            bool show = HasTarget && InputEnabled;
            _outline.SetActive(show);
            if (show)
            {
                byte b = World.Get(Target.x, Target.y, Target.z);
                var sh = Blocks.Get(b).Shape;
                _outline.transform.position = Target;
                _outline.transform.localScale = Vector3.one;
                if (sh == Shape.Torch) { _outline.transform.position = Target + new Vector3(0.4f, 0, 0.4f); _outline.transform.localScale = new Vector3(0.2f, 0.65f, 0.2f); }
                else if (sh == Shape.Cross) { _outline.transform.position = Target + new Vector3(0.1f, 0, 0.1f); _outline.transform.localScale = new Vector3(0.8f, 0.85f, 0.8f); }
            }
            bool cracking = show && Progress > 0.02f;
            _crack.SetActive(cracking);
            if (cracking)
            {
                _crack.transform.position = Target + Vector3.one * 0.5f;
                int stage = Mathf.Clamp((int)(Progress * _cracks.Length), 0, _cracks.Length - 1);
                _crackMat.SetTexture("_MainTex", _cracks[stage]);
            }

            // What's in hand.
            var held = P.Inv.Held;
            if (held.Item != _heldItem) { _heldItem = held.Item; SetHeld(); _swing = 0.5f; }
            _lightTimer -= dt;
            if (_lightTimer <= 0) { _lightTimer = 0.15f; _light = LightAt(P.EyePos); _heldBlockMat.SetVector("_LightOverride", new Vector4(_light.x, _light.y, 0, 1)); }
            if (_held.activeSelf)
            {
                _swing = Mathf.Min(1, _swing + dt * 4.5f);
                float s = Mathf.Sin(_swing * Mathf.PI);
                var md = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
                _sway = Vector2.Lerp(_sway, md * 0.0008f, 1 - Mathf.Exp(-dt * 10));
                var it = Items.Get(_heldItem);
                bool block = it != null && it.Block != 0 && !BlockMesh.IsFlat(it.Block);
                var basePos = block ? new Vector3(0.50f, -0.42f, 0.90f) : new Vector3(0.44f, -0.36f, 0.70f);
                _held.transform.localPosition = basePos + new Vector3(-_sway.x - s * 0.12f, -_sway.y - s * 0.18f, s * 0.1f);
                var baseRot = block ? Quaternion.Euler(8, 40, 0) : Quaternion.Euler(0, -95, 25);
                _held.transform.localRotation = Quaternion.Euler(-s * 45, 0, 0) * baseRot;
                float day = Sky != null ? Sky.DayFactor * 0.85f + 0.15f : 1;
                float lit = Mathf.Max(Mathf.Pow(0.8f, (1 - _light.x) * 15) * day, Mathf.Pow(0.8f, (1 - _light.y) * 15));
                if (!block) _heldItemMat.SetColor("_BaseColor", new Color(lit, lit, lit, 1) * 1.1f);
            }
        }

        void SetHeld()
        {
            var it = Items.Get(_heldItem);
            if (it == null) { _held.SetActive(false); return; }
            _held.SetActive(true);
            if (it.Block != 0 && !BlockMesh.IsFlat(it.Block))
            {
                _heldMf.sharedMesh = BlockMesh.Cube(it.Block);
                _heldMr.sharedMaterial = _heldBlockMat;
                _held.transform.localScale = Vector3.one * 0.24f;
            }
            else
            {
                _heldMf.sharedMesh = FlatQuad();
                var tex = Icons.TextureOf(it.Id);
                _heldItemMat.SetTexture("_BaseMap", tex);
                _heldMr.sharedMaterial = _heldItemMat;
                _held.transform.localScale = Vector3.one * 0.38f;
            }
        }

        // Rough light at a point: open sky above or not, and the brightest light source nearby.
        Vector4 LightAt(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float sky = 1;
            for (int yy = y + 1; yy < Mathf.Min(y + 64, Chunk.Height); yy++)
            {
                var d = Blocks.Get(World.Get(x, yy, z));
                if (d.Opaque) { sky = y > Chunk.SeaLevel - 4 ? 0.7f : 0.25f; break; }
            }
            int best = 0;
            for (int dy = -4; dy <= 4; dy++)
                for (int dz = -5; dz <= 5; dz++)
                    for (int dx = -5; dx <= 5; dx++)
                    {
                        int e = Blocks.Get(World.Get(x + dx, y + dy, z + dz)).Emit;
                        if (e == 0) continue;
                        int l = e - Mathf.Abs(dx) - Mathf.Abs(dy) - Mathf.Abs(dz);
                        if (l > best) best = l;
                    }
            return new Vector4(sky, best / 15f, 0, 1);
        }

        void Burst(Vector3Int p, BlockDef def)
        {
            var c = BlockTextures.Average[(int)def.Side];
            if (def.Side == Tex.GrassSide) c = BlockTextures.Average[(int)Tex.Dirt];
            float l = Mathf.Pow(0.8f, (1 - _light.x) * 15) * 0.9f + 0.1f;
            _dust.transform.position = p + Vector3.one * 0.5f;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            for (int i = 0; i < 18; i++)
            {
                float k = Random.Range(0.75f, 1.1f) * l;
                ep.startColor = new Color(c.r * k, c.g * k, c.b * k, 1);
                ep.velocity = new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(1f, 3.5f), Random.Range(-1.5f, 1.5f));
                _dust.Emit(ep, 1);
            }
        }

        // ------------------------------------------------------------------ meshes

        static Mesh _edges, _flat;
        static readonly System.Collections.Generic.Dictionary<float, Mesh> _cubes = new System.Collections.Generic.Dictionary<float, Mesh>();

        static Mesh UnitCube(float size)
        {
            if (_cubes.TryGetValue(size, out var m)) return m;
            m = new Mesh();
            float o = (size - 1) / 2, a = -0.5f - o, b = 0.5f + o;
            var v = new System.Collections.Generic.List<Vector3>(); var uv = new System.Collections.Generic.List<Vector2>(); var t = new System.Collections.Generic.List<int>();
            void Face(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
            {
                int i = v.Count; v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            Face(new(b, a, a), new(b, b, a), new(b, b, b), new(b, a, b));
            Face(new(a, a, b), new(a, b, b), new(a, b, a), new(a, a, a));
            Face(new(a, b, a), new(a, b, b), new(b, b, b), new(b, b, a));
            Face(new(a, a, a), new(b, a, a), new(b, a, b), new(a, a, b));
            Face(new(b, a, b), new(b, b, b), new(a, b, b), new(a, a, b));
            Face(new(a, a, a), new(a, b, a), new(b, b, a), new(b, a, a));
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            _cubes[size] = m;
            return m;
        }

        // Twelve thin bars along the edges of a unit cell (slightly larger than the block).
        static Mesh Edges()
        {
            if (_edges != null) return _edges;
            var combine = new System.Collections.Generic.List<CombineInstance>();
            const float w = 0.012f, e = 0.004f;
            var bar = UnitCube(1);
            for (int axis = 0; axis < 3; axis++)
                for (int i = 0; i < 4; i++)
                {
                    float u = (i & 1) == 0 ? -e : 1 + e, vv = (i & 2) == 0 ? -e : 1 + e;
                    Vector3 pos, scale;
                    if (axis == 0) { pos = new Vector3(0.5f, u, vv); scale = new Vector3(1 + 2 * e + w, w, w); }
                    else if (axis == 1) { pos = new Vector3(u, 0.5f, vv); scale = new Vector3(w, 1 + 2 * e + w, w); }
                    else { pos = new Vector3(u, vv, 0.5f); scale = new Vector3(w, w, 1 + 2 * e + w); }
                    combine.Add(new CombineInstance { mesh = bar, transform = Matrix4x4.TRS(pos, Quaternion.identity, scale) });
                }
            _edges = new Mesh();
            _edges.CombineMeshes(combine.ToArray(), true, true);
            return _edges;
        }

        static Mesh FlatQuad()
        {
            if (_flat != null) return _flat;
            _flat = new Mesh();
            _flat.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) };
            _flat.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            _flat.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _flat.RecalculateNormals();
            return _flat;
        }

        static Texture2D[] CrackStages()
        {
            var p = new Paint(99);
            var stages = new Texture2D[8];
            // Crack lines grow outward from the middle as the stage rises.
            var field = new float[Paint.S * Paint.S];
            for (int y = 0; y < Paint.S; y++)
                for (int x = 0; x < Paint.S; x++)
                {
                    p.Cells(x, y, 5, out float f1, out float f2, out _);
                    float edge = f2 - f1;
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 45f + p.V(x, y, 8, 2) * 0.35f;
                    field[x + y * Paint.S] = edge < 1.3f ? dist : 99;
                }
            for (int s = 0; s < 8; s++)
            {
                var t = new Texture2D(Paint.S, Paint.S, TextureFormat.R8, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[Paint.S * Paint.S];
                float reach = (s + 1) / 8f * 1.1f;
                for (int i = 0; i < px.Length; i++) { float v = field[i] < reach ? 1 : 0; px[i] = new Color(v, v, v, 1); }
                t.SetPixels(px); t.Apply();
                stages[s] = t;
            }
            return stages;
        }
    }
}
