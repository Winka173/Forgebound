using System.Collections.Generic;
using Forgebound.Core.Combat;
using Forgebound.Core.Content;
using UnityEngine;

namespace Forgebound.Presentation
{
    /// <summary>Visual for one unit: animated body sprite, shadow, HP/shield bar, status glyphs, hovering secondary weapons (hero).</summary>
    public sealed class UnitView
    {
        public readonly Transform Root;
        private readonly SpriteRenderer _body, _shadow, _hpBack, _hpFill, _shieldFill;
        private readonly TextMesh _status;
        private readonly List<Sprite> _idle, _attack;
        private readonly List<WeaponRig> _weapons = new List<WeaponRig>();
        private float _animT, _attackT = -1, _hitFlash, _flashT;
        private Color _flashColor;
        private readonly bool _isHero, _flying;
        private readonly float _height;
        private readonly Color _tint = Color.white;
        private float _windupGlow;

        private sealed class WeaponRig
        {
            public Transform T;
            public SpriteRenderer Sprite, Glow;
            public float Lunge, Pulse;
            public bool Primary;
        }

        public UnitView(Services s, Transform parent, Unit u, Battle battle)
        {
            Root = new GameObject("Unit" + u.Id).transform;
            Root.SetParent(parent, false);
            _isHero = u.IsHero;
            _flying = u.Flying;
            float scale = u.IsHero ? 1.25f : Mathf.Clamp(u.Radius / 450f, 0.6f, 2.4f);
            _height = 1.6f * scale;
            _shadow = Sprite(Root, SpriteLibrary.Primitive(SpriteLibrary.Shape.Circle), new Color(0, 0, 0, 0.35f), 0);
            _shadow.transform.localScale = new Vector3(1.6f * scale, 0.35f * scale, 1);
            _body = Sprite(Root, null, Color.white, 10);
            _body.transform.localScale = Vector3.one * scale;
            if (u.IsHero)
            {
                string fm = battle.Setup.Hero.ForgemasterId;
                _idle = s.Sprites.CharacterClip(fm, "idle");
                _attack = s.Sprites.CharacterClip(fm, "attack");
                // Secondary weapons hover as magical echoes behind the Forgemaster (no conflicting humanoid rigs).
                for (int i = 0; i < battle.Weapons.Length; i++)
                {
                    var spec = battle.Weapons[i].Spec;
                    var rig = new WeaponRig { Primary = i == 0 };
                    rig.T = new GameObject("Weapon" + i).transform;
                    rig.T.SetParent(Root, false);
                    rig.Sprite = Sprite(rig.T, s.Sprites.Weapon(spec.Frame.Id, spec.Core != null ? spec.Core.Id : null, spec.Tier, spec.Legendary != null ? spec.Legendary.Id : null, true), Color.white, i == 0 ? 12 : 8);
                    if (spec.Legendary != null)
                    {
                        var glow = s.Sprites.LegendaryGlow(spec.Frame.Id, spec.Core != null ? spec.Core.Id : null);
                        if (glow != null) rig.Glow = Sprite(rig.T, glow, new Color(1, 1, 1, 0.7f), 7);
                    }
                    rig.T.localScale = Vector3.one * (i == 0 ? 0.9f : 0.65f);
                    _weapons.Add(rig);
                }
            }
            else
            {
                _idle = u.Def != null ? s.Sprites.EnemyClip(u.Def, "idle") : new List<Sprite> { SpriteLibrary.Primitive(SpriteLibrary.Shape.Capsule) };
                _attack = _idle;
                if (u.IsFamiliar) _tint = new Color(0.55f, 0.75f, 1f, 0.85f);
                else if (u.Def != null && u.Def.Rank == EnemyRank.Boss) _tint = Color.white;
                _body.flipX = true;   // enemies face left toward the hero
            }
            _hpBack = Sprite(Root, SpriteLibrary.Primitive(SpriteLibrary.Shape.Square), new Color(0, 0, 0, 0.7f), 30);
            _hpFill = Sprite(Root, SpriteLibrary.Primitive(SpriteLibrary.Shape.Square), u.Team == 0 ? Theme.Ok : Theme.Danger, 31);
            _shieldFill = Sprite(Root, SpriteLibrary.Primitive(SpriteLibrary.Shape.Square), Theme.Link, 32);
            var barY = _height + 0.25f;
            _hpBack.transform.localPosition = new Vector3(0, barY, 0);
            _hpBack.transform.localScale = new Vector3(1.4f / 0.64f, 0.12f / 0.64f, 1);
            var stGo = new GameObject("Status");
            stGo.transform.SetParent(Root, false);
            stGo.transform.localPosition = new Vector3(0, barY + 0.32f, 0);
            _status = stGo.AddComponent<TextMesh>();
            _status.font = UiKit.Font;
            _status.GetComponent<MeshRenderer>().material = UiKit.Font.material;
            _status.GetComponent<MeshRenderer>().sortingOrder = 33;
            _status.characterSize = 0.06f;
            _status.fontSize = 48;
            _status.anchor = TextAnchor.MiddleCenter;
            _status.color = Color.white;
        }

