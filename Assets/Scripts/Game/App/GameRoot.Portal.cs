using Stratum.Core;
using UnityEngine;

namespace Stratum.Game
{
    // Ember portals: stand in one for a few seconds and the world turns; you come out by a portal on the other
    // side (one is built if there isn't one near). The land there has to arrive first, so the player is held
    // still above the spot until it has.
    public sealed partial class GameRoot
    {
        float _inPortal;
        bool _leftPortal = true;
        Vector3? _arrive;
        Vector3 _departed;
        bool _arriveEmber;
        float _arriveWait;
        bool _asked;

        // Leaving a world (or a server) in the middle of a portal trip: the trip is called off. The player counts as
        // still standing where they left from, so that is what gets saved, and nothing of the trip is left to act on
        // the next world.
        void AbortPortal()
        {
            if (_arrive != null && _player != null) { _player.Feet = _departed; _player.Velocity = Vector3.zero; _player.ResetFall(); }
            _arrive = null; _asked = false; _arriveWait = 0; _inPortal = 0; _leftPortal = true;
            if (_hud != null) _hud.PortalGlow(0);
        }
        public bool InTransit => _arrive != null;

        void TickPortal(float dt)
        {
            if (_player == null || _world == null) return;
            _sky.Deep = Ember.In(_player.Feet.x);
            if (_arrive != null) { HoldArrival(dt); return; }
            var f = _player.Feet;
            int x = Mathf.FloorToInt(f.x), z = Mathf.FloorToInt(f.z);
            bool inside = Ember.IsPortal(_world.Get(x, Mathf.FloorToInt(f.y + 0.2f), z)) || Ember.IsPortal(_world.Get(x, Mathf.FloorToInt(f.y + 1.2f), z));
            if (!inside || _player.Health <= 0) { _inPortal = Mathf.Max(0, _inPortal - dt * 2); _leftPortal |= !inside; _hud.PortalGlow(_inPortal / 3f); return; }
            if (!_leftPortal) { _hud.PortalGlow(0); return; }
            if (_inPortal == 0) Sfx.I?.Play("portal", 0.5f);
            _inPortal += dt;
            float need = _player.Creative ? 0.8f : 3f;
            _hud.PortalGlow(_inPortal / need);
            if (_inPortal >= need) Travel();
        }

        void Travel()
        {
            _inPortal = 0; _leftPortal = false;
            var f = _player.Feet;
            bool toEmber = !Ember.In(f.x);
            var (tx, tz) = toEmber ? Ember.ToEmber(f.x, f.z) : Ember.ToOverworld(f.x, f.z);
            _player.GetOut();
            _departed = f;
            _arrive = new Vector3(Mathf.Floor(tx) + 0.5f, toEmber ? 70 : 140, Mathf.Floor(tz) + 0.5f);
            _arriveEmber = toEmber; _arriveWait = 0; _asked = false;
            _player.Feet = _arrive.Value; _player.Velocity = Vector3.zero;
            Sfx.I?.Play("portal", 0.8f, 0.7f);
            _hud.Toast(toEmber ? "The Ember Deep" : "Back to the surface");
            if (Online) SendMove(true);
        }

        void HoldArrival(float dt)
        {
            var a = _arrive.Value;
            _player.Feet = a; _player.Velocity = Vector3.zero;
            _player.ResetFall();
            _arriveWait += dt;
            _hud.PortalGlow(Mathf.Clamp01(1 - _arriveWait * 0.5f) * 0.8f + 0.2f);
            if (_asked)
            {
                // The server never answered: back to the portal we left from, not loose in the holding place.
                if (_arriveWait > 20)
                {
                    _arrive = null; _hud.PortalGlow(0); _leftPortal = false;
                    _player.Feet = _departed; _player.Velocity = Vector3.zero; _world.SetFocus(_departed);
                    _player.ResetFall();
                    if (Online) SendMove(true);
                    _hud.Toast("The portal didn't open. Step out and try again.");
                }
                return;
            }
            int ax = Mathf.FloorToInt(a.x), az = Mathf.FloorToInt(a.z);
            int R = _arriveEmber ? 24 : 44;
            bool ready = _world.IsReady(ax, az);
            for (int dz = -R; dz <= R && ready; dz += R) for (int dx = -R; dx <= R && ready; dx += R) ready = _world.IsReady(ax + dx, az + dz);
            if (!ready && _arriveWait < 25) return;
            int surface = 64;
            if (_world.Top(ax, az, out int ty, out _)) surface = ty;
            if (Online)
            {
                SendMove(true);
                _net.Send(new Packet(Net.Msg.Portal).I32(ax).I32(az).Bool(_arriveEmber).I32(surface));
                _asked = true;
                return;
            }
            var (sx, sy, sz) = Ember.Arrive(_world.Get, (x, y, z, id) => _world.Set(x, y, z, id), ax, az, _arriveEmber, surface);
            Land(new Vector3(sx, sy + 0.01f, sz));
        }

        // Where the server (or this computer) says to stand on the far side.
        void Land(Vector3 at)
        {
            _arrive = null;
            _player.Feet = at; _player.Velocity = Vector3.zero;
            _player.ResetFall();              // the wait above the far side isn't a fall
            _hud.PortalGlow(0);
            if (Online) SendMove(true);
        }
    }
}
