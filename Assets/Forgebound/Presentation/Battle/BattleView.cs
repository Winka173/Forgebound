using System.Collections.Generic;
using Forgebound.Core.Combat;
using Forgebound.Core.Content;
using UnityEngine;

namespace Forgebound.Presentation
{
    /// <summary>
    /// Renders a running <see cref="Battle"/>: steps the deterministic simulation at a fixed 30 Hz (x speed), interpolates
    /// unit positions between ticks, and turns the combat event stream into animation, VFX, numbers and sound.
    /// The view never writes to the simulation.
    /// </summary>
    public sealed class BattleView
    {
        private readonly Services _s;
        public readonly Battle Battle;
        private readonly Transform _root;
        private readonly Dictionary<int, UnitView> _units = new Dictionary<int, UnitView>();
        private readonly Dictionary<int, ProjectileView> _projectiles = new Dictionary<int, ProjectileView>();
        private readonly Dictionary<int, ZoneView> _zones = new Dictionary<int, ZoneView>();
        private readonly VfxPool _vfx;
        private int _eventCursor;
        private float _acc;
        public float Speed = 1f;        // 0 = paused, 1x, 2x
        public bool Finished { get { return Battle.Finished && _endDelay <= 0; } }
        private float _endDelay = 1.2f;
        private readonly Dictionary<int, int> _prevX = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _curX = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _prevP = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _curP = new Dictionary<int, int>();
        public string LastPhaseName;
        public int ProcFlashesThisSecond;
        private float _secondTimer;
        private readonly string _frameSfxPrimary;

        public BattleView(Services s, Battle battle)
        {
            _s = s;
            Battle = battle;
            _root = new GameObject("BattleView").transform;
            _root.SetParent(BattleWorld.Instance.Stage, false);
            int budget = Mathf.Max(16, 96 * s.Profile.Settings.ParticleBudgetPercent / 100);
            _vfx = new VfxPool(_root, budget, s.Profile.Settings.ReducedFlashes);
            BattleWorld.Instance.ShakeScale = s.Profile.Settings.ScreenShakePercent / 100f;
            foreach (var u in battle.Units) EnsureUnit(u);
            if (battle.Weapons.Length > 0) _frameSfxPrimary = battle.Weapons[0].Spec.Frame.Sfx;
            SnapshotPositions();
            SnapshotPositions();
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
        }

        private void SnapshotPositions()
        {
            foreach (var u in Battle.Units)
            {
                int cur;
                _prevX[u.Id] = _curX.TryGetValue(u.Id, out cur) ? cur : u.X;
                _curX[u.Id] = u.X;
            }
            foreach (var p in Battle.Projectiles)
            {
                int cur;
                _prevP[p.Id] = _curP.TryGetValue(p.Id, out cur) ? cur : p.X;
                _curP[p.Id] = p.X;
            }
        }

        /// <summary>Advance presentation; returns true while the battle is still playing.</summary>
        public void Update(float dt)
        {
            _secondTimer += dt;
            if (_secondTimer > 1f) { _secondTimer = 0; ProcFlashesThisSecond = 0; }
            if (!Battle.Finished)
            {
                _acc += dt * Speed * FMathTicks;
                int steps = 0;
                while (_acc >= 1f && !Battle.Finished && steps < 8)
                {
                    Battle.Step();
                    _acc -= 1f;
                    steps++;
                    SnapshotPositions();
                    ConsumeEvents();
                }
            }
            else _endDelay -= dt;
            float alpha = Mathf.Clamp01(_acc);
            SyncUnits(alpha, dt);
            SyncProjectiles(alpha);
            SyncZones();
            _vfx.Update(dt);
        }

        private const float FMathTicks = 30f;

        /// <summary>Instantly finish (Skip): the simulation result is identical; only presentation is skipped.</summary>
        public void SkipToEnd()
        {
            while (!Battle.Finished) Battle.Step();
            _eventCursor = Battle.Events.Count;
            _endDelay = 0;
        }

        // ------------------------------------------------------------------ units

        private UnitView EnsureUnit(Unit u)
        {
            UnitView v;
            if (_units.TryGetValue(u.Id, out v)) return v;
            v = new UnitView(_s, _root, u, Battle);
            _units[u.Id] = v;
            return v;
        }

        private void SyncUnits(float alpha, float dt)
        {
            var alive = new HashSet<int>();
            foreach (var u in Battle.Units)
            {
                alive.Add(u.Id);
                var v = EnsureUnit(u);
                int px, cx;
                if (!_prevX.TryGetValue(u.Id, out px)) px = u.X;
                if (!_curX.TryGetValue(u.Id, out cx)) cx = u.X;
                float x = Mathf.Lerp(BattleWorld.WorldX(px), BattleWorld.WorldX(cx), alpha);
                v.Sync(u, x, dt);
            }
            var remove = new List<int>();
            foreach (var kv in _units) if (!alive.Contains(kv.Key)) { kv.Value.Destroy(); remove.Add(kv.Key); }
            foreach (var id in remove) _units.Remove(id);
        }

