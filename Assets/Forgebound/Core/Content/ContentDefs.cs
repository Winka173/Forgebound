using System;
using System.Collections.Generic;

namespace Forgebound.Core.Content
{
    /// <summary>Shared parse helpers that collect errors instead of throwing, so the validator can report all issues at once.</summary>
    public static class P
    {
        public static T Enum<T>(JsonValue v, string key, T def, List<string> errors, string ctx) where T : struct
        {
            var s = v.Str(key);
            if (s == null) return def;
            T result;
            if (System.Enum.TryParse(s, true, out result)) return result;
            // allow snake_case -> PascalCase
            var pascal = ToPascal(s);
            if (System.Enum.TryParse(pascal, true, out result)) return result;
            errors.Add(ctx + ": unknown " + typeof(T).Name + " '" + s + "' in field '" + key + "'");
            return def;
        }

        public static T EnumStr<T>(string s, T def, List<string> errors, string ctx) where T : struct
        {
            if (s == null) return def;
            T result;
            if (System.Enum.TryParse(s, true, out result)) return result;
            if (System.Enum.TryParse(ToPascal(s), true, out result)) return result;
            errors.Add(ctx + ": unknown " + typeof(T).Name + " '" + s + "'");
            return def;
        }

        public static string ToPascal(string s)
        {
            var sb = new System.Text.StringBuilder();
            bool up = true;
            foreach (char c in s)
            {
                if (c == '_' || c == '-' || c == ' ') { up = true; continue; }
                sb.Append(up ? char.ToUpperInvariant(c) : c);
                up = false;
            }
            return sb.ToString();
        }

        public static string RequireId(JsonValue v, List<string> errors, string ctx)
        {
            var id = v.Str("id");
            if (string.IsNullOrEmpty(id)) errors.Add(ctx + ": missing id");
            return id ?? "";
        }
    }

    public struct Cell
    {
        public int X, Y;
        public Cell(int x, int y) { X = x; Y = y; }
        public override string ToString() { return "(" + X + "," + Y + ")"; }
        public override bool Equals(object obj) { return obj is Cell && ((Cell)obj).X == X && ((Cell)obj).Y == Y; }
        public override int GetHashCode() { return X * 397 ^ Y; }
    }

    public sealed class PortDef
    {
        public int CellIndex;
        public PortDir Dir;
        public PortKind Kind;

        public static PortDef Parse(JsonValue v, List<string> errors, string ctx)
        {
            return new PortDef
            {
                CellIndex = v.Int("cell"),
                Dir = P.Enum(v, "dir", PortDir.East, errors, ctx),
                Kind = P.Enum(v, "kind", PortKind.Both, errors, ctx)
            };
        }
    }

    public sealed class StatMod
    {
        public StatType Stat;
        public int Value;      // flat or permille depending on stat (documented in docs/CONTENT_SCHEMA.md)
        public string Tag;     // optional filter: frame tag, damage type name or status name

        public static StatMod Parse(JsonValue v, List<string> errors, string ctx)
        {
            return new StatMod { Stat = P.Enum(v, "stat", StatType.None, errors, ctx), Value = v.Int("value"), Tag = v.Str("tag") };
        }

        public static List<StatMod> ParseList(JsonValue arr, List<string> errors, string ctx)
        {
            var list = new List<StatMod>();
            foreach (var e in arr.Elements()) list.Add(Parse(e, errors, ctx));
            return list;
        }
    }

    public sealed class StatusApply
    {
        public StatusType Status;
        public int ChancePermille = 1000;
        public int Stacks = 1;
        public int DurationTicks = 90;

        public static StatusApply Parse(JsonValue v, List<string> errors, string ctx)
        {
            if (v.IsNull) return null;
            return new StatusApply
            {
                Status = P.Enum(v, "status", StatusType.None, errors, ctx),
                ChancePermille = v.Permille("chance", 1000),
                Stacks = v.Int("stacks", 1),
                DurationTicks = v.Ticks("duration", 90)
            };
        }
    }

    public sealed class EffectDef
    {
        public EffectType Type;
        public EffectTarget Target = EffectTarget.TriggerTarget;
        public int Permille;            // relative to owning weapon's base damage (or hero max HP for heal/shield when FromMaxHp)
        public int Flat;
        public bool FromMaxHp;
        public int DamageType = -1;     // -1 = inherit weapon damage type
        public StatusType Status;
        public int Stacks = 1;
        public int DurationTicks = 90;
        public int RadiusMilli = 1500;
        public int Count = 1;
        public int ChancePermille = 1000;

        public static EffectDef Parse(JsonValue v, List<string> errors, string ctx)
        {
            var e = new EffectDef
            {
                Type = P.Enum(v, "type", EffectType.None, errors, ctx),
                Target = P.Enum(v, "target", EffectTarget.TriggerTarget, errors, ctx),
                Permille = v.Permille("power", 0),
                Flat = v.Int("flat"),
                FromMaxHp = v.BoolOf("fromMaxHp"),
                Status = P.Enum(v, "status", StatusType.None, errors, ctx),
                Stacks = v.Int("stacks", 1),
                DurationTicks = v.Ticks("duration", 90),
                RadiusMilli = v.Milli("radius", 1500),
                Count = v.Int("count", 1),
                ChancePermille = v.Permille("chance", 1000)
            };
            var dt = v.Str("damageType");
            if (dt != null) e.DamageType = (int)P.EnumStr(dt, Content.DamageType.Physical, errors, ctx);
            if (e.Type == EffectType.None) errors.Add(ctx + ": effect without type");
            return e;
        }

