using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stratum.Core;
using UnityEngine;

namespace Stratum.Game
{
    // "-smoketest <folder>": plays a short scripted tour of a fresh world and saves a screenshot of each stop,
    // logs frame rates, then quits. Lets a build be checked on a machine nobody is sitting at.
    public sealed partial class GameRoot
    {
        string _smokeDir;
        readonly List<string> _smokeLog = new List<string>();

        void MaybeSmokeTest()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-smoketest");
            if (i < 0) return;
            _smokeDir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Path.Combine(Application.persistentDataPath, "smoketest");
            Directory.CreateDirectory(_smokeDir);
            // Virtual and sleeping displays may never send a refresh, so don't wait for one.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Debug.Log("SMOKE starting, screenshots to " + _smokeDir);
            StartCoroutine(Smoke());
        }

        void SmokeNote(string s) { Debug.Log("SMOKE " + s); _smokeLog.Add(s); }

        IEnumerator Wait(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_smokeDir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            SmokeNote("shot " + name);
        }

        IEnumerator Fps(string label, float seconds)
        {
            for (int k = 0; k < 5; k++) yield return null;   // let the screenshot's own hitch pass
            int frames = 0, worstAt = 0; float worst = 0, start = Time.realtimeSinceStartup;
            int gc0 = GC.CollectionCount(0), gc2 = GC.CollectionCount(GC.MaxGeneration);
            while (Time.realtimeSinceStartup - start < seconds)
            {
                frames++;
                if (Time.unscaledDeltaTime > worst) { worst = Time.unscaledDeltaTime; worstAt = frames; }
                yield return null;
            }
            SmokeNote($"fps {label}: avg {frames / seconds:0.0}, worst frame {worst * 1000:0} ms (frame {worstAt} of {frames}), " +
                      $"GCs {GC.CollectionCount(0) - gc0}/{GC.CollectionCount(GC.MaxGeneration) - gc2}, chunks {_world?.LoadedChunks}, jobs {_world?.PendingJobs}");
        }

        IEnumerator Smoke()
        {
            float t0 = Time.realtimeSinceStartup;
            yield return Wait(8);
            yield return Shot("1_title");
            yield return Fps("title", 4);

            var meta = WorldStore.Create("Smoke Test", 20261119, false);
            StartCoroutine(Enter(meta));
            float load = Time.realtimeSinceStartup;
            while (_state != State.Playing && Time.realtimeSinceStartup - load < 180) yield return null;
            SmokeNote($"world ready in {Time.realtimeSinceStartup - load:0.0} s (state {_state})");
            if (_state != State.Playing) { Finish(); yield break; }
            SetCursor(false);
            _sky.Time01 = 0.4f;
            _player.Pitch = 8;
            yield return Wait(4);
            yield return Shot("2_spawn");
            yield return Fps("spawn", 5);

            foreach (var (item, n) in new[] { ("oak_log", 14), ("cobble", 40), ("stone_pickaxe", 1), ("coal", 6), ("torch", 16), ("oak_planks", 20), ("copper_ore", 5) })
                if (Items.Get(item) != null) _player.Inv.Add(item, n);
            _player.Inv.Selected = 2;
            OpenInventory(null);
            yield return Wait(1.5f);
            yield return Shot("3_backpack");
            CloseInventory();
            SetCursor(false);

            _player.Creative = true; _player.Flying = true;
            _player.Feet += new Vector3(0, 36, 0);
            _player.Pitch = 22;
            yield return Wait(8);
            yield return Shot("4_vista");
            _player.Yaw += 140;
            yield return Wait(6);
            yield return Shot("5_vista2");
            yield return Fps("flying", 5);

            _sky.Time01 = 0.262f;
            yield return Wait(2);
            yield return Shot("6_dawn");
            _sky.Time01 = 0.02f;
            yield return Wait(2);
            yield return Shot("7_night");

            // Find a cave under the player and light it with a few torches.
            var f = _player.Feet; bool found = false;
            for (int r = 0; r < 48 && !found; r += 3)
                for (int a = 0; a < 12 && !found; a++)
                {
                    int x = Mathf.FloorToInt(f.x + Mathf.Cos(a * 0.52f) * r), z = Mathf.FloorToInt(f.z + Mathf.Sin(a * 0.52f) * r);
                    if (!_world.IsMeshed(x, z)) continue;
                    for (int y = 20; y < 56 && !found; y++)
                    {
                        if (_world.Get(x, y, z) != Blocks.Air || _world.Get(x, y + 1, z) != Blocks.Air || !Blocks.Get(_world.Get(x, y - 1, z)).Solid) continue;
                        int open = 0;
                        for (int d = -4; d <= 4; d++) if (_world.Get(x + d, y + 1, z) == Blocks.Air && _world.Get(x, y + 1, z + d) == Blocks.Air) open++;
                        if (open < 7) continue;
                        found = true;
                        for (int k = 0; k < 4; k++)
                        {
                            int tx = x + (k % 2 == 0 ? 3 : -3), tz = z + (k < 2 ? 2 : -2);
                            for (int ty = y + 3; ty > y - 4; ty--)
                                if (_world.Get(tx, ty, tz) == Blocks.Air && Blocks.Get(_world.Get(tx, ty - 1, tz)).Solid) { _world.Set(tx, ty, tz, Blocks.Torch); break; }
                        }
                        _player.Feet = new Vector3(x + 0.5f, y, z + 0.5f);
                        _player.Pitch = 5;
                        SmokeNote($"cave at {x} {y} {z}");
                    }
                }
            _sky.Time01 = 0.45f;
            yield return Wait(3);
            for (float w = 0; w < 60 && _world.PendingJobs > 0; w += Time.unscaledDeltaTime) yield return null;
            yield return Wait(2);
            if (found) yield return Shot("8_cave");

            Save();
            SmokeNote($"done in {Time.realtimeSinceStartup - t0:0} s");
            Finish();
        }

        void Finish()
        {
            File.WriteAllLines(Path.Combine(_smokeDir, "smoke.txt"), _smokeLog);
            if (_meta != null && _meta.name == "Smoke Test") { var m = _meta; ToTitle(); WorldStore.Delete(m); }
            Application.Quit();
        }
    }
}
