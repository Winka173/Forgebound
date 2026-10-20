using System;
using System.Collections.Generic;
using Forgebound.Core.Content;

namespace Forgebound.Core.Weapons
{
    /// <summary>
    /// Immutable battle config for one weapon, compiled from content + run state at battle start.
    /// Modifier precedence (documented in docs/COMBAT_RULES.md): Spawn → Split → Target → Pierce → Impact → Timing.
    /// Because ops are compiled into independent fields and then consumed by the attack pipeline in that fixed
    /// order, the result never depends on the order modifiers were socketed.
    /// </summary>
    public sealed class WeaponSpec
    {
        public int Uid;
        public bool IsPrimary;
        public FrameDef Frame;
        public CoreDef Core;
        public LegendaryDef Legendary;
        public List<AttackStep> Steps;
        public readonly List<string> Tags = new List<string>();
        public DamageType DamageType;
        public int BaseDamage;
        public int CooldownTicks;
        public int WindupPermille = 1000;
        public int RangeMilli;
        public int MinRangeMilli;
        public Stance Stance;
        public int CritChance;
        public int CritDamage;
        public int ProjectileSpeed;
        public int ArmorPen;
        public StatusApply Status;
        public int StatusChanceAdd;
        public int StatusDurationPermille = 1000;
        public int ProcPermille = 1000;

        // Spawn stage
        public int RepeatCount, RepeatPermille = 500, RepeatDelayTicks = 12;
        public bool TwinStrike;
        public int BarrageCount;
        public int OrbitBlades;
        public int SummonCapAdd;
        // Split stage
        public int ProjectileAdd;
        public int SplitDamagePermille = 1000;
        public bool Scatter;
        public int MaxTargetsAdd;
        public int ArcAddMilli;
        // Target stage
        public bool Homing;
        public int FocusBonusPermille;
        // Pierce stage
        public int Pierce;
        public int Ricochet;
        public int ChainCount;
        public int ChainPermille = 500;
        public bool Boomerang;
        // Impact stage
        public int ExplosionRadius;
        public int ExplosionPermille = 500;
        public int DelayedDetonatePermille;
        public int ShockwavePermille;
        public int ShatterBonusPermille;
        public int KnockbackMilli;
        public int PullMilli;
        public int BleedChance;
        public int StaggerChance;
        public int VolatileOnKillPermille;
        public int LingeringPermille;
        public int ArmorBreakStacks;
        public StatusType BonusVsStatus;
        public int BonusVsStatusPermille;
        public int ExecuteBelowPermille;
        public int ExecuteMultPermille = 2000;
        // Timing / sustain stage
        public int OverchargeEvery, OverchargePermille = 3000;
        public int LeechPermille;
        public int ShieldOnDamagePermille;
        public int TempoPerAttack, TempoMax;
        public int AmbushPermille;
        public int FrenzyAttackSpeed;
        public int LinkThresholdReduce;
        public int DamagePermille = 1000;

        public readonly List<RuneDef> Runes = new List<RuneDef>();
        public LegendarySignature Signature;
        public int SignatureValue;

        public bool IsProjectileFrame
        {
            get { foreach (var s in Steps) if (s.Kind == StepKind.Projectile) return true; return false; }
        }
    }

    public static class WeaponCompiler
    {
        public static int TierDamagePermille(int tier)
        {
            switch (tier) { case 2: return 1300; case 3: return 1700; default: return 1000; }
        }