        public static List<EffectDef> ParseList(JsonValue arr, List<string> errors, string ctx)
        {
            var list = new List<EffectDef>();
            foreach (var e in arr.Elements()) list.Add(Parse(e, errors, ctx));
            return list;
        }
    }

    public sealed class RuneCondition
    {
        public int DamageType = -1;
        public StatusType TargetHasStatus;
        public StatusType StatusIs;           // for OnStatusApplied
        public int TargetHpBelowPermille;     // 0 = ignore
        public int HeroHpBelowPermille;       // 0 = ignore
        public int EveryN;                    // fire every Nth qualifying trigger (0/1 = every)
        public bool AllowFromProc;            // may trigger from proc-generated events (depth-limited)
        public bool OnlyPrimary;
        public bool TargetIsElite;

        public static RuneCondition Parse(JsonValue v, List<string> errors, string ctx)
        {
            var c = new RuneCondition();
            if (v.IsNull) return c;
            var dt = v.Str("damageType");
            if (dt != null) c.DamageType = (int)P.EnumStr(dt, Content.DamageType.Physical, errors, ctx);
            c.TargetHasStatus = P.Enum(v, "targetHasStatus", StatusType.None, errors, ctx);
            c.StatusIs = P.Enum(v, "statusIs", StatusType.None, errors, ctx);
            c.TargetHpBelowPermille = v.Permille("targetHpBelow", 0);
            c.HeroHpBelowPermille = v.Permille("heroHpBelow", 0);
            c.EveryN = v.Int("everyN", 0);
            c.AllowFromProc = v.BoolOf("allowFromProc");
            c.OnlyPrimary = v.BoolOf("onlyPrimary");
            c.TargetIsElite = v.BoolOf("targetIsElite");
            return c;
        }
    }

    public sealed class RuneDef
    {
        public string Id;
        public TriggerType Trigger;
        public int TimerTicks;
        public RuneCondition Condition = new RuneCondition();
        public List<EffectDef> Effects = new List<EffectDef>();
        public int IcdTicks;
        public int MaxPerBattle;      // 0 = unlimited
        public int ChancePermille = 1000;
        public Rarity Rarity;
        public int Cost = 40;
        public List<string> Tags = new List<string>();
        public List<string> RequiresTags = new List<string>();
        public string Icon;

        public static RuneDef Parse(JsonValue v, List<string> errors, string ctxPrefix)
        {
            var id = P.RequireId(v, errors, ctxPrefix);
            string ctx = ctxPrefix + ":" + id;
            var r = new RuneDef
            {
                Id = id,
                Trigger = P.Enum(v, "trigger", TriggerType.None, errors, ctx),
                TimerTicks = v.Ticks("interval", 0),
                Condition = RuneCondition.Parse(v["condition"], errors, ctx),
                Effects = EffectDef.ParseList(v["effects"], errors, ctx),
                IcdTicks = v.Ticks("icd", 0),
                MaxPerBattle = v.Int("maxPerBattle", 0),
                ChancePermille = v.Permille("chance", 1000),
                Rarity = P.Enum(v, "rarity", Rarity.Common, errors, ctx),
                Cost = v.Int("cost", 40),
                Tags = v.StrList("tags"),
                RequiresTags = v.StrList("requiresTags"),
                Icon = v.Str("icon")
            };
            if (r.Trigger == TriggerType.None) errors.Add(ctx + ": rune without trigger");
            if (r.Trigger == TriggerType.OnTimer && r.TimerTicks <= 0) errors.Add(ctx + ": OnTimer rune needs interval > 0");
            if (r.Effects.Count == 0) errors.Add(ctx + ": rune without effects");
            return r;
        }
    }

    public sealed class AttackStep
    {
        public StepKind Kind;
        public HitShape Shape;
        public int DamagePermille = 1000;
        public int Hits = 1;
        public int WidthMilli;          // arc/around radius or line length
        public int MaxTargets = 1;
        public int WindupTicks = -1;    // -1 = frame default
        public int CooldownTicks = -1;  // -1 = frame default
        public int ProjectileSpeed;     // milli-units per tick, 0 = frame default
        public int Pierce;
        public int StaggerChance;
        public int ArmorBreak;
        public int ShieldPermille;      // hero gains shield = damage dealt * permille
        public int PullMilli;
        public string SummonId;
        public int SummonCap = 2;
        public StatusApply Status;