        private static SpriteRenderer Sprite(Transform parent, Sprite sprite, Color c, int order)
        {
            var go = new GameObject("Sprite");
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = c;
            sr.sortingOrder = order;
            return sr;
        }

        public void Windup(int weapon, int ticks)
        {
            _windupGlow = Mathf.Clamp(ticks / 30f, 0.15f, 1.2f);
        }

        public void Attack(int weapon, bool proc)
        {
            if (weapon <= 0 || !_isHero) _attackT = 0;
            if (_isHero && weapon >= 0 && weapon < _weapons.Count) _weapons[weapon].Lunge = proc ? 0.6f : 1f;
        }

        public void LinkPulse(int weapon)
        {
            if (weapon >= 0 && weapon < _weapons.Count) _weapons[weapon].Pulse = 1f;
        }

        public void Hit(bool crit) { _hitFlash = crit ? 0.18f : 0.1f; }

        public void Flash(Color c)
        {
            _flashColor = c;
            _flashT = 0.25f;
        }

        public void Sync(Unit u, float x, float dt)
        {
            float y = _flying ? BattleWorld.AirY + Mathf.Sin(Time.time * 3f + u.Id) * 0.15f : BattleWorld.GroundY;
            if (u.Flying && u.State == UnitState.Windup) y = Mathf.Lerp(y, BattleWorld.GroundY + 0.6f, 0.7f);   // dive to attack
            if (u.Burrowed) y = BattleWorld.GroundY - 0.9f;
            Root.position = new Vector3(x, y, 0);
            _shadow.transform.position = new Vector3(x, BattleWorld.GroundY - 0.05f, 0);

            // Animation: attack clip plays once on release, else idle loop.
            _animT += dt;
            List<Sprite> clip = _idle;
            int frame;
            if (_attackT >= 0 && _attack != null && _attack.Count > 1)
            {
                _attackT += dt;
                frame = Mathf.FloorToInt(_attackT * 24f);
                if (frame >= _attack.Count) { _attackT = -1; frame = Mathf.FloorToInt(_animT * 10f) % Mathf.Max(1, _idle.Count); }
                else clip = _attack;
            }
            else frame = Mathf.FloorToInt(_animT * 10f) % Mathf.Max(1, _idle.Count);
            if (clip != null && clip.Count > 0) _body.sprite = clip[Mathf.Clamp(frame, 0, clip.Count - 1)];
            _body.transform.localPosition = new Vector3(0, _height * 0.5f, 0);

            // Hit / status / windup colour feedback (reduced-flash setting dims these in VfxPool, body flash stays subtle).
            var c = _tint;
            if (u.State == UnitState.Dead) c.a = Mathf.Max(0, c.a - 0.6f);
            if (_hitFlash > 0) { c = Color.Lerp(c, Color.white, 0.7f); _hitFlash -= dt; }
            if (_flashT > 0) { c = Color.Lerp(c, _flashColor, _flashT * 2f); _flashT -= dt; }
            if (u.State == UnitState.Ability || u.State == UnitState.Windup && !_isHero)
                c = Color.Lerp(c, Theme.Danger, 0.25f + 0.25f * Mathf.Sin(Time.time * 20f));
            if (u.Stealthed) c.a *= 0.35f;
            _body.color = c;

            // HP + shield bar
            float hp = u.MaxHp > 0 ? Mathf.Clamp01(u.Hp / (float)u.MaxHp) : 0;
            float sh = u.MaxHp > 0 ? Mathf.Clamp01((u.Shield + u.BarrierHp) / (float)u.MaxHp) : 0;
            float barY = _height + 0.25f;
            _hpFill.transform.localScale = new Vector3(1.4f * hp / 0.64f, 0.1f / 0.64f, 1);
            _hpFill.transform.localPosition = new Vector3(-0.7f + 0.7f * hp, barY, 0);
            _shieldFill.transform.localScale = new Vector3(1.4f * sh / 0.64f, 0.05f / 0.64f, 1);
            _shieldFill.transform.localPosition = new Vector3(-0.7f + 0.7f * sh, barY + 0.08f, 0);
            bool showBar = u.State != UnitState.Dead && (u.Hp < u.MaxHp || u.IsHero || u.Shield > 0 || u.IsBoss || u.IsElite);
            _hpBack.enabled = _hpFill.enabled = showBar;
            _shieldFill.enabled = showBar && sh > 0;

            // Status glyphs (letters + colour, readable without colour vision)
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i < (int)StatusType.Count; i++)
            {
                if (u.Status.Stacks[i] <= 0) continue;
                var st = (StatusType)i;
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(BattleView.StatusColor(st))).Append('>').Append(Theme.StatusGlyph(st));
                if (u.Status.Stacks[i] > 1) sb.Append(u.Status.Stacks[i]);
                sb.Append("</color> ");
            }
            _status.richText = true;
            _status.text = sb.ToString();

