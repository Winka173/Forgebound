using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Forgebound.Core.Content;

namespace Forgebound.Core.Localization
{
    /// <summary>
    /// String tables (Resources/Localization/en.json, vi.json: {"strings":{key:value}}). Lookup falls back
    /// language → English → key, so a missing translation is visible but never crashes. Placeholders: {0}, {1}...
    /// </summary>
    public sealed class Loc
    {
        public static readonly string[] Languages = { "en", "vi" };
        private readonly Dictionary<string, Dictionary<string, string>> _tables = new Dictionary<string, Dictionary<string, string>>();
        public string Language = "en";
        public readonly HashSet<string> MissingKeys = new HashSet<string>();

        public void AddTable(string lang, Dictionary<string, string> table) { _tables[lang] = table; }

        public static Dictionary<string, string> ParseTable(string json)
        {
            var d = new Dictionary<string, string>();
            JsonValue root;
            string err;
            if (!JsonParser.TryParse(json, out root, out err)) return d;
            var s = root["strings"];
            if (s.Kind != JsonKind.Object) return d;
            foreach (var kv in s.Fields) if (kv.Value.Kind == JsonKind.String) d[kv.Key] = kv.Value.StringValue;
            return d;
        }

        public bool Has(string key)
        {
            Dictionary<string, string> t;
            return _tables.TryGetValue(Language, out t) && t.ContainsKey(key);
        }

        public string Get(string key)
        {
            if (key == null) return "";
            Dictionary<string, string> t;
            string v;
            if (_tables.TryGetValue(Language, out t) && t.TryGetValue(key, out v)) return v;
            if (Language != "en" && _tables.TryGetValue("en", out t) && t.TryGetValue(key, out v)) { MissingKeys.Add(Language + ":" + key); return v; }
            MissingKeys.Add(Language + ":" + key);
            return key;
        }