        public static AttackStep Parse(JsonValue v, List<string> errors, string ctx)
        {
            return new AttackStep
            {
                Kind = P.Enum(v, "kind", StepKind.Melee, errors, ctx),
                Shape = P.Enum(v, "shape", HitShape.Single, errors, ctx),
                DamagePermille = v.Permille("damage", 1000),
                Hits = Math.Max(1, v.Int("hits", 1)),
                WidthMilli = v.Milli("width", 0),
                MaxTargets = Math.Max(1, v.Int("maxTargets", 1)),
                WindupTicks = v.Has("windup") ? v.Ticks("windup") : -1,
                CooldownTicks = v.Has("cooldown") ? v.Ticks("cooldown") : -1,
                ProjectileSpeed = v.Milli("projectileSpeed", 0) / 30,
                Pierce = v.Int("pierce"),
                StaggerChance = v.Permille("stagger", 0),
                ArmorBreak = v.Int("armorBreak"),
                ShieldPermille = v.Permille("shield", 0),
                PullMilli = v.Milli("pull", 0),
                SummonId = v.Str("summon"),
                SummonCap = v.Int("summonCap", 2),
                Status = StatusApply.Parse(v["status"], errors, ctx)
            };
        }
    }

    public sealed class FrameDef
    {
        public string Id;
        public List<string> Tags = new List<string>();
        public Stance Stance;
        public int RangeMilli;
        public int MinRangeMilli;
        public int BaseDamage;
        public int CooldownTicks;
        public int WindupTicks;
        public int ProjectileSpeed;     // milli-units per tick
        public int CritChance = 50;     // permille
        public int CritDamage = 1500;   // permille multiplier
        public DamageType DamageType = DamageType.Physical;
        public List<AttackStep> Steps = new List<AttackStep>();
        public List<Cell> Footprint = new List<Cell>();
        public List<PortDef> Ports = new List<PortDef>();
        public int ModifierSlots = 1;
        public int RuneSlots = 1;
        public Rarity Rarity;
        public int Cost = 60;
        public string Sfx;
        public string Vfx;
        public string Animation;

        public static FrameDef Parse(JsonValue v, List<string> errors)
        {
            var id = P.RequireId(v, errors, "frame");
            string ctx = "frame:" + id;
            var f = new FrameDef
            {
                Id = id,
                Tags = v.StrList("tags"),
                Stance = P.Enum(v, "stance", Stance.Melee, errors, ctx),
                RangeMilli = v.Milli("range", 1500),
                MinRangeMilli = v.Milli("minRange", 0),
                BaseDamage = v.Int("baseDamage", 10),
                CooldownTicks = v.Ticks("cooldown", 30),
                WindupTicks = v.Ticks("windup", 6),
                ProjectileSpeed = v.Milli("projectileSpeed", 12000) / 30,
                CritChance = v.Permille("critChance", 50),
                CritDamage = v.Permille("critDamage", 1500),
                DamageType = P.Enum(v, "damageType", DamageType.Physical, errors, ctx),
                ModifierSlots = v.Int("modifierSlots", 1),
                RuneSlots = v.Int("runeSlots", 1),
                Rarity = P.Enum(v, "rarity", Rarity.Common, errors, ctx),
                Cost = v.Int("cost", 60),
                Sfx = v.Str("sfx"),
                Vfx = v.Str("vfx"),
                Animation = v.Str("animation")
            };
            foreach (var s in v["steps"].Elements()) f.Steps.Add(AttackStep.Parse(s, errors, ctx));
            foreach (var c in v["footprint"].Elements()) f.Footprint.Add(new Cell(c[0].AsInt(), c[1].AsInt()));
            foreach (var p in v["ports"].Elements()) f.Ports.Add(PortDef.Parse(p, errors, ctx));
            if (f.Steps.Count == 0) errors.Add(ctx + ": frame has no attack steps");
            if (f.Footprint.Count == 0) errors.Add(ctx + ": frame has no footprint");
            return f;
        }
    }

    public sealed class CoreDef
    {
        public string Id;
        public List<string> Tags = new List<string>();
        public int DamageType = -1;     // -1 keeps frame damage type (utility cores)
        public StatusApply Status;
        public List<StatMod> Stats = new List<StatMod>();
        public List<RuneDef> InnateRunes = new List<RuneDef>();
        public string ColorHex = "#ffffff";
        public Rarity Rarity;
        public int Cost = 70;
        public string Sfx;
        public string Vfx;

        public static CoreDef Parse(JsonValue v, List<string> errors)
        {
            var id = P.RequireId(v, errors, "core");
            string ctx = "core:" + id;
            var c = new CoreDef
            {
                Id = id,
                Tags = v.StrList("tags"),
                Status = StatusApply.Parse(v["status"], errors, ctx),
                Stats = StatMod.ParseList(v["stats"], errors, ctx),
                ColorHex = v.Str("color", "#ffffff"),
                Rarity = P.Enum(v, "rarity", Rarity.Common, errors, ctx),
                Cost = v.Int("cost", 70),
                Sfx = v.Str("sfx"),
                Vfx = v.Str("vfx")
            };
            var dt = v.Str("damageType");
            if (dt != null) c.DamageType = (int)P.EnumStr(dt, Content.DamageType.Physical, errors, ctx);
            foreach (var r in v["runes"].Elements()) c.InnateRunes.Add(RuneDef.Parse(r, errors, ctx));
            return c;
        }
    }