            // Weapon rigs: primary in hand, secondaries hover behind with bob; lunge toward enemies on release.
            for (int i = 0; i < _weapons.Count; i++)
            {
                var r = _weapons[i];
                Vector3 basePos = r.Primary ? new Vector3(0.55f, _height * 0.55f, 0) : new Vector3(-0.5f - 0.35f * i, _height * 0.9f + 0.35f * i + Mathf.Sin(Time.time * 2f + i) * 0.12f, 0);
                r.T.localPosition = basePos + new Vector3(r.Lunge * 0.9f, 0, 0);
                r.T.localRotation = Quaternion.Euler(0, 0, r.Primary ? -35f + r.Lunge * -60f : -20f + r.Lunge * -40f);
                r.Lunge = Mathf.MoveTowards(r.Lunge, 0, dt * 5f);
                var tint = Color.Lerp(Color.white, Theme.Link, r.Pulse);
                r.Sprite.color = tint;
                r.Pulse = Mathf.MoveTowards(r.Pulse, 0, dt * 3f);
                if (r.Glow != null) r.Glow.color = new Color(1, 1, 1, 0.45f + 0.25f * Mathf.Sin(Time.time * 4f));
            }
            _windupGlow = Mathf.MoveTowards(_windupGlow, 0, dt);
        }

        public void Destroy() { if (Root != null) Object.Destroy(Root.gameObject); }
    }

    public sealed class ProjectileView
    {
        private readonly Transform _t;
        private readonly SpriteRenderer _sr;
        private readonly bool _isWave;

        public ProjectileView(Transform parent, Projectile p, Battle battle)
        {
            _t = new GameObject("Proj" + p.Id).transform;
            _t.SetParent(parent, false);
            _sr = _t.gameObject.AddComponent<SpriteRenderer>();
            _isWave = p.Wave;
            bool arrow = false;
            if (!p.IsEnemy && p.Weapon >= 0 && p.Weapon < battle.Weapons.Length)
            {
                var tags = battle.Weapons[p.Weapon].Spec.Frame.Tags;
                arrow = tags.Contains("projectile") && !tags.Contains("spell");
            }
            _sr.sprite = SpriteLibrary.Primitive(_isWave ? SpriteLibrary.Shape.Capsule : (arrow ? SpriteLibrary.Shape.Spark : SpriteLibrary.Shape.Circle));
            var c = p.IsEnemy ? Theme.Danger : Theme.DamageType(p.Type);
            if (p.IsProc) c.a = 0.75f;
            _sr.color = c;
            _sr.sortingOrder = 20;
            _t.localScale = _isWave ? new Vector3(0.5f, 1.4f, 1) : (arrow ? new Vector3(0.9f, 0.25f, 1) : Vector3.one * (p.IsProc ? 0.3f : 0.42f));
        }

        public void Sync(Projectile p, float x, Battle b)
        {
            float y = _isWave ? BattleWorld.GroundY + 0.3f : BattleWorld.GroundY + 1.1f;
            var target = b.FindUnit(p.TargetId);
            if (!_isWave && target != null && target.Flying) y = BattleWorld.AirY + 0.6f;
            _t.position = new Vector3(x, y, 0);
            _sr.flipX = p.Dir < 0;
        }

        public void Destroy() { Object.Destroy(_t.gameObject); }
    }

    public sealed class ZoneView
    {
        private readonly Transform _t;
        private readonly SpriteRenderer _ring, _fill;
        private readonly int _total;

        public ZoneView(Transform parent, Zone z)
        {
            _t = new GameObject("Zone" + z.Id).transform;
            _t.SetParent(parent, false);
            _ring = new GameObject("Ring").AddComponent<SpriteRenderer>();
            _ring.transform.SetParent(_t, false);
            _ring.sprite = SpriteLibrary.Primitive(SpriteLibrary.Shape.Ring);
            _fill = new GameObject("Fill").AddComponent<SpriteRenderer>();
            _fill.transform.SetParent(_t, false);
            _fill.sprite = SpriteLibrary.Primitive(SpriteLibrary.Shape.Circle);
            bool enemy = z.Kind == ZoneKind.EnemySlam;
            var c = enemy ? Theme.Danger : Theme.DamageType(z.Type);
            _ring.color = new Color(c.r, c.g, c.b, 0.9f);
            _fill.color = new Color(c.r, c.g, c.b, 0.25f);
            _ring.sortingOrder = 5;
            _fill.sortingOrder = 4;
            _total = Mathf.Max(1, z.TicksLeft);
            float d = z.Radius / 1000f * 2f;
            _t.position = new Vector3(BattleWorld.WorldX(z.X), BattleWorld.GroundY - 0.1f, 0);
            _t.localScale = new Vector3(d, d * 0.3f, 1);
        }

        public void Sync(Zone z)
        {
            // Telegraph fill grows until impact so players can read the timing.
            float t = 1f - z.TicksLeft / (float)_total;
            _fill.transform.localScale = Vector3.one * Mathf.Clamp01(z.Kind == ZoneKind.EnemySlam || z.Kind == ZoneKind.Delayed ? t : 1f);
        }

        public void Destroy() { Object.Destroy(_t.gameObject); }
    }

    /// <summary>
    /// Pooled transient VFX (slashes, bursts, rings, beams, floating numbers, glyphs). The pool size is the particle budget
    /// (Settings.ParticleBudgetPercent); when exhausted, the oldest effect is recycled instead of allocating.
    /// </summary>
    public sealed class VfxPool
    {
        private sealed class Fx
        {
            public Transform T;
            public SpriteRenderer Sr;
            public TextMesh Text;
            public float Life, Max;
            public Vector3 Velocity;
            public float Grow;
            public bool Active;
        }

        private readonly List<Fx> _pool = new List<Fx>();
        private readonly Transform _root;
        private readonly bool _reducedFlashes;
        private int _next;
        private readonly TextMesh _banner;
        private float _bannerT;

        public VfxPool(Transform parent, int budget, bool reducedFlashes)
        {
            _root = new GameObject("VFX").transform;
            _root.SetParent(parent, false);
            _reducedFlashes = reducedFlashes;
            for (int i = 0; i < budget; i++)
            {
                var go = new GameObject("fx");
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 40;
                go.SetActive(false);
                _pool.Add(new Fx { T = go.transform, Sr = sr });
            }
            var bgo = new GameObject("Banner");
            bgo.transform.SetParent(_root, false);
            bgo.transform.position = new Vector3(0, 3.2f, 0);
            _banner = bgo.AddComponent<TextMesh>();
            _banner.font = UiKit.Font;
            bgo.GetComponent<MeshRenderer>().material = UiKit.Font.material;
            bgo.GetComponent<MeshRenderer>().sortingOrder = 60;
            _banner.anchor = TextAnchor.MiddleCenter;
            _banner.characterSize = 0.1f;
            _banner.fontSize = 64;
            _banner.color = Theme.Gold;
        }

        private Fx Take()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                var f = _pool[(_next + i) % _pool.Count];
                if (!f.Active) { _next = (_next + i + 1) % _pool.Count; return Activate(f); }
            }
            var oldest = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            return Activate(oldest);
        }

        private Fx Activate(Fx f)
        {
            f.Active = true;
            f.T.gameObject.SetActive(true);
            f.Sr.enabled = true;
            if (f.Text != null) f.Text.text = "";
            f.Velocity = Vector3.zero;
            f.Grow = 0;
            f.T.localRotation = Quaternion.identity;
            return f;
        }

        private TextMesh TextOf(Fx f)
        {
            if (f.Text == null)
            {
                var go = new GameObject("t");
                go.transform.SetParent(f.T, false);
                f.Text = go.AddComponent<TextMesh>();
                f.Text.font = UiKit.Font;
                go.GetComponent<MeshRenderer>().material = UiKit.Font.material;
                go.GetComponent<MeshRenderer>().sortingOrder = 50;
                f.Text.anchor = TextAnchor.MiddleCenter;
                f.Text.fontSize = 64;
            }
            return f.Text;
        }

        public void Number(Vector3 pos, int value, Color c, bool crit, bool proc, string prefix = "")
        {
            var f = Take();
            f.Sr.enabled = false;
            var t = TextOf(f);
            t.text = prefix + value;
            t.color = crit ? Theme.Gold : c;
            t.characterSize = crit ? 0.085f : (proc ? 0.045f : 0.06f);
            t.fontStyle = crit ? FontStyle.Bold : (proc ? FontStyle.Italic : FontStyle.Normal);
            f.T.position = pos + new Vector3(Random.Range(-0.3f, 0.3f), 0, 0);
            f.T.localScale = Vector3.one;
            f.Velocity = new Vector3(0, 1.4f, 0);
            f.Life = f.Max = crit ? 0.9f : 0.7f;
        }

        public void Text(Vector3 pos, string text, Color c, float seconds)
        {
            var f = Take();
            f.Sr.enabled = false;
            var t = TextOf(f);
            t.text = text;
            t.color = c;
            t.characterSize = 0.06f;
            t.fontStyle = FontStyle.Bold;
            f.T.position = pos;
            f.T.localScale = Vector3.one;
            f.Velocity = new Vector3(0, 0.6f, 0);
            f.Life = f.Max = Mathf.Max(0.4f, seconds);
        }

        public void Slash(Vector3 pos, Color c, bool heavy)
        {
            var f = Take();
            f.Sr.sprite = SpriteLibrary.Primitive(SpriteLibrary.Shape.Capsule);
            f.Sr.color = new Color(c.r, c.g, c.b, _reducedFlashes ? 0.45f : 0.85f);
            f.T.position = pos;
            f.T.localRotation = Quaternion.Euler(0, 0, Random.Range(-40f, 40f));
            f.T.localScale = heavy ? new Vector3(2.2f, 0.35f, 1) : new Vector3(1.5f, 0.2f, 1);
            f.Life = f.Max = heavy ? 0.22f : 0.14f;
        }

        public void Burst(Vector3 pos, Color c, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var f = Take();
                f.Sr.sprite = SpriteLibrary.Primitive(SpriteLibrary.Shape.Spark);
                f.Sr.color = c;
                f.T.position = pos;
                f.T.localScale = Vector3.one * Random.Range(0.15f, 0.3f);
                float a = Random.Range(0f, Mathf.PI * 2f);
                f.Velocity = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.7f + 0.4f, 0) * Random.Range(1.5f, 3.5f);
                f.Life = f.Max = Random.Range(0.25f, 0.5f);
            }
        }

        public void Ring(Vector3 pos, Color c, float size, float seconds)
        {
            var f = Take();
            f.Sr.sprite = SpriteLibrary.Primitive(SpriteLibrary.Shape.Ring);
            f.Sr.color = new Color(c.r, c.g, c.b, _reducedFlashes ? 0.5f : 0.9f);
            f.T.position = pos;
            f.T.localScale = Vector3.one * size * 0.3f;
            f.Grow = size * 1.6f;
            f.Life = f.Max = seconds;
        }

        public void Glyph(Vector3 pos, Color c)
        {
            var f = Take();
            f.Sr.sprite = SpriteLibrary.Primitive(SpriteLibrary.Shape.Diamond);
            f.Sr.color = c;
            f.T.position = pos;
            f.T.localScale = Vector3.one * 0.35f;
            f.Velocity = new Vector3(0, 0.8f, 0);
            f.Life = f.Max = 0.5f;
        }

        public void Beam(Vector3 a, Vector3 b, Color c)
        {
            var f = Take();
            f.Sr.sprite = SpriteLibrary.Primitive(SpriteLibrary.Shape.Square);
            f.Sr.color = new Color(c.r, c.g, c.b, 0.8f);
            var d = b - a;
            f.T.position = (a + b) * 0.5f;
            f.T.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            f.T.localScale = new Vector3(d.magnitude / 0.64f, 0.08f / 0.64f, 1);
            f.Life = f.Max = 0.2f;
        }

        /// <summary>Enemy telegraph: pulsing danger ring at the impact point for the wind-up duration.</summary>
        public void Warning(Vector3 pos, float radius, float seconds)
        {
            var f = Take();
            f.Sr.sprite = SpriteLibrary.Primitive(SpriteLibrary.Shape.Ring);
            f.Sr.color = Theme.Danger;
            f.T.position = pos;
            f.T.localScale = new Vector3(radius * 2f, radius * 0.6f, 1);
            f.Life = f.Max = Mathf.Max(0.3f, seconds);
        }

        public void Banner(string text)
        {
            _banner.text = text;
            _bannerT = 1.6f;
        }

        public void Update(float dt)
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                var f = _pool[i];
                if (!f.Active) continue;
                f.Life -= dt;
                if (f.Life <= 0) { f.Active = false; f.T.gameObject.SetActive(false); continue; }
                f.T.position += f.Velocity * dt;
                if (f.Grow > 0) f.T.localScale += Vector3.one * f.Grow * dt;
                float a = Mathf.Clamp01(f.Life / f.Max * 1.5f);
                var c = f.Sr.color; c.a = Mathf.Min(c.a, a); f.Sr.color = c;
                if (f.Text != null) { var tc = f.Text.color; tc.a = a; f.Text.color = tc; }
            }
            if (_bannerT > 0)
            {
                _bannerT -= dt;
                var c = _banner.color; c.a = Mathf.Clamp01(_bannerT); _banner.color = c;
                if (_bannerT <= 0) _banner.text = "";
            }
        }
    }
}