        public static WeaponSpec Compile(ContentDatabase db, WeaponInstance w, StatBlock heroStats, StatBlock supportStats, bool isPrimary)
        {
            var frame = db.Frame(w.FrameId);
            if (frame == null) throw new InvalidOperationException("Unknown frame " + w.FrameId);
            var core = w.CoreId != null ? db.Core(w.CoreId) : null;
            var legendary = w.LegendaryId != null ? db.Legendary(w.LegendaryId) : null;

            var s = new WeaponSpec
            {
                Uid = w.Uid,
                IsPrimary = isPrimary,
                Frame = frame,
                Core = core,
                Legendary = legendary,
                Steps = new List<AttackStep>(frame.Steps),
                DamageType = frame.DamageType,
                BaseDamage = frame.BaseDamage,
                CooldownTicks = frame.CooldownTicks,
                RangeMilli = frame.RangeMilli,
                MinRangeMilli = frame.MinRangeMilli,
                Stance = frame.Stance,
                CritChance = frame.CritChance,
                CritDamage = frame.CritDamage,
                ProjectileSpeed = frame.ProjectileSpeed
            };
            s.Tags.AddRange(AssemblyRules.WeaponTags(db, w));
            if (legendary != null) foreach (var t in legendary.Tags) if (!s.Tags.Contains(t)) s.Tags.Add(t);

            if (core != null)
            {
                if (core.DamageType >= 0) s.DamageType = (DamageType)core.DamageType;
                s.Status = core.Status;
                s.Runes.AddRange(core.InnateRunes);
            }

            // Collect ops: modifiers sorted by stage (precedence), then legendary ops last.
            var ops = new List<KeyValuePair<int, ModOpDef>>();
            var mods = new List<ModifierDef>();
            foreach (var mid in w.Modifiers) { var m = db.Modifier(mid); if (m != null) mods.Add(m); }
            mods.Sort((a, b) => a.Stage != b.Stage ? a.Stage.CompareTo(b.Stage) : string.CompareOrdinal(a.Id, b.Id));
            foreach (var m in mods) foreach (var op in m.Ops) ops.Add(new KeyValuePair<int, ModOpDef>(m.Stage, op));
            if (legendary != null)
            {
                foreach (var op in legendary.Ops) ops.Add(new KeyValuePair<int, ModOpDef>(9, op));
                s.Runes.AddRange(legendary.InnateRunes);
                s.Signature = legendary.Signature;
                s.SignatureValue = legendary.SignatureValue;
            }
            foreach (var kv in ops) ApplyOp(s, kv.Value);

            foreach (var rid in w.Runes) { var r = db.Rune(rid); if (r != null) s.Runes.Add(r); }

            // Tier quality multiplies base damage (run-scoped only).
            s.BaseDamage = FMathI.Permille(s.BaseDamage, TierDamagePermille(w.Tier));

            // Stats from the core itself, hero (perks/blessings/curses) and adjacent support pieces.
            if (core != null && core.Stats.Count > 0)
            {
                var coreStats = new StatBlock();
                coreStats.AddRange(core.Stats);
                ApplyStats(s, coreStats);
            }
            ApplyStats(s, heroStats);
            ApplyStats(s, supportStats);

            s.BaseDamage = Math.Max(1, FMathI.Permille(FMathI.Permille(s.BaseDamage, s.DamagePermille), db.Tuning.HeroDamagePermille));
            s.CritDamage = Math.Min(s.CritDamage, db.Tuning.CritDamageCapPermille);
            s.ProcPermille = FMathI.Permille(s.ProcPermille, db.Tuning.ProcGlobalPermille);
            int projectiles = 1 + s.ProjectileAdd;
            if (projectiles > 3) s.SplitDamagePermille = FMathI.Permille(s.SplitDamagePermille, db.Tuning.MultiProjectileFalloffPermille);
            s.CooldownTicks = Math.Max(4, s.CooldownTicks);
            s.CritChance = Clamp(s.CritChance, 0, 1000);
            s.RangeMilli = Math.Max(600, s.RangeMilli);
            return s;
        }

        private static int Clamp(int v, int a, int b) { return v < a ? a : (v > b ? b : v); }

        private static void ApplyStats(WeaponSpec s, StatBlock stats)
        {
            if (stats == null) return;
            var tags = s.Tags;
            string dt = s.DamageType.ToString();
            int dmg = stats.Sum(StatType.Damage, tags, dt) + stats.Sum(StatType.DamageTypeBonus, null, dt);
            bool melee = s.Stance == Stance.Melee;
            dmg += melee ? stats.Sum(StatType.MeleeDamage, tags) : stats.Sum(StatType.RangedDamage, tags);
            s.DamagePermille = FMathI.Permille(s.DamagePermille, 1000 + dmg);
            int atkSpeed = stats.Sum(StatType.AttackSpeed, tags, dt);
            if (atkSpeed != 0) s.CooldownTicks = Math.Max(4, s.CooldownTicks * 1000 / Math.Max(200, 1000 + atkSpeed));
            s.CritChance += stats.Sum(StatType.CritChance, tags, dt);
            s.CritDamage += stats.Sum(StatType.CritDamage, tags, dt);
            s.StatusChanceAdd += stats.Sum(StatType.StatusChance, tags, dt);
            s.StatusDurationPermille += stats.Sum(StatType.StatusDuration, tags, dt);
            int range = stats.Sum(StatType.Range, tags, dt);
            if (range != 0) s.RangeMilli = FMathI.Permille(s.RangeMilli, 1000 + range);
            s.ProjectileSpeed = FMathI.Permille(s.ProjectileSpeed, 1000 + stats.Sum(StatType.ProjectileSpeed, tags, dt));
            s.ProcPermille += stats.Sum(StatType.ProcDamage, tags, dt);
            s.ChainCount += stats.Sum(StatType.ChainCount, tags, dt);
            s.ProjectileAdd += s.IsProjectileFrame ? stats.Sum(StatType.ProjectileCount, tags, dt) : 0;
            s.LeechPermille += stats.Sum(StatType.Leech, tags, dt);
            s.LinkThresholdReduce += stats.Sum(StatType.RelayThreshold, tags, dt);
        }