    public enum ModOp
    {
        None,
        ProjectileAdd, SplitDamage, PierceAdd, RicochetAdd, Homing, CooldownPermille, DamagePermille,
        Knockback, Pull, RepeatCount, RepeatPermille, RepeatDelay, DelayedDetonate, ExplosionRadius, ExplosionPermille,
        Shockwave, ShatterBonus, ArcAdd, RangePermille, CritChanceAdd, CritDamageAdd, BleedChance, StaggerChance,
        VolatileOnKill, ChainAdd, ChainPermille, OrbitBlades, Boomerang, FocusBonus, Lingering, OverchargeEvery,
        OverchargePermille, Leech, ShieldOnDamage, ExecuteBelow, ExecuteMult, TempoPerAttack, TempoMax, AmbushPermille,
        ArmorBreakStacks, TwinStrike, BarrageCount, StatusChanceAdd, LinkThresholdReduce, FrenzyAttackSpeed,
        MaxTargetsAdd, ProjectileSpeedPermille, StatusDurationPermille, BonusVsStatus, ConvertDamage, ArmorPen,
        WindupPermille, SummonCapAdd, ProcPermille, Scatter
    }

    public sealed class ModOpDef
    {
        public ModOp Op;
        public int Value;
        public string Tag;

        public static ModOpDef Parse(JsonValue v, List<string> errors, string ctx)
        {
            return new ModOpDef { Op = P.Enum(v, "op", ModOp.None, errors, ctx), Value = v.Int("value"), Tag = v.Str("tag") };
        }
    }

    public sealed class ModifierDef
    {
        public string Id;
        public List<ModOpDef> Ops = new List<ModOpDef>();
        public List<string> RequiresTags = new List<string>();   // frame must have ANY of these tags (empty = all)
        public List<string> ExcludesTags = new List<string>();
        public string ConflictGroup;
        public int SlotCost = 1;
        public int Stage;                                         // precedence: 0 spawn,1 split,2 target,3 pierce,4 impact
        public Rarity Rarity;
        public int Cost = 50;
        public List<string> Tags = new List<string>();

        public static ModifierDef Parse(JsonValue v, List<string> errors)
        {
            var id = P.RequireId(v, errors, "modifier");
            string ctx = "modifier:" + id;
            var m = new ModifierDef
            {
                Id = id,
                RequiresTags = v.StrList("requiresTags"),
                ExcludesTags = v.StrList("excludesTags"),
                ConflictGroup = v.Str("conflictGroup"),
                SlotCost = v.Int("slotCost", 1),
                Rarity = P.Enum(v, "rarity", Rarity.Common, errors, ctx),
                Cost = v.Int("cost", 50),
                Tags = v.StrList("tags")
            };
            var stage = v.Str("stage", "impact");
            switch (stage)
            {
                case "spawn": m.Stage = 0; break;
                case "split": m.Stage = 1; break;
                case "target": m.Stage = 2; break;
                case "pierce": m.Stage = 3; break;
                case "impact": m.Stage = 4; break;
                case "timing": m.Stage = 5; break;
                default: errors.Add(ctx + ": unknown stage " + stage); break;
            }
            foreach (var o in v["ops"].Elements()) m.Ops.Add(ModOpDef.Parse(o, errors, ctx));
            if (m.Ops.Count == 0) errors.Add(ctx + ": modifier without ops");
            return m;
        }
    }

    public sealed class RelayDef
    {
        public RelayType Type;
        public TriggerType Listen = TriggerType.OnHit;   // which source event charges the relay
        public int Threshold = 3;
        public int Permille = 1000;      // proc damage permille contribution / amplification
        public int Capacity = 2;         // max distinct links routed through it
        public int DelayTicks;
        public int MaxPerBattle;
        public int ConvertDamageType = -1;
        public StatusApply Status;
        public int RadiusMilli = 2000;

        public static RelayDef Parse(JsonValue v, List<string> errors, string ctx)
        {
            var r = new RelayDef
            {
                Type = P.Enum(v, "type", RelayType.None, errors, ctx),
                Listen = P.Enum(v, "listen", TriggerType.OnHit, errors, ctx),
                Threshold = Math.Max(1, v.Int("threshold", 3)),
                Permille = v.Permille("power", 1000),
                Capacity = Math.Max(1, v.Int("capacity", 2)),
                DelayTicks = v.Ticks("delay", 0),
                MaxPerBattle = v.Int("maxPerBattle", 0),
                Status = StatusApply.Parse(v["status"], errors, ctx),
                RadiusMilli = v.Milli("radius", 2000)
            };
            var dt = v.Str("convert");
            if (dt != null) r.ConvertDamageType = (int)P.EnumStr(dt, DamageType.Physical, errors, ctx);
            return r;
        }
    }

    public sealed class BoardPieceDef
    {
        public string Id;
        public PieceKind Kind;
        public List<Cell> Footprint = new List<Cell>();
        public List<PortDef> Ports = new List<PortDef>();
        public RelayDef Relay;
        public List<StatMod> AdjacentStats = new List<StatMod>();   // Support: applied to edge-adjacent weapons
        public int MaxAdjacent = 4;
        public Rarity Rarity;
        public int Cost = 45;
        public List<string> Tags = new List<string>();

