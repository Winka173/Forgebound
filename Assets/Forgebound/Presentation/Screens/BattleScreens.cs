using System.Collections.Generic;
using System.Text;
using Forgebound.Core.Combat;
using Forgebound.Core.Content;
using Forgebound.Core.Run;
using UnityEngine;
using UnityEngine.UI;

namespace Forgebound.Presentation
{
    /// <summary>
    /// Side-view auto battle. No combat input: only speed (1x/2x), pause and skip (skip resolves the same deterministic
    /// result instantly). HUD: hero HP/shield, weapon cooldowns, boss bar + phase, wave counter, live link/rune counters.
    /// </summary>
    public sealed class BattleScreen : UiScreen
    {
        private Battle _battle;
        private BattleView _view;
        private Image _heroHp, _heroShield, _bossHp;
        private Text _heroText, _bossText, _info;
        private readonly List<Image> _cooldowns = new List<Image>();
        private Button _speedButton, _pauseButton;
        private float _speed = 1;
        private bool _paused, _resolved;
        public override bool ShowsBattleWorld { get { return true; } }

        protected override void Build()
        {
            _battle = S.Run.CreateBattle(true);
            if (_battle == null) { Flow.Continue(S); return; }
            BattleWorld.Instance.ApplyBiome(S.Db.Biome(S.Run.Run.CurrentMap.Biome));
            _view = new BattleView(S, _battle);
            _speed = S.Profile.Settings.DefaultBattleSpeed;
            _view.Speed = _speed;
            var node = S.Run.CurrentNode;
            if (node != null && node.Type == NodeType.Boss) S.Audio.Music("mus_boss");

            // Hero bar (top-left)
            var heroBox = UiKit.Panel(Root, "Hero", new Color(0, 0, 0, 0.5f));
            UiKit.Anchor(heroBox.rectTransform, 0.01f, 0.88f, 0.35f, 0.99f);
            RectTransform bar;
            _heroHp = UiKit.Bar(heroBox.transform, Theme.Ok, new Color(0.2f, 0.05f, 0.05f), out bar);
            UiKit.Anchor(bar, 0.02f, 0.55f, 0.98f, 0.9f);
            RectTransform sbar;
            _heroShield = UiKit.Bar(heroBox.transform, Theme.Link, new Color(0, 0, 0, 0.3f), out sbar);
            UiKit.Anchor(sbar, 0.02f, 0.45f, 0.98f, 0.53f);
            _heroText = UiKit.Label(heroBox.transform, "", 22, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(_heroText.rectTransform, 0.03f, 0.55f, 0.97f, 0.9f);
            var cds = UiKit.HBox(heroBox.transform, "Cooldowns", 6);
            UiKit.Anchor((RectTransform)cds.transform, 0.02f, 0.05f, 0.98f, 0.42f);
            for (int i = 0; i < _battle.Weapons.Length; i++)
            {
                var spec = _battle.Weapons[i].Spec;
                var slot = UiKit.Panel(cds.transform, "W" + i, Theme.PanelLight);
                var icon = UiKit.Panel(slot.transform, "Icon", Color.white);
                icon.sprite = S.Sprites.Weapon(spec.Frame.Id, spec.Core != null ? spec.Core.Id : null, spec.Tier, spec.Legendary != null ? spec.Legendary.Id : null);
                icon.preserveAspect = true;
                UiKit.Stretch(icon.rectTransform, 4, 4, 4, 4);
                var cd = UiKit.Panel(slot.transform, "Cooldown", new Color(0, 0, 0, 0.55f));
                cd.type = Image.Type.Filled;
                cd.fillMethod = Image.FillMethod.Vertical;
                cd.sprite = SpriteLibrary.Primitive(SpriteLibrary.Shape.Square);
                UiKit.Stretch(cd.rectTransform);
                _cooldowns.Add(cd);
            }

            // Boss / encounter bar (top-centre)
            var bossBox = UiKit.Panel(Root, "Boss", new Color(0, 0, 0, 0.5f));
            UiKit.Anchor(bossBox.rectTransform, 0.38f, 0.92f, 0.85f, 0.99f);
            RectTransform bb;
            _bossHp = UiKit.Bar(bossBox.transform, Theme.Danger, new Color(0.1f, 0.03f, 0.03f), out bb);
            UiKit.Stretch(bb, 6, 6, 6, 6);
            _bossText = UiKit.Label(bossBox.transform, "", 22, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(_bossText.rectTransform);

            _info = UiKit.Label(Root, "", 20, Theme.Muted, TextAnchor.UpperRight);
            UiKit.Anchor(_info.rectTransform, 0.7f, 0.74f, 0.99f, 0.9f);

            // Controls (bottom-right): large touch targets.
            var ctl = UiKit.HBox(Root, "Controls", 10);
            UiKit.Anchor((RectTransform)ctl.transform, 0.62f, 0.02f, 0.99f, 0.11f);
            _pauseButton = UiKit.Button(ctl.transform, L("ui.battle.pause"), TogglePause);
            _speedButton = UiKit.Button(ctl.transform, _speed + "x", ToggleSpeed);
            UiKit.Button(ctl.transform, L("ui.battle.skip"), () => _view.SkipToEnd());
            Hints.Show(S, "battle_intro");
        }

        private void TogglePause()
        {
            _paused = !_paused;
            _view.Speed = _paused ? 0 : _speed;
            UiKit.SetLabel(_pauseButton, L(_paused ? "ui.battle.resume" : "ui.battle.pause"));
        }

        private void ToggleSpeed()
        {
            _speed = _speed >= 2 ? 1 : 2;
            if (!_paused) _view.Speed = _speed;
            UiKit.SetLabel(_speedButton, _speed + "x");
        }

        public override void Tick(float dt)
        {
            if (_view == null || _resolved) return;
            if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
            if (Input.GetKeyDown(KeyCode.Alpha1)) { _speed = 1; if (!_paused) _view.Speed = 1; UiKit.SetLabel(_speedButton, "1x"); }
            if (Input.GetKeyDown(KeyCode.Alpha2)) { _speed = 2; if (!_paused) _view.Speed = 2; UiKit.SetLabel(_speedButton, "2x"); }
            _view.Update(dt);
            var hero = _battle.Hero;
            UiKit.SetFill(_heroHp, hero.Hp / (float)hero.MaxHp);
            UiKit.SetFill(_heroShield, hero.Shield / (float)hero.MaxHp);
            _heroText.text = Mathf.Max(0, hero.Hp) + " / " + hero.MaxHp + (hero.Shield > 0 ? "  (+" + hero.Shield + ")" : "");
            for (int i = 0; i < _cooldowns.Count; i++)
            {
                var w = _battle.Weapons[i];
                float total = Mathf.Max(1, w.Spec.CooldownTicks * 1000f);
                _cooldowns[i].fillAmount = w.WindupLeft > 0 ? 0 : Mathf.Clamp01(w.Cooldown / total);
            }
            Unit boss = null;
            int alive = 0;
            long hpSum = 0, hpMax = 0;
            foreach (var u in _battle.Units)
            {
                if (u.Team != 1 || !u.Alive) continue;
                alive++;
                hpSum += u.Hp;
                hpMax += u.MaxHp;
                if (u.IsBoss || (boss == null && u.IsElite)) boss = u;
            }
            if (boss != null)
            {
                UiKit.SetFill(_bossHp, boss.Hp / (float)boss.MaxHp);
                _bossText.text = S.Loc.Name("enemy", boss.Def.Id) + (_view.LastPhaseName != null ? " — " + _view.LastPhaseName : "") + "  " + boss.Hp + "/" + boss.MaxHp;
            }
            else
            {
                UiKit.SetFill(_bossHp, hpMax > 0 ? hpSum / (float)hpMax : 0);
                _bossText.text = LF("ui.battle.enemies_left", alive);
            }
            int fires = 0;
            foreach (var kv in _battle.Report.LinkFires) fires += kv.Value;
            int procs = 0;
            foreach (var kv in _battle.Report.RuneTriggers) procs += kv.Value;
            _info.text = LF("ui.battle.live", (_battle.Tick / 30f).ToString("0.0"), procs, fires);

            if (_view.Finished && !_resolved)
            {
                _resolved = true;
                var result = _battle.Result;
                Flow.Report(S, S.Run.ResolveBattle(result));
                S.SaveRun();
                _view.Dispose();
                S.Ui.Show(() => new ReportScreen(result));
            }
        }

        public override void OnHide()
        {
            if (!_resolved && _view != null) _view.Dispose();
        }

        public override bool OnBack() { TogglePause(); return true; }
    }

    /// <summary>Post-battle report grounded in logged data: per-weapon damage, procs, statuses, mitigation, links, insights.</summary>
    public sealed class ReportScreen : UiScreen
    {
        private readonly BattleResult _r;
        public ReportScreen(BattleResult r) { _r = r; }

        protected override void Build()
        {
            UiKit.Stretch(UiKit.Panel(Root, "Bg", Theme.Bg).rectTransform);
            var rep = _r.Report;
            var title = UiKit.Label(Root, _r.Victory ? L("ui.report.victory") : L("ui.report.defeat"), 56, _r.Victory ? Theme.Gold : Theme.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, 0.2f, 0.86f, 0.8f, 0.98f);
            var sub = UiKit.Label(Root, LF("ui.report.summary", (_r.Ticks / 30f).ToString("0.0"), rep.EnemiesKilled, rep.TotalDamage, rep.DamageTaken), 24, Theme.Muted, TextAnchor.MiddleCenter);
            UiKit.Anchor(sub.rectTransform, 0.1f, 0.8f, 0.9f, 0.86f);

            // Weapons damage share
            var left = UiKit.VBox(Root, "Weapons", 10, 12);
            UiKit.Anchor((RectTransform)left.transform, 0.03f, 0.15f, 0.5f, 0.78f);
            UiKit.Label(left.transform, L("ui.report.weapons"), 28, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            long total = System.Math.Max(1, rep.TotalDamage);
            var weapons = BattleBuilder.ActiveWeapons(S.Run.Run);
            for (int i = 0; i < weapons.Count && i < 3; i++)
            {
                var ws = rep.Weapons[i];
                var row = UiKit.Rect(left.transform, "Row");
                UiKit.Size(row, -1, 70, -1, 70);
                RectTransform br;
                var fill = UiKit.Bar(row, Theme.Ember, Theme.Panel, out br);
                UiKit.Anchor(br, 0, 0.1f, 1, 0.9f);
                UiKit.SetFill(fill, ws.Damage / (float)total);
                var t = UiKit.Label(row, RunBoardHost.WeaponName(S, weapons[i]) + "  —  " + LF("ui.report.weapon_line", ws.Damage, ws.Damage * 100 / total, ws.Hits, ws.Crits, ws.Procs, ws.ProcDamage, ws.DotDamage, ws.Kills), 20, Theme.Text);
                UiKit.Stretch(t.rectTransform, 10, 0, 10, 0);
            }
            UiKit.Label(left.transform, LF("ui.report.defense", rep.DamageBlockedByShield, rep.DamageMitigatedByArmor, rep.Healing, rep.LeechHealing), 20, Theme.Muted);
            var statuses = new StringBuilder();
            for (int s = 1; s < (int)StatusType.Count; s++)
                if (rep.StatusUptimeTicks[s] > 0) statuses.Append(S.Tips.Status((StatusType)s)).Append(' ').Append((rep.StatusUptimeTicks[s] / 30f).ToString("0")).Append("s  ");
            if (statuses.Length > 0) UiKit.Label(left.transform, L("ui.report.status_uptime") + " " + statuses, 20, Theme.Muted);

            // Right: runes, links, insights
            var right = UiKit.VBox(Root, "Details", 8, 12);
            UiKit.Anchor((RectTransform)right.transform, 0.52f, 0.15f, 0.97f, 0.78f);
            UiKit.Label(right.transform, L("ui.report.procs"), 28, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            var runes = new StringBuilder();
            foreach (var kv in rep.RuneTriggers) runes.Append(RuneName(kv.Key)).Append(" ×").Append(kv.Value).Append("   ");
            UiKit.Label(right.transform, runes.Length > 0 ? runes.ToString() : L("ui.report.no_procs"), 20, Theme.Text);
            int fires = 0;
            foreach (var kv in rep.LinkFires) fires += kv.Value;
            UiKit.Label(right.transform, LF("ui.report.links", fires, rep.DeadLinks.Count), 20, Theme.Link);
            if (rep.ProcsSuppressedDepth + rep.ProcsSuppressedTickCap > 0)
                UiKit.Label(right.transform, LF("ui.report.suppressed", rep.ProcsSuppressedDepth + rep.ProcsSuppressedTickCap), 18, Theme.Muted);
            UiKit.Label(right.transform, L("ui.report.insights"), 28, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            foreach (var ins in rep.Insights) UiKit.Label(right.transform, "• " + S.Loc.Insight(ins), 20, Theme.Text);
            if (!_r.Victory && rep.KilledBy != null) UiKit.Label(right.transform, LF("ui.report.killed_by", S.Loc.Name("enemy", rep.KilledBy)), 22, Theme.Danger);

            var cont = UiKit.Button(Root, L("ui.continue"), () => Flow.Continue(S), Theme.Ember);
            UiKit.Anchor((RectTransform)cont.transform, 0.4f, 0.03f, 0.6f, 0.11f);
        }

        private string RuneName(string id)
        {
            if (S.Loc.Has("rune." + id + ".name")) return S.Loc.Name("rune", id);
            return L("ui.report.innate");
        }
    }

    /// <summary>Short, skippable legendary transformation (respects "skip forge animations").</summary>
    public sealed class EvolutionScreen : UiScreen
    {
        private readonly int _weaponUid;
        private readonly string _legendary;
        private float _t;
        private Image _icon, _flash;

        public EvolutionScreen(int weaponUid, string legendary) { _weaponUid = weaponUid; _legendary = legendary; }

        protected override void Build()
        {
            UiKit.Stretch(UiKit.Panel(Root, "Bg", new Color(0.05f, 0.03f, 0.02f, 0.97f)).rectTransform);
            var w = S.Run.Run.FindWeapon(_weaponUid);
            _icon = UiKit.Panel(Root, "Weapon", Color.white);
            _icon.sprite = S.Sprites.Weapon(w.FrameId, w.CoreId, w.Tier, w.LegendaryId);
            _icon.preserveAspect = true;
            UiKit.Anchor(_icon.rectTransform, 0.35f, 0.3f, 0.65f, 0.8f);
            _flash = UiKit.Panel(Root, "Flash", new Color(1, 0.85f, 0.5f, 0));
            UiKit.Stretch(_flash.rectTransform);
            _flash.raycastTarget = false;
            var name = UiKit.Label(Root, S.Loc.Name("legendary", _legendary), 54, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(name.rectTransform, 0.1f, 0.17f, 0.9f, 0.28f);
            var lore = UiKit.Label(Root, L("legendary." + _legendary + ".lore"), 24, Theme.Text, TextAnchor.MiddleCenter);
            UiKit.Anchor(lore.rectTransform, 0.15f, 0.1f, 0.85f, 0.17f);
            var b = UiKit.Button(Root, L("ui.continue"), Done, Theme.Ember);
            UiKit.Anchor((RectTransform)b.transform, 0.42f, 0.02f, 0.58f, 0.09f);
            S.Audio.Stinger("sting_legendary");
            if (S.Profile.Settings.SkipForgeAnimations) _t = 10;
            Hints.Show(S, "legendary_intro");
        }

        private void Done() { Flow.Continue(S); }

        public override void Tick(float dt)
        {
            _t += dt;
            float pulse = Mathf.Clamp01(1f - Mathf.Abs(_t - 0.8f) * 2f);
            _flash.color = new Color(1, 0.85f, 0.5f, S.Profile.Settings.ReducedFlashes ? pulse * 0.25f : pulse * 0.7f);
            _icon.rectTransform.localScale = Vector3.one * (0.8f + Mathf.Min(_t, 1.2f) * 0.2f);
            _icon.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(_t * 2f) * 4f);
            if (Input.anyKeyDown && _t > 0.3f) Done();
        }
    }
}
