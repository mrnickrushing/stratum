using System.Collections.Generic;
using Stratum.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Stratum.Game
{
    // Goals: things done for the first time in this world, noticed by looking at the player once a second and
    // remembered on this computer.
    public sealed partial class GameRoot
    {
        readonly HashSet<string> _goals = new HashSet<string>();
        string _goalsKey, _myName; float _goalsTimer;

        string GoalsKey => "goals_" + (_meta != null ? _meta.name + "_" + _meta.seed : "online");

        void LoadGoals()
        {
            _goalsKey = GoalsKey; _goals.Clear();
            foreach (var id in PlayerPrefs.GetString(_goalsKey, "").Split(',')) if (id.Length > 0) _goals.Add(id);
        }

        void Reach(string id)
        {
            if (!_goals.Add(id)) return;
            PlayerPrefs.SetString(_goalsKey, string.Join(",", _goals)); PlayerPrefs.Save();
            foreach (var (gid, title, how) in Hud.Goals)
                if (gid == id) { _hud.Toast("Goal reached: " + title); _hud.AddChat("", "Goal reached: " + title + "  (G shows them all)"); }
            Sfx.I?.Play("levelup", 0.5f, 1.2f);
        }

        void GoalsTick(Keyboard kb)
        {
            if (_player == null || _hud == null || _state != State.Playing) return;
            if (_goalsKey != GoalsKey) LoadGoals();
            // G opens and closes the list; Tab held shows who is here.
            if (kb != null && !_hud.Typing && !_hud.ChatOpen)
            {
                if (kb.gKey.wasPressedThisFrame && (!_hud.InventoryOpen || _hud.GoalsOpen)) _hud.ShowGoals(!_hud.GoalsOpen, _goals);
                else if (kb.escapeKey.wasPressedThisFrame && _hud.GoalsOpen) _hud.ShowGoals(false, _goals);
                _hud.ShowWho(kb.tabKey.isPressed && !_hud.InventoryOpen, Online ? _myName : null, _avatars != null ? _avatars.Names : null);
            }
            if (_player.Creative || (_goalsTimer -= Time.unscaledDeltaTime) > 0) return;
            _goalsTimer = 1;
            var inv = _player.Inv;
            bool potion = false; int worn = 0;
            for (int i = 0; i < Inventory.Main; i++)
            {
                string it = inv.Slots[i].Item;
                if (it == null) continue;
                if (it.EndsWith("_log")) Reach("wood");
                else if (it == "workbench") Reach("bench");
                else if (it.StartsWith("wood_pickaxe")) Reach("pick");
                else if (it.StartsWith("stone_pickaxe")) Reach("stone");
                else if (it == "furnace") Reach("furnace");
                else if (it == "iron_ingot") Reach("iron");
                else if (it == "bread") Reach("bread");
                else if (it == "raw_fish") Reach("fish");
                else if (it == "blast_keg") Reach("keg");
                else if (it == "piston" || it == "sticky_piston") Reach("piston");
                else if (it == "starstone") Reach("star");
                else if (it == "colossus_heart") Reach("heart");
                else if (it.StartsWith("potion_")) potion = true;
            }
            if (potion) Reach("potion");
            for (int i = Inventory.ArmourStart; i < Inventory.Size; i++) if (!inv.Slots[i].Empty) worn++;
            if (worn == 4) Reach("armour");
            if (inv.HasShield) Reach("shield");
            if (_player.Asleep) Reach("bed");
            if (_player.Riding == MobKind.Camel) Reach("camel");
            if (Ember.In(_player.Feet.x)) Reach("ember");
            if (_player.XpLevel >= 15) Reach("level");
            if (_link != null)
                foreach (var m in _link.Mobs)
                    if (m.Kind == MobKind.Hound && (m.More & Mob.MTame) != 0 && (m.Pos - _player.Feet).sqrMagnitude < 8 * 8) { Reach("hound"); break; }
        }
    }
}