        public static BoardPieceDef Parse(JsonValue v, List<string> errors)
        {
            var id = P.RequireId(v, errors, "piece");
            string ctx = "piece:" + id;
            var p = new BoardPieceDef
            {
                Id = id,
                Kind = P.Enum(v, "kind", PieceKind.Relay, errors, ctx),
                AdjacentStats = StatMod.ParseList(v["adjacent"], errors, ctx),
                MaxAdjacent = v.Int("maxAdjacent", 4),
                Rarity = P.Enum(v, "rarity", Rarity.Common, errors, ctx),
                Cost = v.Int("cost", 45),
                Tags = v.StrList("tags")
            };
            foreach (var c in v["footprint"].Elements()) p.Footprint.Add(new Cell(c[0].AsInt(), c[1].AsInt()));
            foreach (var q in v["ports"].Elements()) p.Ports.Add(PortDef.Parse(q, errors, ctx));
            if (!v["relay"].IsNull) p.Relay = RelayDef.Parse(v["relay"], errors, ctx);
            if (p.Footprint.Count == 0) errors.Add(ctx + ": piece has no footprint");
            if (p.Kind == PieceKind.Relay && p.Relay == null) errors.Add(ctx + ": relay piece without relay block");
            if (p.Kind == PieceKind.Support && p.AdjacentStats.Count == 0) errors.Add(ctx + ": support piece without adjacent stats");
            return p;
        }
    }

    public enum LegendarySignature
    {
        None,
        WaveOnAttack,        // each primary attack launches a ground wave
        StatusSpreadOnKill,  // statuses on a killed enemy jump to nearby enemies
        InfinitePierce,      // projectiles never stop and chain on each pierce
        QuakeEveryThird,     // every 3rd attack: stunning nova
        ShatterFrozen,       // hitting frozen enemies shatters for % max HP
        BladeStorm,          // hits store blades; release storm at threshold
        SoulHarvest,         // kills grant souls (+damage); at 10 souls summon a reaper
        Overclock,           // attack speed ramps without cap but overheats
        Mirror,              // every attack is mirrored behind the hero
        Singularity,         // periodic gravity well pulling enemies and dealing damage
        Bloodpact,           // spend HP to double damage, leech refunds
        Sanctuary,           // shield pulses heal when shield is full
        Thunderhead,         // periodic lightning storm at random enemies
        Plague,              // poison stacks never expire and spread on death
        Eclipse,             // alternates radiant/shadow every attack, each boosting the other
        Gearstorm,           // every attack also fires SignatureValue cog projectiles at nearby enemies
        Rootbind,            // hits root enemies; rooted take bonus damage
        Phoenix,             // once per battle, revive hero with burst
        Starfall,            // crits call a falling star
        Ricochet,            // projectile bounces between every enemy once
        Colossus             // huge single hits that knock back and stun, slow cadence
    }

    public sealed class LegendaryDef
    {
        public string Id;
        public string Frame;
        public string Core;
        public List<string> RequiresModifiers = new List<string>();   // all required
        public List<string> RequiresAnyRunes = new List<string>();    // at least one of (empty = none)
        public int MinTier = 1;
        public int EssenceCost = 3;
        public List<ModOpDef> Ops = new List<ModOpDef>();
        public List<RuneDef> InnateRunes = new List<RuneDef>();
        public LegendarySignature Signature;
        public int SignatureValue;
        public string Vfx;
        public List<string> Tags = new List<string>();

        public static LegendaryDef Parse(JsonValue v, List<string> errors)
        {
            var id = P.RequireId(v, errors, "legendary");
            string ctx = "legendary:" + id;
            var l = new LegendaryDef
            {
                Id = id,
                Frame = v.Str("frame"),
                Core = v.Str("core"),
                RequiresModifiers = v.StrList("requiresModifiers"),
                RequiresAnyRunes = v.StrList("requiresAnyRunes"),
                MinTier = v.Int("minTier", 1),
                EssenceCost = v.Int("essence", 3),
                Signature = P.Enum(v, "signature", LegendarySignature.None, errors, ctx),
                SignatureValue = v.Int("signatureValue", 0),
                Vfx = v.Str("vfx"),
                Tags = v.StrList("tags")
            };
            foreach (var o in v["ops"].Elements()) l.Ops.Add(ModOpDef.Parse(o, errors, ctx));
            foreach (var r in v["runes"].Elements()) l.InnateRunes.Add(RuneDef.Parse(r, errors, ctx));
            if (l.Signature == LegendarySignature.None && l.InnateRunes.Count == 0)
                errors.Add(ctx + ": legendary must change a rule (signature or innate rune), not only stats");
            return l;
        }
    }