        private void SyncProjectiles(float alpha)
        {
            var alive = new HashSet<int>();
            foreach (var p in Battle.Projectiles)
            {
                if (p.Dead) continue;
                alive.Add(p.Id);
                ProjectileView v;
                if (!_projectiles.TryGetValue(p.Id, out v)) { v = new ProjectileView(_root, p, Battle); _projectiles[p.Id] = v; }
                int px, cx;
                if (!_prevP.TryGetValue(p.Id, out px)) px = p.X;
                if (!_curP.TryGetValue(p.Id, out cx)) cx = p.X;
                v.Sync(p, Mathf.Lerp(BattleWorld.WorldX(px), BattleWorld.WorldX(cx), alpha), Battle);
            }
            var remove = new List<int>();
            foreach (var kv in _projectiles) if (!alive.Contains(kv.Key)) { kv.Value.Destroy(); remove.Add(kv.Key); }
            foreach (var id in remove) { _projectiles.Remove(id); _prevP.Remove(id); _curP.Remove(id); }
        }

        private void SyncZones()
        {
            var alive = new HashSet<int>();
            foreach (var z in Battle.Zones)
            {
                if (z.Dead) continue;
                alive.Add(z.Id);
                ZoneView v;
                if (!_zones.TryGetValue(z.Id, out v)) { v = new ZoneView(_root, z); _zones[z.Id] = v; }
                v.Sync(z);
            }
            var remove = new List<int>();
            foreach (var kv in _zones) if (!alive.Contains(kv.Key)) { kv.Value.Destroy(); remove.Add(kv.Key); }
            foreach (var id in remove) _zones.Remove(id);
        }

        private Vector3 UnitPos(int id, float yOffset = 0.9f)
        {
            UnitView v;
            if (_units.TryGetValue(id, out v)) return v.Root.position + new Vector3(0, yOffset, 0);
            return new Vector3(0, BattleWorld.GroundY + yOffset, 0);
        }

        // ------------------------------------------------------------------ events → feedback