        public string Format(string key, params object[] args)
        {
            var template = Get(key);
            if (args == null || args.Length == 0) return template;
            var sb = new StringBuilder(template.Length + 16);
            for (int i = 0; i < template.Length; i++)
            {
                char c = template[i];
                if (c == '{' && i + 2 < template.Length && char.IsDigit(template[i + 1]))
                {
                    int end = template.IndexOf('}', i);
                    int idx;
                    if (end > i && int.TryParse(template.Substring(i + 1, end - i - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) && idx < args.Length)
                    {
                        sb.Append(Convert.ToString(args[idx], CultureInfo.InvariantCulture));
                        i = end;
                        continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public string Name(string category, string id) { return Get(category + "." + id + ".name"); }

        /// <summary>Format "a|b|c" insight records produced by the battle report.</summary>
        public string Insight(string record)
        {
            var parts = record.Split('|');
            var args = new object[parts.Length - 1];
            for (int i = 1; i < parts.Length; i++)
            {
                string p = parts[i];
                // Resolve content ids into names where possible for readability.
                if (Has("frame." + p + ".name")) p = Get("frame." + p + ".name");
                else if (Has("rune." + p + ".name")) p = Get("rune." + p + ".name");
                else if (Has("enemy." + p + ".name")) p = Get("enemy." + p + ".name");
                args[i - 1] = p;
            }
            return Format(parts[0], args);
        }
    }

    /// <summary>
    /// Builds tooltip text strictly from content data (no hand-written mechanics claims): every line comes from a stat,
    /// op, trigger or effect actually present in the definition. Template keys live under "tip.*".
    /// </summary>
    public sealed class TooltipBuilder
    {
        private readonly Loc _loc;
        private readonly ContentDatabase _db;

        public TooltipBuilder(Loc loc, ContentDatabase db) { _loc = loc; _db = db; }

        private static string Pct(int permille) { return (permille / 10.0).ToString("0.#", CultureInfo.InvariantCulture) + "%"; }
        private static string Sec(int ticks) { return (ticks / 30.0).ToString("0.##", CultureInfo.InvariantCulture); }
        private static string Units(int milli) { return (milli / 1000.0).ToString("0.#", CultureInfo.InvariantCulture); }

        public string Status(StatusType s) { return _loc.Get("status." + s.ToString().ToLowerInvariant()); }
        public string DamageTypeName(DamageType d) { return _loc.Get("damage." + d.ToString().ToLowerInvariant()); }

        public List<string> Frame(FrameDef f)
        {
            var lines = new List<string>();
            lines.Add(_loc.Format("tip.frame.stats", f.BaseDamage, Sec(f.CooldownTicks), Units(f.RangeMilli), _loc.Get("stance." + f.Stance.ToString().ToLowerInvariant())));
            for (int i = 0; i < f.Steps.Count; i++) lines.Add(Step(f.Steps[i], i + 1));
            lines.Add(_loc.Format("tip.frame.slots", f.ModifierSlots, f.RuneSlots, f.Footprint.Count));
            return lines;
        }

        private string Step(AttackStep s, int index)
        {
            string kind = _loc.Get("step." + s.Kind.ToString().ToLowerInvariant());
            var sb = new StringBuilder(_loc.Format("tip.step", index, kind, Pct(s.DamagePermille)));
            if (s.Hits > 1) sb.Append(' ').Append(_loc.Format("tip.step.hits", s.Hits));
            if (s.MaxTargets > 1) sb.Append(' ').Append(_loc.Format("tip.step.targets", s.MaxTargets));
            if (s.Pierce > 0) sb.Append(' ').Append(_loc.Format("tip.step.pierce", s.Pierce));
            if (s.StaggerChance > 0) sb.Append(' ').Append(_loc.Format("tip.step.stagger", Pct(s.StaggerChance)));
            if (s.ArmorBreak > 0) sb.Append(' ').Append(_loc.Format("tip.step.armorbreak", s.ArmorBreak));
            if (s.ShieldPermille > 0) sb.Append(' ').Append(_loc.Format("tip.step.shield", Pct(s.ShieldPermille)));
            if (s.PullMilli > 0) sb.Append(' ').Append(_loc.Format("tip.step.pull", Units(s.PullMilli)));
            if (s.Status != null) sb.Append(' ').Append(StatusApplyText(s.Status));
            if (s.Kind == StepKind.Summon) sb.Append(' ').Append(_loc.Format("tip.step.summon", s.SummonCap));
            return sb.ToString();
        }

        public string StatusApplyText(StatusApply a)
        {
            return _loc.Format("tip.applies", Pct(a.ChancePermille), a.Stacks, Status(a.Status), Sec(a.DurationTicks));
        }

        public List<string> Core(CoreDef c)
        {
            var lines = new List<string>();
            if (c.DamageType >= 0) lines.Add(_loc.Format("tip.core.converts", DamageTypeName((DamageType)c.DamageType)));
            if (c.Status != null) lines.Add(StatusApplyText(c.Status));
            foreach (var m in c.Stats) lines.Add(Stat(m));
            foreach (var r in c.InnateRunes) lines.Add(Rune(r));
            return lines;
        }

        public string Stat(StatMod m)
        {
            string key = "stat." + m.Stat.ToString();
            string value = IsPermilleStat(m.Stat) ? Pct(m.Value) : m.Value.ToString(CultureInfo.InvariantCulture);
            string sign = m.Value >= 0 ? "+" : "";
            string text = _loc.Format("tip.stat", sign + value, _loc.Get(key));
            if (m.Tag != null) text += " " + _loc.Format("tip.stat.tag", TagName(m.Tag));
            return text;
        }

        private string TagName(string tag)
        {
            if (_loc.Has("tag." + tag)) return _loc.Get("tag." + tag);
            if (_loc.Has("damage." + tag.ToLowerInvariant())) return _loc.Get("damage." + tag.ToLowerInvariant());
            if (_loc.Has("status." + tag.ToLowerInvariant())) return _loc.Get("status." + tag.ToLowerInvariant());
            return tag;
        }

        public static bool IsPermilleStat(StatType s)
        {
            switch (s)
            {
                case StatType.MaxHp: case StatType.Armor: case StatType.Regen: case StatType.ShieldOnStart: case StatType.RelayCapacity:
                case StatType.RelayThreshold: case StatType.BoardWidth: case StatType.BoardHeight: case StatType.ShopOffers: case StatType.StartGold:
                case StatType.MaxWeapons: case StatType.ModifierSlots: case StatType.RuneSlots: case StatType.FirstStrike: case StatType.ChainCount:
                case StatType.ProjectileCount: case StatType.RewardChoices:
                    return false;
                default:
                    return true;
            }
        }

        public List<string> Modifier(ModifierDef m)
        {
            var lines = new List<string>();
            foreach (var op in m.Ops) lines.Add(Op(op));
            if (m.RequiresTags.Count > 0)
            {
                var names = new List<string>();
                foreach (var t in m.RequiresTags) names.Add(TagName(t));
                lines.Add(_loc.Format("tip.requires", string.Join(" / ", names)));
            }
            if (m.ConflictGroup != null) lines.Add(_loc.Format("tip.conflict", _loc.Get("conflict." + m.ConflictGroup)));
            lines.Add(_loc.Format("tip.stage", _loc.Get("stage." + m.Stage)));
            return lines;
        }

        public string Op(ModOpDef op)
        {
            string key = "op." + op.Op.ToString();
            int v = op.Value;
            object arg;
            switch (op.Op)
            {
                case ModOp.CooldownPermille: case ModOp.DamagePermille: case ModOp.SplitDamage: case ModOp.RangePermille: case ModOp.ProjectileSpeedPermille:
                case ModOp.StatusDurationPermille: case ModOp.WindupPermille: case ModOp.ProcPermille: case ModOp.RepeatPermille: case ModOp.ExplosionPermille:
                case ModOp.ChainPermille: case ModOp.ExecuteMult: case ModOp.OverchargePermille:
                    arg = Pct(v); break;   // absolute multiplier, e.g. 72%
                case ModOp.Knockback: case ModOp.Pull: case ModOp.ExplosionRadius: case ModOp.ArcAdd:
                    arg = Units(v); break;
                case ModOp.ProjectileAdd: case ModOp.PierceAdd: case ModOp.RicochetAdd: case ModOp.RepeatCount: case ModOp.ChainAdd: case ModOp.OverchargeEvery:
                case ModOp.ArmorBreakStacks: case ModOp.BarrageCount: case ModOp.LinkThresholdReduce: case ModOp.MaxTargetsAdd: case ModOp.ArmorPen:
                case ModOp.SummonCapAdd: case ModOp.Homing: case ModOp.Boomerang: case ModOp.TwinStrike: case ModOp.Scatter: case ModOp.RepeatDelay:
                    arg = v; break;
                default:
                    arg = Pct(v); break;  // additive permille bonuses (crit, chances, leech...)
            }
            if (op.Op == ModOp.BonusVsStatus) return _loc.Format(key, arg, TagName(op.Tag));
            if (op.Op == ModOp.ConvertDamage) return _loc.Format(key, TagName(op.Tag));
            return _loc.Format(key, arg);
        }

        public string Rune(RuneDef r)
        {
            var sb = new StringBuilder();
            string trig = _loc.Get("trigger." + r.Trigger.ToString());
            if (r.Trigger == TriggerType.OnTimer) trig = _loc.Format("trigger.OnTimer.every", Sec(r.TimerTicks));
            sb.Append(trig);
            var c = r.Condition;
            if (c.EveryN > 1) sb.Append(' ').Append(_loc.Format("cond.everyN", c.EveryN));
            if (c.StatusIs != StatusType.None) sb.Append(' ').Append(_loc.Format("cond.statusIs", Status(c.StatusIs)));
            if (c.TargetHasStatus != StatusType.None) sb.Append(' ').Append(_loc.Format("cond.targetHas", Status(c.TargetHasStatus)));
            if (c.TargetHpBelowPermille > 0) sb.Append(' ').Append(_loc.Format("cond.targetHpBelow", Pct(c.TargetHpBelowPermille)));
            if (c.HeroHpBelowPermille > 0) sb.Append(' ').Append(_loc.Format("cond.heroHpBelow", Pct(c.HeroHpBelowPermille)));
            if (c.TargetIsElite) sb.Append(' ').Append(_loc.Get("cond.elite"));
            if (c.OnlyPrimary) sb.Append(' ').Append(_loc.Get("cond.primary"));
            if (r.ChancePermille < 1000) sb.Append(' ').Append(_loc.Format("cond.chance", Pct(r.ChancePermille)));
            sb.Append(": ");
            for (int i = 0; i < r.Effects.Count; i++)
            {
                if (i > 0) sb.Append(_loc.Get("tip.and"));
                sb.Append(Effect(r.Effects[i]));
            }
            if (r.IcdTicks > 0) sb.Append(' ').Append(_loc.Format("tip.icd", Sec(r.IcdTicks)));
            if (r.MaxPerBattle > 0) sb.Append(' ').Append(_loc.Format("tip.maxPerBattle", r.MaxPerBattle));
            if (c.AllowFromProc) sb.Append(' ').Append(_loc.Get("tip.chains"));
            return sb.ToString();
        }

        public string Effect(EffectDef e)
        {
            string target = _loc.Get("target." + e.Target.ToString());
            string dt = e.DamageType >= 0 ? DamageTypeName((DamageType)e.DamageType) : _loc.Get("damage.weapon");
            string amount = e.FromMaxHp ? _loc.Format("tip.amount.maxhp", Pct(e.Permille)) : (e.Permille > 0 ? _loc.Format("tip.amount.weapon", Pct(e.Permille)) : "");
            if (e.Flat > 0) amount = amount.Length > 0 ? amount + " + " + e.Flat : e.Flat.ToString(CultureInfo.InvariantCulture);
            switch (e.Type)
            {
                case EffectType.ApplyStatus:
                case EffectType.Mark:
                    return _loc.Format("effect.ApplyStatus", e.Stacks, e.Type == EffectType.Mark ? Status(StatusType.Mark) : Status(e.Status), Sec(e.DurationTicks), target);
                case EffectType.Nova:
                    return _loc.Format("effect.Nova", amount, dt, Units(e.RadiusMilli)) + (e.Status != StatusType.None ? " " + _loc.Format("tip.applies", "100%", e.Stacks, Status(e.Status), Sec(e.DurationTicks)) : "");
                case EffectType.Chain: return _loc.Format("effect.Chain", amount, dt, e.Count);
                case EffectType.Damage: return _loc.Format("effect.Damage", amount, dt, target);
                case EffectType.Haste: return _loc.Format("effect.Haste", e.Stacks, Sec(e.DurationTicks));
                case EffectType.Fortify: return _loc.Format("effect.Fortify", Sec(e.DurationTicks));
                case EffectType.CooldownReduce: return e.Flat > 0 ? _loc.Format("effect.CooldownReduceFlat", Sec(e.Flat)) : _loc.Format("effect.CooldownReduce", Pct(e.Permille));
                case EffectType.Execute: return _loc.Format("effect.Execute", Pct(e.Permille > 0 ? e.Permille : 150));
                case EffectType.Pull:
                case EffectType.Knockback: return _loc.Format("effect." + e.Type, Units(e.RadiusMilli), target);
                case EffectType.Gold: return _loc.Format("effect.Gold", e.Flat);
                case EffectType.SpreadStatus: return _loc.Format("effect.SpreadStatus", Units(e.RadiusMilli));
                case EffectType.Detonate: return _loc.Format("effect.Detonate", Status(e.Status), amount);
                case EffectType.Projectile: return _loc.Format("effect.Projectile", e.Count, amount, target);
                case EffectType.ExtraAttack: return _loc.Format("effect.ExtraAttack", Pct(e.Permille > 0 ? e.Permille : 1000));
                default: return _loc.Format("effect." + e.Type, amount, target);
            }
        }

        public List<string> Piece(BoardPieceDef p)
        {
            var lines = new List<string>();
            if (p.Relay != null)
            {
                var r = p.Relay;
                lines.Add(_loc.Get("relay." + r.Type + ".desc"));
                if (r.Type == RelayType.Echo || r.Type == RelayType.Capacitor || r.Type == RelayType.Governor)
                    lines.Add(_loc.Format("tip.relay.charge", _loc.Get("trigger." + r.Listen), r.Threshold, Pct(r.Permille)));
                if (r.Type == RelayType.Amplifier || r.Type == RelayType.Splitter || r.Type == RelayType.Prism || r.Type == RelayType.Delay) lines.Add(_loc.Format("tip.relay.power", Pct(r.Permille)));
                if (r.DelayTicks > 0) lines.Add(_loc.Format("tip.relay.delay", Sec(r.DelayTicks)));
                if (r.ConvertDamageType >= 0) lines.Add(_loc.Format("tip.core.converts", DamageTypeName((DamageType)r.ConvertDamageType)));
                if (r.Status != null) lines.Add(StatusApplyText(r.Status));
                if (r.MaxPerBattle > 0) lines.Add(_loc.Format("tip.maxPerBattle", r.MaxPerBattle));
                lines.Add(_loc.Format("tip.relay.capacity", r.Capacity));
            }
            foreach (var m in p.AdjacentStats) lines.Add(_loc.Format("tip.support.adjacent", Stat(m), p.MaxAdjacent));
            lines.Add(_loc.Format("tip.piece.cells", p.Footprint.Count, p.Ports.Count));
            return lines;
        }

        public List<string> Legendary(LegendaryDef l)
        {
            var lines = new List<string>();
            var parts = new List<string> { _loc.Name("frame", l.Frame) };
            if (l.Core != null) parts.Add(_loc.Name("core", l.Core));
            foreach (var m in l.RequiresModifiers) parts.Add(_loc.Name("modifier", m));
            lines.Add(_loc.Format("tip.legendary.recipe", string.Join(" + ", parts), l.MinTier, l.EssenceCost));
            if (l.Signature != LegendarySignature.None) lines.Add(_loc.Format("signature." + l.Signature, l.SignatureValue > 0 ? (object)l.SignatureValue : ""));
            foreach (var op in l.Ops) lines.Add(Op(op));
            foreach (var r in l.InnateRunes) lines.Add(Rune(r));
            return lines;
        }

        public List<string> Blessing(BlessingDef b)
        {
            var lines = new List<string>();
            foreach (var s in b.Stats) lines.Add(Stat(s));
            foreach (var r in b.Runes) lines.Add(Rune(r));
            return lines;
        }

        public List<string> Forgemaster(ForgemasterDef f)
        {
            var lines = new List<string> { _loc.Format("tip.fm.base", f.MaxHp, f.Armor) };
            foreach (var s in f.Perks) lines.Add(Stat(s));
            foreach (var r in f.InnateRunes) lines.Add(Rune(r));
            return lines;
        }

        public List<string> Enemy(EnemyDef e)
        {
            var lines = new List<string> { _loc.Format("tip.enemy.base", e.MaxHp, e.Armor, e.AttackDamage, _loc.Get(e.AttackRanged ? "tip.enemy.ranged" : "tip.enemy.melee")) };
            foreach (var kv in e.Resist) lines.Add(_loc.Format("tip.enemy.resist", DamageTypeName(kv.Key), Pct(kv.Value)));
            if (e.Flying) lines.Add(_loc.Get("tip.enemy.flying"));
            foreach (var ab in e.Abilities) lines.Add(_loc.Get("ability." + ab.Type));
            for (int i = 0; i < e.Phases.Count; i++)
            {
                var names = new List<string>();
                foreach (var ab in e.Phases[i].Abilities) names.Add(_loc.Get("ability." + ab.Type));
                lines.Add(_loc.Format("tip.enemy.phase", i + 1, Pct(e.Phases[i].HpBelowPermille), string.Join(", ", names)));
            }
            return lines;
        }

        /// <summary>All template/enum keys the tooltip builder can request (used to validate localisation coverage).</summary>
        public static IEnumerable<string> TemplateKeys()
        {
            foreach (var k in new[] { "tip.frame.stats", "tip.step", "tip.step.hits", "tip.step.targets", "tip.step.pierce", "tip.step.stagger", "tip.step.armorbreak",
                "tip.step.shield", "tip.step.pull", "tip.step.summon", "tip.applies", "tip.frame.slots", "tip.core.converts", "tip.stat", "tip.stat.tag", "tip.requires",
                "tip.conflict", "tip.stage", "tip.and", "tip.icd", "tip.maxPerBattle", "tip.chains", "tip.amount.maxhp", "tip.amount.weapon", "tip.relay.charge",
                "tip.relay.power", "tip.relay.delay", "tip.relay.capacity", "tip.support.adjacent", "tip.piece.cells", "tip.legendary.recipe", "tip.fm.base",
                "tip.enemy.base", "tip.enemy.ranged", "tip.enemy.melee", "tip.enemy.resist", "tip.enemy.flying", "tip.enemy.phase", "damage.weapon",
                "cond.everyN", "cond.statusIs", "cond.targetHas", "cond.targetHpBelow", "cond.heroHpBelow", "cond.elite", "cond.primary", "cond.chance", "trigger.OnTimer.every" })
                yield return k;
            foreach (StatusType s in Enum.GetValues(typeof(StatusType))) if (s != StatusType.None && s != StatusType.Count) yield return "status." + s.ToString().ToLowerInvariant();
            foreach (DamageType d in Enum.GetValues(typeof(DamageType))) yield return "damage." + d.ToString().ToLowerInvariant();
            foreach (Stance s in Enum.GetValues(typeof(Stance))) yield return "stance." + s.ToString().ToLowerInvariant();
            foreach (StepKind s in Enum.GetValues(typeof(StepKind))) yield return "step." + s.ToString().ToLowerInvariant();
            foreach (TriggerType t in Enum.GetValues(typeof(TriggerType))) if (t != TriggerType.None) yield return "trigger." + t;
            foreach (EffectTarget t in Enum.GetValues(typeof(EffectTarget))) yield return "target." + t;
            foreach (EffectType t in Enum.GetValues(typeof(EffectType))) if (t != EffectType.None) yield return "effect." + t;
            foreach (ModOp o in Enum.GetValues(typeof(ModOp))) if (o != ModOp.None && o != ModOp.OrbitBlades) yield return "op." + o;
            foreach (StatType s in Enum.GetValues(typeof(StatType))) if (s != StatType.None) yield return "stat." + s;
            foreach (RelayType r in Enum.GetValues(typeof(RelayType))) if (r != RelayType.None) yield return "relay." + r + ".desc";
            foreach (LegendarySignature s in Enum.GetValues(typeof(LegendarySignature))) if (s != LegendarySignature.None) yield return "signature." + s;
            foreach (EnemyAbilityType a in Enum.GetValues(typeof(EnemyAbilityType))) if (a != EnemyAbilityType.None) yield return "ability." + a;
            for (int i = 0; i <= 5; i++) yield return "stage." + i;
            foreach (var g in new[] { "volley_count", "cadence" }) yield return "conflict." + g;
        }
    }
}