    public sealed class ForgemasterDef
    {
        public string Id;
        public int MaxHp = 100;
        public int Armor;
        public List<StatMod> Perks = new List<StatMod>();
        public List<RuneDef> InnateRunes = new List<RuneDef>();
        public string StartFrame;
        public string StartCore;
        public List<string> StartPieces = new List<string>();
        public List<string> StartItems = new List<string>();
        public int StartGold = 40;
        public string Unlock;   // null = unlocked from start
        public string Silhouette;

        public static ForgemasterDef Parse(JsonValue v, List<string> errors)
        {
            var id = P.RequireId(v, errors, "forgemaster");
            string ctx = "forgemaster:" + id;
            var f = new ForgemasterDef
            {
                Id = id,
                MaxHp = v.Int("maxHp", 100),
                Armor = v.Int("armor", 0),
                Perks = StatMod.ParseList(v["perks"], errors, ctx),
                StartFrame = v.Str("startFrame"),
                StartCore = v.Str("startCore"),
                StartPieces = v.StrList("startPieces"),
                StartItems = v.StrList("startItems"),
                StartGold = v.Int("startGold", 40),
                Unlock = v.Str("unlock"),
                Silhouette = v.Str("silhouette")
            };
            foreach (var r in v["runes"].Elements()) f.InnateRunes.Add(RuneDef.Parse(r, errors, ctx));
            return f;
        }
    }

    public enum EnemyAbilityType
    {
        None, HealAlly, ShieldAlly, Summon, Charge, ExplodeOnDeath, SplitOnDeath, Stealth, ReflectProjectiles, Enrage,
        Regen, Thorns, Teleport, BuffAllies, Leap, Volley, Slam, Burrow, Barrier, CurseHero, ArmorUp, DrainHero, Revive,
        Taunt, Cleanse, Beam, StatusAura
    }

    public sealed class EnemyAbilityDef
    {
        public EnemyAbilityType Type;
        public int CooldownTicks = 150;
        public int WindupTicks = 30;   // telegraph duration
        public int Value;              // damage / heal / shield amount
        public int Permille = 1000;
        public int RadiusMilli = 3000;
        public string EnemyId;
        public int Count = 1;
        public int Cap = 6;
        public StatusApply Status;
        public int InitialDelayTicks;

        public static EnemyAbilityDef Parse(JsonValue v, List<string> errors, string ctx)
        {
            return new EnemyAbilityDef
            {
                Type = P.Enum(v, "type", EnemyAbilityType.None, errors, ctx),
                CooldownTicks = v.Ticks("cooldown", 150),
                WindupTicks = v.Ticks("windup", 30),
                Value = v.Int("value", 0),
                Permille = v.Permille("power", 1000),
                RadiusMilli = v.Milli("radius", 3000),
                EnemyId = v.Str("enemy"),
                Count = v.Int("count", 1),
                Cap = v.Int("cap", 6),
                Status = StatusApply.Parse(v["status"], errors, ctx),
                InitialDelayTicks = v.Ticks("delay", 0)
            };
        }
    }

    public sealed class BossPhaseDef
    {
        public int HpBelowPermille;
        public List<EnemyAbilityDef> Abilities = new List<EnemyAbilityDef>();
        public int SpeedPermille = 1000;
        public int DamagePermille = 1000;
        public int AttackSpeedPermille = 1000;
        public string NameKey;
        public Dictionary<DamageType, int> Resist = new Dictionary<DamageType, int>();
    }

    public sealed class EnemyDef
    {
        public string Id;
        public EnemyRank Rank;
        public int MaxHp = 30;
        public int Armor;
        public int Shield;
        public int Evasion;
        public Dictionary<DamageType, int> Resist = new Dictionary<DamageType, int>();
        public int SpeedMilli = 40;      // milli-units per tick
        public int RadiusMilli = 400;
        public bool Flying;
        public bool AttackRanged;
        public int AttackRangeMilli = 900;
        public int AttackDamage = 5;
        public int AttackCooldownTicks = 45;
        public int AttackWindupTicks = 12;
        public int ProjectileSpeed = 300;
        public DamageType AttackType = DamageType.Physical;
        public StatusApply AttackStatus;
        public List<EnemyAbilityDef> Abilities = new List<EnemyAbilityDef>();
        public List<BossPhaseDef> Phases = new List<BossPhaseDef>();
        public int Threat = 1;
        public int Gold = 3;
        public List<string> Tags = new List<string>();
        public string Role;    // swarm, tank, support, ranged, assassin, summoner, boss...
        public string Sprite;

        public static Dictionary<DamageType, int> ParseResist(JsonValue v, List<string> errors, string ctx)
        {
            var d = new Dictionary<DamageType, int>();
            if (v.Kind != JsonKind.Object) return d;
            foreach (var kv in v.Fields)
            {
                var t = P.EnumStr(kv.Key, DamageType.Physical, errors, ctx);
                d[t] = (int)Math.Round(kv.Value.AsDouble() * 1000);
            }
            return d;
        }

