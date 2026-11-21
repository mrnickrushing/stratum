using System;
using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Stratum.Game
{
    // Held items that show something: a compass (a needle toward the spawn), a clock (the sun and moon going
    // round) and a map of the land round about, drawn a few rows at a time, with you and your friends on it.
    public sealed partial class Hud
    {
        public Func<Vector3> SpawnPoint;
        public Func<IEnumerable<(string name, Vector3 pos)>> Others;

        RectTransform _compass, _needle, _clockDial;
        Image _clockFace;
        RectTransform _map, _mapMarks;
        RawImage _mapImage;
        Texture2D _mapTex;
        Color32[] _mapPx;
        const int MapSize = 128, MapScale = 2;          // pixels across, blocks a pixel
        Vector2Int _mapCentre; int _mapRow; bool _mapFresh;
        readonly List<Text> _mapNames = new List<Text>();
        RectTransform _mapYou;

        void BuildHeld()
        {
            // Compass and clock: round dials at the right of the hotbar.
            _compass = UIKit.Panel(_root, "Compass", new Color(0.78f, 0.8f, 0.84f), UIKit.Circle).rectTransform;
            _compass.Place(0.5f, 0, -520, 70, 110, 110);
            var face = UIKit.Panel(_compass, "Face", new Color(0.96f, 0.94f, 0.88f), UIKit.Circle); face.rectTransform.Fill(8);
            var n = UIKit.Label(face.transform, "N", 18, new Color(0.6f, 0.15f, 0.1f), TextAnchor.UpperCenter, FontStyle.Bold); n.rectTransform.Fill(2);
            _needle = UIKit.Rect(face.transform, "Needle"); _needle.Fill();
            var red = UIKit.Panel(_needle, "Red", new Color(0.85f, 0.2f, 0.15f), UIKit.White); red.rectTransform.Place(0.5f, 0.5f, 0, 18, 8, 38);
            var grey = UIKit.Panel(_needle, "Grey", new Color(0.3f, 0.32f, 0.36f), UIKit.White); grey.rectTransform.Place(0.5f, 0.5f, 0, -18, 8, 38);
            _compass.gameObject.SetActive(false);

            _clockFace = UIKit.Panel(_root, "Clock", new Color(0.86f, 0.68f, 0.2f), UIKit.Circle);
            _clockFace.rectTransform.Place(0.5f, 0, -520, 70, 110, 110);
            var mask = UIKit.Panel(_clockFace.transform, "Window", Color.white, UIKit.Circle); mask.rectTransform.Fill(8);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            _clockDial = UIKit.Rect(mask.transform, "Dial"); _clockDial.Place(0.5f, 0.5f, 0, 0, 94, 94);
            var dayHalf = UIKit.Panel(_clockDial, "Day", new Color(0.42f, 0.7f, 1f), UIKit.White); dayHalf.rectTransform.Place(0.5f, 1, 0, -23.5f, 94, 47);
            var nightHalf = UIKit.Panel(_clockDial, "Night", new Color(0.07f, 0.1f, 0.2f), UIKit.White); nightHalf.rectTransform.Place(0.5f, 0, 0, 23.5f, 94, 47);
            var sun = UIKit.Panel(_clockDial, "Sun", new Color(1f, 0.85f, 0.3f), UIKit.Circle); sun.rectTransform.Place(0.5f, 1, 0, -20, 22, 22);
            var moon = UIKit.Panel(_clockDial, "Moon", new Color(0.9f, 0.92f, 0.98f), UIKit.Circle); moon.rectTransform.Place(0.5f, 0, 0, 20, 16, 16);
            var hand = UIKit.Panel(_clockFace.transform, "Mark", new Color(0.25f, 0.18f, 0.05f), UIKit.White); hand.rectTransform.Place(0.5f, 1, 0, -10, 4, 18);
            _clockFace.gameObject.SetActive(false);

            // The map, top right.
            _map = UIKit.Panel(_root, "Map", new Color(0.82f, 0.74f, 0.56f), UIKit.RoundSmall).rectTransform;
            _map.Place(1, 1, -24, -60, 420, 420);
            _map.pivot = new Vector2(1, 1); _map.anchoredPosition = new Vector2(-24, -60);
            _mapTex = new Texture2D(MapSize, MapSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _mapPx = new Color32[MapSize * MapSize];
            for (int i = 0; i < _mapPx.Length; i++) _mapPx[i] = new Color32(214, 200, 160, 255);
            _mapTex.SetPixels32(_mapPx); _mapTex.Apply();
            var img = new GameObject("Land", typeof(RectTransform), typeof(RawImage));
            img.transform.SetParent(_map, false);
            _mapImage = img.GetComponent<RawImage>(); _mapImage.texture = _mapTex; _mapImage.raycastTarget = false;
            ((RectTransform)img.transform).Fill(14);
            _mapMarks = UIKit.Rect(img.transform, "Marks"); _mapMarks.Fill();
            _mapYou = UIKit.Panel(_mapMarks, "You", Color.white, UIKit.White).rectTransform;
            _mapYou.sizeDelta = new Vector2(12, 18);
            var tip = UIKit.Panel(_mapYou, "Tip", new Color(0.9f, 0.2f, 0.15f), UIKit.White); tip.rectTransform.Place(0.5f, 1, 0, -3, 12, 6);
            _map.gameObject.SetActive(false);
        }

        void UpdateHeld(float dt, string held, bool show)
        {
            if (_compass == null) BuildHeld();
            bool compass = show && held == "compass", clock = show && held == "clock", map = show && held == "map";
            if (_compass.gameObject.activeSelf != compass) _compass.gameObject.SetActive(compass);
            if (_clockFace.gameObject.activeSelf != clock) _clockFace.gameObject.SetActive(clock);
            if (_map.gameObject.activeSelf != map) { _map.gameObject.SetActive(map); _mapFresh = false; }
            var cam = Camera.main;
            float yaw = cam != null ? cam.transform.eulerAngles.y : 0;
            if (compass)
            {
                var to = (SpawnPoint?.Invoke() ?? Vector3.zero) - P.Feet;
                float ang = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - yaw;
                // A little wobble, like a real needle settling.
                ang += Mathf.Sin(Time.time * 2.3f) * 2;
                _needle.localRotation = Quaternion.Slerp(_needle.localRotation, Quaternion.Euler(0, 0, -ang), 1 - Mathf.Exp(-dt * 8));
            }
            if (clock) _clockDial.localRotation = Quaternion.Euler(0, 0, (Sky.Time01 - 0.5f) * 360);
            if (map) DrawMap(yaw);
        }

        static readonly Color32 Sea = new Color32(64, 104, 190, 255);

        Color32 MapColour(int wx, int wz, out int h)
        {
            if (!World.Top(wx, wz, out h, out ushort id)) { h = -1; return new Color32(214, 200, 160, 255); }
            var d = Blocks.Get(id);
            if (d.Fluid == 1)
            {
                int depth = 0; while (depth < 12 && Blocks.IsWater(World.Get(wx, h - depth - 1, wz))) depth++;
                float k = 1 - depth / 16f;
                return new Color32((byte)(Sea.r * k + 20), (byte)(Sea.g * k + 30), (byte)(Sea.b * k + 40), 255);
            }
            Color c = id == Blocks.Grass ? new Color(0.42f, 0.6f, 0.26f) : d.Sway && d.Shape == Shape.Cube ? new Color(0.25f, 0.45f, 0.18f)
                    : BlockTextures.Average[(int)d.Top];
            return c;
        }

        void DrawMap(float yaw)
        {
            int px = Mathf.FloorToInt(P.Feet.x), pz = Mathf.FloorToInt(P.Feet.z);
            var want = new Vector2Int(Mathf.RoundToInt(px / 64f) * 64, Mathf.RoundToInt(pz / 64f) * 64);
            if (want != _mapCentre || !_mapFresh) { _mapCentre = want; _mapRow = 0; _mapFresh = true; }
            // A few rows a frame; then start over to catch changes.
            int x0 = _mapCentre.x - MapSize * MapScale / 2, z0 = _mapCentre.y - MapSize * MapScale / 2;
            for (int r = 0; r < 6; r++)
            {
                int row = _mapRow;
                _mapRow = (_mapRow + 1) % MapSize;
                int wz = z0 + row * MapScale;
                for (int col = 0; col < MapSize; col++)
                {
                    int wx = x0 + col * MapScale;
                    var c = MapColour(wx, wz, out int h);
                    if (h >= 0)
                    {
                        // Slopes facing north-west light, the others shade.
                        World.Top(wx, wz - MapScale, out int hn, out _);
                        float k = h > hn ? 1.12f : h < hn ? 0.82f : 1f;
                        c = new Color32((byte)Mathf.Min(255, c.r * k), (byte)Mathf.Min(255, c.g * k), (byte)Mathf.Min(255, c.b * k), 255);
                    }
                    _mapPx[col + row * MapSize] = c;
                }
            }
            _mapTex.SetPixels32(_mapPx); _mapTex.Apply();

            // You (an arrow pointing the way you face) and your friends (dots with names).
            var area = _mapMarks.rect.size;
            Vector2 ToMap(Vector3 w) => new Vector2((w.x - x0) / (MapSize * MapScale) * area.x - area.x / 2, (w.z - z0) / (MapSize * MapScale) * area.y - area.y / 2);
            _mapYou.anchoredPosition = ToMap(P.Feet);
            _mapYou.localRotation = Quaternion.Euler(0, 0, -yaw);
            int i = 0;
            if (Others != null)
                foreach (var (name, pos) in Others())
                {
                    if (i >= _mapNames.Count)
                    {
                        var dot = UIKit.Panel(_mapMarks, "Friend", new Color(0.2f, 0.85f, 1f), UIKit.Circle); dot.rectTransform.sizeDelta = new Vector2(12, 12);
                        var label = UIKit.Label(dot.transform, "", 16, Color.white, TextAnchor.LowerCenter, FontStyle.Bold).Shadowed(1.5f);
                        label.rectTransform.Place(0.5f, 1, 0, 14, 200, 22);
                        _mapNames.Add(label);
                    }
                    var t = _mapNames[i++];
                    var m = ToMap(pos);
                    bool inside = Mathf.Abs(m.x) < area.x / 2 && Mathf.Abs(m.y) < area.y / 2;
                    var dotRt = (RectTransform)t.transform.parent;
                    dotRt.gameObject.SetActive(true);
                    dotRt.anchoredPosition = new Vector2(Mathf.Clamp(m.x, -area.x / 2, area.x / 2), Mathf.Clamp(m.y, -area.y / 2, area.y / 2));
                    t.text = inside ? name : name + " →";
                }
            for (int k = i; k < _mapNames.Count; k++) _mapNames[k].transform.parent.gameObject.SetActive(false);
        }
    }
}
