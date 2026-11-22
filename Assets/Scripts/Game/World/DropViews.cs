using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stratum.Game
{
    public struct DropState
    {
        public int Id; public string Item; public int Count; public Vector3 Pos;
    }

    // Draws what lies on the ground: a small copy of each block, or the item's picture standing up, turning slowly
    // and bobbing, lit by the light where it lies.
    public sealed class DropViews : MonoBehaviour
    {
        sealed class View
        {
            public GameObject Go; public MeshRenderer Mr; public MeshFilter Mf; public string Item; public int Count;
            public Vector3 At; public float Phase, LightTimer; public bool Seen, Block; public GameObject Extra;
        }

        VoxelWorld _world;
        Material _blockMat, _itemMat;
        MaterialPropertyBlock _mpb;
        readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        readonly List<int> _gone = new List<int>();
        static Mesh _flat;
        public float Day = 1;                        // how bright the sky is (0 night .. 1 noon)

        public static DropViews Create(VoxelWorld world, Material blockMat)
        {
            var go = new GameObject("Drops");
            var d = go.AddComponent<DropViews>();
            d._world = world;
            d._blockMat = new Material(blockMat) { name = "DropBlock" };
            d._itemMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "DropItem" };
            d._itemMat.SetFloat("_AlphaClip", 1); d._itemMat.SetFloat("_Cutoff", 0.5f); d._itemMat.EnableKeyword("_ALPHATEST_ON");
            d._itemMat.SetFloat("_Cull", 0);
            d._mpb = new MaterialPropertyBlock();
            return d;
        }

        static Mesh Flat()
        {
            if (_flat != null) return _flat;
            _flat = new Mesh();
            _flat.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(-0.5f, 1, 0), new Vector3(0.5f, 1, 0), new Vector3(0.5f, 0, 0) };
            _flat.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            _flat.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _flat.RecalculateNormals();
            return _flat;
        }

        public void Clear()
        {
            foreach (var v in _views.Values) Destroy(v.Go);
            _views.Clear();
        }

        public void Show(List<DropState> list, float dt)
        {
            foreach (var v in _views.Values) v.Seen = false;
            foreach (var s in list)
            {
                if (!_views.TryGetValue(s.Id, out var v) || v.Item != s.Item)
                {
                    if (v != null) Destroy(v.Go);
                    v = Build(s); if (v == null) continue;
                    _views[s.Id] = v;
                }
                v.Seen = true;
                // A second copy behind the first shows there's more than one.
                if (v.Extra != null) v.Extra.SetActive(s.Count > 1);
                v.At = (v.At - s.Pos).sqrMagnitude > 9 ? s.Pos : Vector3.Lerp(v.At, s.Pos, 1 - Mathf.Exp(-dt * 14));
                v.Phase += dt;
                float bob = Mathf.Sin(v.Phase * 2.2f) * 0.05f + 0.12f;
                v.Go.transform.position = v.At + Vector3.up * bob;
                v.Go.transform.rotation = Quaternion.Euler(0, v.Phase * 70, 0);
                if ((v.LightTimer -= dt) <= 0)
                {
                    v.LightTimer = 0.4f;
                    float sky = 1, block = 0;
                    _world.LightAt(Mathf.FloorToInt(v.At.x), Mathf.FloorToInt(v.At.y + 0.3f), Mathf.FloorToInt(v.At.z), out sky, out block);
                    foreach (var mr in v.Go.GetComponentsInChildren<MeshRenderer>())
                    {
                        mr.GetPropertyBlock(_mpb);
                        if (v.Block) _mpb.SetVector("_LightOverride", new Vector4(sky, block, 0, 1));
                        else
                        {
                            float lit = Mathf.Clamp01(Mathf.Max(Mathf.Pow(0.8f, (1 - sky) * 15) * (Day * 0.85f + 0.15f), Mathf.Pow(0.8f, (1 - block) * 15)) * 1.05f);
                            _mpb.SetColor("_BaseColor", new Color(lit, lit, lit, 1));
                        }
                        mr.SetPropertyBlock(_mpb);
                    }
                }
            }
            _gone.Clear();
            foreach (var kv in _views) if (!kv.Value.Seen) _gone.Add(kv.Key);
            foreach (int id in _gone) { Destroy(_views[id].Go); _views.Remove(id); }
        }

        View Build(DropState s)
        {
            var it = Items.Get(s.Item);
            if (it == null) return null;
            var v = new View { Item = s.Item, At = s.Pos, Phase = (s.Id * 0.618f) % 6.28f };
            v.Go = new GameObject("Drop " + s.Item);
            v.Go.transform.SetParent(transform, false);
            v.Block = it.Block != 0 && !BlockMesh.IsFlat(it.Block);
            GameObject Part(Vector3 at)
            {
                var g = new GameObject("p");
                g.transform.SetParent(v.Go.transform, false);
                g.transform.localPosition = at;
                var mf = g.AddComponent<MeshFilter>(); var mr = g.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                if (v.Block)
                {
                    mf.sharedMesh = BlockMesh.Cube(it.Block); mr.sharedMaterial = _blockMat;
                    g.transform.localScale = Vector3.one * 0.26f;
                    g.transform.localPosition = at + new Vector3(-0.13f, 0, -0.13f);
                }
                else
                {
                    mf.sharedMesh = Flat(); mr.sharedMaterial = _itemMat;
                    mr.GetPropertyBlock(_mpb); _mpb.SetTexture("_BaseMap", Icons.TextureOf(it.Id)); mr.SetPropertyBlock(_mpb);
                    g.transform.localScale = Vector3.one * 0.42f;
                }
                return g;
            }
            Part(Vector3.zero);
            v.Extra = Part(new Vector3(0.09f, 0.05f, 0.1f));
            return v;
        }
    }
}