        public static EnemyDef Parse(JsonValue v, List<string> errors)
        {
            var id = P.RequireId(v, errors, "enemy");
            string ctx = "enemy:" + id;
            var e = new EnemyDef
            {
                Id = id,
                Rank = P.Enum(v, "rank", EnemyRank.Minion, errors, ctx),
                MaxHp = v.Int("hp", 30),
                Armor = v.Int("armor", 0),
                Shield = v.Int("shield", 0),
                Evasion = v.Permille("evasion", 0),
                Resist = ParseResist(v["resist"], errors, ctx),
                SpeedMilli = v.Milli("speed", 1200) / 30,
                RadiusMilli = v.Milli("radius", 400),
                Flying = v.BoolOf("flying"),
                Threat = v.Int("threat", 1),
                Gold = v.Int("gold", 3),
                Tags = v.StrList("tags"),
                Role = v.Str("role", "swarm"),
                Sprite = v.Str("sprite")
            };
            var a = v["attack"];
            if (!a.IsNull)
            {
                e.AttackRanged = a.BoolOf("ranged");
                e.AttackRangeMilli = a.Milli("range", e.AttackRanged ? 7000 : 900);
                e.AttackDamage = a.Int("damage", 5);
                e.AttackCooldownTicks = a.Ticks("cooldown", 45);
                e.AttackWindupTicks = a.Ticks("windup", 12);
                e.ProjectileSpeed = a.Milli("projectileSpeed", 9000) / 30;
                e.AttackType = P.Enum(a, "damageType", DamageType.Physical, errors, ctx);
                e.AttackStatus = StatusApply.Parse(a["status"], errors, ctx);
            }
            foreach (var ab in v["abilities"].Elements()) e.Abilities.Add(EnemyAbilityDef.Parse(ab, errors, ctx));
            foreach (var ph in v["phases"].Elements())
            {
                var phase = new BossPhaseDef
                {
                    HpBelowPermille = ph.Permille("hpBelow", 1000),
                    SpeedPermille = ph.Permille("speed", 1000),
                    DamagePermille = ph.Permille("damage", 1000),
                    AttackSpeedPermille = ph.Permille("attackSpeed", 1000),
                    NameKey = ph.Str("name"),
                    Resist = ParseResist(ph["resist"], errors, ctx)
                };
                foreach (var ab in ph["abilities"].Elements()) phase.Abilities.Add(EnemyAbilityDef.Parse(ab, errors, ctx));
                e.Phases.Add(phase);
            }
            if (e.MaxHp <= 0) errors.Add(ctx + ": hp must be > 0");
            if (e.Rank == EnemyRank.Boss && e.Phases.Count == 0) errors.Add(ctx + ": boss needs at least one phase");
            return e;
        }
    }

    public sealed class SpawnDef
    {
        public string Enemy;
        public int Count = 1;
    }

    public sealed class WaveDef
    {
        public int DelayTicks;
        public List<SpawnDef> Spawns = new List<SpawnDef>();
    }

    public sealed class EncounterDef
    {
        public string Id;
        public string Biome;
        public NodeType Kind = NodeType.Battle;
        public string Style = "swarm";   // swarm, elite, endurance, boss
        public int MinChapter = 1;
        public int MaxChapter = 5;
        public List<WaveDef> Waves = new List<WaveDef>();
        public int Weight = 10;
        public int SoftLimitTicks = 90 * 30;

        public static EncounterDef Parse(JsonValue v, List<string> errors)
        {
            var id = P.RequireId(v, errors, "encounter");
            string ctx = "encounter:" + id;
            var e = new EncounterDef
            {
                Id = id,
                Biome = v.Str("biome"),
                Kind = P.Enum(v, "kind", NodeType.Battle, errors, ctx),
                Style = v.Str("style", "swarm"),
                MinChapter = v.Int("minChapter", 1),
                MaxChapter = v.Int("maxChapter", 5),
                Weight = v.Int("weight", 10),
                SoftLimitTicks = v.Ticks("softLimit", 90 * 30)
            };
            foreach (var w in v["waves"].Elements())
            {
                var wave = new WaveDef { DelayTicks = w.Ticks("delay", 0) };
                foreach (var s in w["spawns"].Elements()) wave.Spawns.Add(new SpawnDef { Enemy = s.Str("enemy"), Count = Math.Max(1, s.Int("count", 1)) });
                e.Waves.Add(wave);
            }
            if (e.Waves.Count == 0) errors.Add(ctx + ": encounter without waves");
            return e;
        }
    }

    public enum OutcomeType
    {
        None, Gold, Essence, Heal, Damage, MaxHp, GainItem, GainRandomItem, GainBlessing, GainCurse, RemoveCurse,
        UpgradeWeapon, Battle, ExpandBoard, LoseItem, ShopDiscount, RevealMap, Nothing
    }

    public sealed class OutcomeDef
    {
        public OutcomeType Type;
        public int Value;
        public string Id;            // item / blessing / encounter id
        public ItemCategory Category;
        public Rarity MinRarity;
        public int ChancePermille = 1000;   // outcome happens with this chance

        public static OutcomeDef Parse(JsonValue v, List<string> errors, string ctx)
        {
            return new OutcomeDef
            {
                Type = P.Enum(v, "type", OutcomeType.None, errors, ctx),
                Value = v.Int("value"),
                Id = v.Str("id"),
                Category = P.Enum(v, "category", ItemCategory.Rune, errors, ctx),
                MinRarity = P.Enum(v, "minRarity", Rarity.Common, errors, ctx),
                ChancePermille = v.Permille("chance", 1000)
            };
        }
    }