        public static void ApplyOp(WeaponSpec s, ModOpDef op)
        {
            int v = op.Value;
            switch (op.Op)
            {
                case ModOp.ProjectileAdd: s.ProjectileAdd += v; break;
                case ModOp.SplitDamage: s.SplitDamagePermille = FMathI.Permille(s.SplitDamagePermille, v); break;
                case ModOp.PierceAdd: s.Pierce += v; break;
                case ModOp.RicochetAdd: s.Ricochet += v; break;
                case ModOp.Homing: s.Homing = v != 0; break;
                case ModOp.CooldownPermille: s.CooldownTicks = Math.Max(4, FMathI.Permille(s.CooldownTicks, v)); break;
                case ModOp.DamagePermille: s.DamagePermille = FMathI.Permille(s.DamagePermille, v); break;
                case ModOp.Knockback: s.KnockbackMilli += v; break;
                case ModOp.Pull: s.PullMilli += v; break;
                case ModOp.RepeatCount: s.RepeatCount += v; break;
                case ModOp.RepeatPermille: s.RepeatPermille = v; break;
                case ModOp.RepeatDelay: s.RepeatDelayTicks = Math.Max(1, v); break;
                case ModOp.DelayedDetonate: s.DelayedDetonatePermille += v; break;
                case ModOp.ExplosionRadius: s.ExplosionRadius = Math.Max(s.ExplosionRadius, v); break;
                case ModOp.ExplosionPermille: s.ExplosionPermille = v; break;
                case ModOp.Shockwave: s.ShockwavePermille += v; break;
                case ModOp.ShatterBonus: s.ShatterBonusPermille += v; break;
                case ModOp.ArcAdd: s.ArcAddMilli += v; break;
                case ModOp.RangePermille: s.RangeMilli = FMathI.Permille(s.RangeMilli, v); break;
                case ModOp.CritChanceAdd: s.CritChance += v; break;
                case ModOp.CritDamageAdd: s.CritDamage += v; break;
                case ModOp.BleedChance: s.BleedChance += v; break;
                case ModOp.StaggerChance: s.StaggerChance += v; break;
                case ModOp.VolatileOnKill: s.VolatileOnKillPermille += v; break;
                case ModOp.ChainAdd: s.ChainCount += v; break;
                case ModOp.ChainPermille: s.ChainPermille = v; break;
                case ModOp.OrbitBlades: s.OrbitBlades += v; break;
                case ModOp.Boomerang: s.Boomerang = v != 0; break;
                case ModOp.FocusBonus: s.FocusBonusPermille += v; break;
                case ModOp.Lingering: s.LingeringPermille += v; break;
                case ModOp.OverchargeEvery: s.OverchargeEvery = v; break;
                case ModOp.OverchargePermille: s.OverchargePermille = v; break;
                case ModOp.Leech: s.LeechPermille += v; break;
                case ModOp.ShieldOnDamage: s.ShieldOnDamagePermille += v; break;
                case ModOp.ExecuteBelow: s.ExecuteBelowPermille = Math.Max(s.ExecuteBelowPermille, v); break;
                case ModOp.ExecuteMult: s.ExecuteMultPermille = v; break;
                case ModOp.TempoPerAttack: s.TempoPerAttack += v; break;
                case ModOp.TempoMax: s.TempoMax += v; break;
                case ModOp.AmbushPermille: s.AmbushPermille += v; break;
                case ModOp.ArmorBreakStacks: s.ArmorBreakStacks += v; break;
                case ModOp.TwinStrike: s.TwinStrike = v != 0; break;
                case ModOp.BarrageCount: s.BarrageCount += v; break;
                case ModOp.StatusChanceAdd: s.StatusChanceAdd += v; break;
                case ModOp.LinkThresholdReduce: s.LinkThresholdReduce += v; break;
                case ModOp.FrenzyAttackSpeed: s.FrenzyAttackSpeed += v; break;
                case ModOp.MaxTargetsAdd: s.MaxTargetsAdd += v; break;
                case ModOp.ProjectileSpeedPermille: s.ProjectileSpeed = FMathI.Permille(s.ProjectileSpeed, v); break;
                case ModOp.StatusDurationPermille: s.StatusDurationPermille = FMathI.Permille(s.StatusDurationPermille, v); break;
                case ModOp.BonusVsStatus:
                    {
                        StatusType st;
                        if (op.Tag != null && Enum.TryParse(op.Tag, true, out st)) { s.BonusVsStatus = st; s.BonusVsStatusPermille += v; }
                        break;
                    }
                case ModOp.ConvertDamage:
                    {
                        DamageType dt;
                        if (op.Tag != null && Enum.TryParse(op.Tag, true, out dt)) s.DamageType = dt;
                        break;
                    }
                case ModOp.ArmorPen: s.ArmorPen += v; break;
                case ModOp.WindupPermille: s.WindupPermille = FMathI.Permille(s.WindupPermille, v); break;
                case ModOp.SummonCapAdd: s.SummonCapAdd += v; break;
                case ModOp.ProcPermille: s.ProcPermille = FMathI.Permille(s.ProcPermille, v); break;
                case ModOp.Scatter: s.Scatter = v != 0; break;
            }
        }
    }

    internal static class FMathI
    {
        public static int Permille(int value, int permille) { return (int)(((long)value * permille) / 1000); }
    }
}
