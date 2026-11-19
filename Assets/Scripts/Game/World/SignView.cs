using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;

namespace Stratum.Game
{
    // The writing on signs: a line of text on the front of each board, made when its block is there to stand on.
    public sealed class SignView : MonoBehaviour
    {
        VoxelWorld _world;
        readonly Dictionary<Vector3Int, string> _texts = new Dictionary<Vector3Int, string>();
        readonly Dictionary<Vector3Int, GameObject> _shown = new Dictionary<Vector3Int, GameObject>();
        float _check;

        public static SignView Create(VoxelWorld world)
        {
            var go = new GameObject("Signs");
            var v = go.AddComponent<SignView>();
            v._world = world;
            return v;
        }

        public string TextAt(Vector3Int at) => _texts.TryGetValue(at, out var t) ? t : "";

        public void Set(Vector3Int at, string text)
        {
            if (_shown.TryGetValue(at, out var go)) { Destroy(go); _shown.Remove(at); }
            if (string.IsNullOrWhiteSpace(text)) { _texts.Remove(at); return; }
            _texts[at] = text;
            Build(at);
        }

        void Update()
        {
            // Signs whose blocks arrived after their words (joining a server), or that were turned or broken.
            if ((_check -= Time.deltaTime) > 0) return;
            _check = 0.5f;
            foreach (var kv in _texts)
            {
                var d = Blocks.Get(_world.Get(kv.Key.x, kv.Key.y, kv.Key.z));
                bool sign = d.BaseId == Blocks.Sign || d.BaseId == Blocks.WallSign;
                bool shown = _shown.ContainsKey(kv.Key);
                if (sign && !shown) { Build(kv.Key); break; }
                if (!sign && shown) { Destroy(_shown[kv.Key]); _shown.Remove(kv.Key); break; }
            }
        }

        void Build(Vector3Int at)
        {
            if (!_texts.TryGetValue(at, out var text)) return;
            var d = Blocks.Get(_world.Get(at.x, at.y, at.z));
            bool standing = d.BaseId == Blocks.Sign;
            if (!standing && d.BaseId != Blocks.WallSign) return;
            var go = new GameObject("Sign text");
            go.transform.SetParent(transform, false);
            var rot = Quaternion.Euler(0, d.Facing * 90, 0);
            // The front of the board looks toward -Z before turning (toward whoever placed it).
            var local = standing ? new Vector3(0, 0.75f, 7 / 16f - 0.5f - 0.004f) : new Vector3(0, 0.5f, 14 / 16f - 0.5f - 0.004f);
            go.transform.position = new Vector3(at.x + 0.5f, at.y, at.z + 0.5f) + rot * local;
            go.transform.rotation = rot;
            var tm = go.AddComponent<TextMesh>();
            tm.font = UIKit.Font;
            tm.fontSize = 48;
            tm.characterSize = 0.021f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.lineSpacing = 0.9f;
            tm.color = new Color(0.16f, 0.1f, 0.05f);
            tm.text = text;
            go.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;
            _shown[at] = go;
        }
    }
}