    public sealed class EventOptionDef
    {
        public string TextKey;
        public int RequireGold;
        public int RequireEssence;
        public int RequireHp;
        public string RequireTag;   // e.g. forgemaster id or "has_weapon_fire"
        public List<OutcomeDef> Outcomes = new List<OutcomeDef>();
        public List<OutcomeDef> FailOutcomes = new List<OutcomeDef>();
        public int SuccessPermille = 1000;
    }

    public sealed class EventDef
    {
        public string Id;
        public string Biome;    // null = any
        public int MinChapter = 1;
        public List<EventOptionDef> Options = new List<EventOptionDef>();
        public int Weight = 10;
        public string Art;

        public static EventDef Parse(JsonValue v, List<string> errors)
        {
            var id = P.RequireId(v, errors, "event");
            string ctx = "event:" + id;
            var e = new EventDef { Id = id, Biome = v.Str("biome"), MinChapter = v.Int("minChapter", 1), Weight = v.Int("weight", 10), Art = v.Str("art") };
            int idx = 0;
            foreach (var o in v["options"].Elements())
            {
                var opt = new EventOptionDef
                {
                    TextKey = o.Str("text", "event." + id + ".opt" + idx),
                    RequireGold = o.Int("requireGold"),
                    RequireEssence = o.Int("requireEssence"),
                    RequireHp = o.Int("requireHp"),
                    RequireTag = o.Str("requireTag"),
                    SuccessPermille = o.Permille("success", 1000)
                };
                foreach (var oc in o["outcomes"].Elements()) opt.Outcomes.Add(OutcomeDef.Parse(oc, errors, ctx));
                foreach (var oc in o["fail"].Elements()) opt.FailOutcomes.Add(OutcomeDef.Parse(oc, errors, ctx));
                e.Options.Add(opt);
                idx++;
            }
            if (e.Options.Count < 2) errors.Add(ctx + ": events need at least two options (a real choice)");
            return e;
        }
    }

    public sealed class BlessingDef
    {
        public string Id;
        public bool IsCurse;
        public List<StatMod> Stats = new List<StatMod>();
        public List<RuneDef> Runes = new List<RuneDef>();
        public Rarity Rarity;
        public List<string> Tags = new List<string>();
        public string Group;   // anti-duplication: at most one per group per run

        public static BlessingDef Parse(JsonValue v, List<string> errors)
        {
            var id = P.RequireId(v, errors, "blessing");
            string ctx = "blessing:" + id;
            var b = new BlessingDef
            {
                Id = id,
                IsCurse = v.BoolOf("curse"),
                Stats = StatMod.ParseList(v["stats"], errors, ctx),
                Rarity = P.Enum(v, "rarity", Rarity.Common, errors, ctx),
                Tags = v.StrList("tags"),
                Group = v.Str("group")
            };
            foreach (var r in v["runes"].Elements()) b.Runes.Add(RuneDef.Parse(r, errors, ctx));
            if (b.Stats.Count == 0 && b.Runes.Count == 0) errors.Add(ctx + ": blessing/curse with no effect");
            return b;
        }
    }

    public sealed class BiomeDef
    {
        public string Id;
        public int Order;
        public List<string> Palette = new List<string>();
        public string Music;
        public string Ambience;
        public List<string> EliteEncounters = new List<string>();
        public List<string> BossEncounters = new List<string>();

        public static BiomeDef Parse(JsonValue v, List<string> errors)
        {
            var id = P.RequireId(v, errors, "biome");
            return new BiomeDef
            {
                Id = id,
                Order = v.Int("order"),
                Palette = v.StrList("palette"),
                Music = v.Str("music"),
                Ambience = v.Str("ambience"),
                EliteEncounters = v.StrList("elites"),
                BossEncounters = v.StrList("bosses")
            };
        }
    }

    public sealed class ChallengeDef
    {
        public string Id;
        public string Mode = "trial";     // trial (workshop) or adventure_modifier
        public List<StatMod> Rules = new List<StatMod>();
        public List<string> AllowedFrames = new List<string>();
        public string Encounter;
        public string Forgemaster;
        public List<string> GivenItems = new List<string>();
        public string RewardCosmetic;
        public int Tier = 1;

        public static ChallengeDef Parse(JsonValue v, List<string> errors)
        {
            var id = P.RequireId(v, errors, "challenge");
            string ctx = "challenge:" + id;
            return new ChallengeDef
            {
                Id = id,
                Mode = v.Str("mode", "trial"),
                Rules = StatMod.ParseList(v["rules"], errors, ctx),
                AllowedFrames = v.StrList("allowedFrames"),
                Encounter = v.Str("encounter"),
                Forgemaster = v.Str("forgemaster"),
                GivenItems = v.StrList("items"),
                RewardCosmetic = v.Str("reward"),
                Tier = v.Int("tier", 1)
            };
        }
    }
}