        private void ConsumeEvents()
        {
            var events = Battle.Events;
            int damageNumbersThisTick = 0;
            for (; _eventCursor < events.Count; _eventCursor++)
            {
                var e = events[_eventCursor];
                switch (e.Type)
                {
                    case CombatEventType.AttackWindup:
                        { UnitView v; if (_units.TryGetValue(e.A, out v)) v.Windup(e.Weapon, e.Value2); }
                        break;
                    case CombatEventType.AttackRelease:
                        {
                            UnitView v;
                            if (_units.TryGetValue(e.A, out v)) v.Attack(e.Weapon, e.Proc);
                            if (e.A == Battle.Hero.Id && e.Weapon >= 0 && e.Weapon < Battle.Weapons.Length && !e.Proc)
                            {
                                var spec = Battle.Weapons[e.Weapon].Spec;
                                _s.Audio.Play(spec.Frame.Sfx ?? "sfx_hit_generic", e.Weapon == 0 ? 0.9f : 0.6f);
                                if (spec.Core != null && spec.Core.Sfx != null) _s.Audio.Play(spec.Core.Sfx, 0.35f);
                                var tgt = UnitPos(e.B);
                                if (spec.Steps[e.Value % spec.Steps.Count].Kind == StepKind.Melee) _vfx.Slash(tgt, Theme.DamageType(spec.DamageType), spec.Frame.Tags.Contains("heavy"));
                            }
                            else if (e.Weapon < 0 && e.A != Battle.Hero.Id) _s.Audio.Play("sfx_enemy_attack", 0.5f);
                        }
                        break;
                    case CombatEventType.Damage:
                        {
                            UnitView v;
                            if (_units.TryGetValue(e.B, out v)) v.Hit(e.Crit);
                            bool heroHit = e.B == Battle.Hero.Id;
                            if (damageNumbersThisTick < 6 || e.Crit || heroHit)
                            {
                                damageNumbersThisTick++;
                                _vfx.Number(UnitPos(e.B, 1.4f), e.Value, heroHit ? Theme.Danger : Theme.DamageType((DamageType)e.Value2), e.Crit, e.Proc);
                            }
                            if (e.Crit) { BattleWorld.Instance.Shake(0.25f); _s.Audio.Play("sfx_crit", 0.7f); }
                            if (heroHit) _s.Audio.Play("sfx_hero_hurt", 0.6f);
                            else _s.Audio.Play("sfx_hit_generic", e.Proc ? 0.25f : 0.45f);
                        }
                        break;
                    case CombatEventType.Miss:
                        _vfx.Text(UnitPos(e.B, 1.4f), _s.L("battle.miss"), Theme.Muted, 0.35f);
                        break;
                    case CombatEventType.Heal:
                        _vfx.Number(UnitPos(e.B, 1.6f), e.Value, Theme.Ok, false, false, "+");
                        break;
                    case CombatEventType.ShieldGain:
                        if (e.B == Battle.Hero.Id && e.Value >= 3) _vfx.Ring(UnitPos(e.B, 0.6f), Theme.Link, 0.9f, 0.3f);
                        break;
                    case CombatEventType.ShieldBreak:
                        _vfx.Burst(UnitPos(e.B, 0.8f), Theme.Link, 10);
                        _s.Audio.Play("sfx_shield_break", 0.8f);
                        break;
                    case CombatEventType.StatusApplied:
                        { UnitView v; if (_units.TryGetValue(e.B, out v)) v.Flash(StatusColor((StatusType)e.Value)); }
                        break;
                    case CombatEventType.Death:
                        _vfx.Burst(UnitPos(e.A, 0.6f), Theme.Ember, 14);
                        _s.Audio.Play("sfx_enemy_death", 0.6f);
                        break;
                    case CombatEventType.RuneProc:
                        if (ProcFlashesThisSecond < 8)
                        {
                            ProcFlashesThisSecond++;
                            _vfx.Glyph(UnitPos(Battle.Hero.Id, 2.2f), Theme.Gold);
                            _s.Audio.Play("sfx_rune_proc", 0.35f);
                        }
                        break;
                    case CombatEventType.LinkCharge:
                        { UnitView v; if (_units.TryGetValue(Battle.Hero.Id, out v)) v.LinkPulse(e.Weapon); }
                        break;
                    case CombatEventType.LinkFire:
                        _vfx.Beam(UnitPos(Battle.Hero.Id, 1.2f), UnitPos(e.B, 0.8f), Theme.Link);
                        _s.Audio.Play("sfx_relay_fire", 0.6f);
                        break;
                    case CombatEventType.Telegraph:
                        _vfx.Warning(new Vector3(BattleWorld.WorldX(e.X), BattleWorld.GroundY - 0.35f, 0), e.Value2 / 1000f, e.Weapon / 30f);
                        _s.Audio.Play("sfx_telegraph", 0.7f, 1f, 0.9f);
                        break;
                    case CombatEventType.AbilityCast:
                        if (e.Tag == "slam_impact") { BattleWorld.Instance.Shake(0.6f); _s.Audio.Play("sfx_slam", 0.9f); }
                        break;
                    case CombatEventType.PhaseChange:
                        LastPhaseName = e.Tag != null ? _s.L(e.Tag) : null;
                        BattleWorld.Instance.Shake(0.8f);
                        _s.Audio.Stinger("sting_boss_phase");
                        break;
                    case CombatEventType.Summon:
                        _vfx.Ring(UnitPos(e.B, 0.4f), Theme.Hex("4fa3ff"), 1.4f, 0.5f);
                        break;
                    case CombatEventType.ZoneSpawn:
                        if (e.Tag == "explosion" || e.Tag == "capacitor" || e.Tag == "volatile" || e.Tag == "rune_nova" || e.Tag == "nova")
                        {
                            _vfx.Ring(new Vector3(BattleWorld.WorldX(e.X), BattleWorld.GroundY + 0.5f, 0), e.Tag == "capacitor" ? Theme.Link : Theme.Ember, e.Value / 1000f * 2f, 0.35f);
                            _s.Audio.Play("sfx_explosion", 0.5f);
                        }
                        break;
                    case CombatEventType.SignatureFire:
                        _vfx.Burst(UnitPos(e.B != 0 ? e.B : Battle.Hero.Id, 1f), Theme.Gold, 18);
                        _s.Audio.Play("sfx_legendary_signature", 0.8f);
                        break;
                    case CombatEventType.Revive:
                        _vfx.Ring(UnitPos(e.A, 0.6f), Theme.Gold, 3f, 0.8f);
                        _s.Audio.Stinger("sting_revive");
                        break;
                    case CombatEventType.Enrage:
                        _vfx.Text(UnitPos(e.A != 0 ? e.A : Battle.Hero.Id, 2.4f), _s.L("battle.enrage"), Theme.Danger, 1f);
                        break;
                    case CombatEventType.WaveStart:
                        if (e.Value > 0) _vfx.Banner(_s.LF("battle.wave", e.Value + 1, e.Value2));
                        break;
                    case CombatEventType.BattleEnd:
                        _s.Audio.Stinger(e.Value == 1 ? "sting_victory" : "sting_defeat");
                        break;
                }
            }
        }

        public static Color StatusColor(StatusType s)
        {
            switch (s)
            {
                case StatusType.Burn: return Theme.Hex("ff6a2a");
                case StatusType.Chill: case StatusType.Freeze: return Theme.Hex("7fd8ff");
                case StatusType.Shock: return Theme.Hex("ffe24a");
                case StatusType.Poison: return Theme.Hex("9be22d");
                case StatusType.Curse: case StatusType.Weaken: return Theme.Hex("9a6bff");
                case StatusType.Mark: return Theme.Hex("ffd76a");
                case StatusType.Bleed: return Theme.Hex("d03030");
                case StatusType.ArmorBreak: return Theme.Hex("b98a4e");
                default: return Color.white;
            }
        }
    }
}
