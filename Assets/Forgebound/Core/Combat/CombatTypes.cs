using System;
using System.Collections.Generic;
using Forgebound.Core.Content;
using Forgebound.Core.Weapons;

namespace Forgebound.Core.Combat
{
    public enum CombatEventType : byte
    {
        Spawn, WaveStart, AttackWindup, AttackRelease, ProjectileSpawn, ProjectileHit, ProjectileExpire, Damage, Miss, Heal,
        ShieldGain, ShieldBreak, StatusApplied, StatusExpired, Death, RuneProc, LinkCharge, LinkFire, Telegraph,
        AbilityCast, PhaseChange, Summon, ZoneSpawn, Knockback, ProcSuppressed, Revive, SignatureFire, Enrage, BattleEnd
    }

    /// <summary>Compact, allocation-light event record streamed to presentation and analytics.</summary>
    public struct CombatEvent
    {
        public int Tick;
        public CombatEventType Type;
        public int A;          // source unit id (or projectile id)
        public int B;          // target unit id
        public int Value;      // damage / amount / status id / ability id
        public int Value2;     // damage type / stacks / link index / flags
        public int Weapon;     // weapon index (0..2) or -1
        public int X;          // world position (milli-units)
        public bool Crit;
        public bool Proc;
        public string Tag;     // rune id / ability name / signature (rare events only)

        public override string ToString()
        {
            return Tick + ":" + Type + " a=" + A + " b=" + B + " v=" + Value + " v2=" + Value2 + " w=" + Weapon + (Crit ? " crit" : "") + (Proc ? " proc" : "") + (Tag != null ? " " + Tag : "");
        }
    }

    public enum UnitState : byte { Idle, Moving, Windup, Recover, Stunned, Ability, Dead, Burrowed }

    public sealed class StatusState
    {
        public readonly int[] Stacks = new int[(int)StatusType.Count];
        public readonly int[] Ticks = new int[(int)StatusType.Count];
        public readonly int[] Potency = new int[(int)StatusType.Count];       // damage per tick-interval per stack for DoTs
        public readonly int[] SourceWeapon = new int[(int)StatusType.Count];
        public int FreezeImmuneTicks;

        public bool Has(StatusType s) { return Stacks[(int)s] > 0; }
        public int Get(StatusType s) { return Stacks[(int)s]; }

        public void Clear(StatusType s)
        {
            Stacks[(int)s] = 0;
            Ticks[(int)s] = 0;
            Potency[(int)s] = 0;
        }
    }

    public sealed class Unit
    {
        public int Id;
        public int Team;               // 0 = player side, 1 = enemies
        public bool IsHero;
        public bool IsFamiliar;
        public EnemyDef Def;
        public int MaxHp, Hp, Shield, Armor, Evasion;
        public int[] Resist = new int[9];
        public int X;
        public int Radius = 400;
        public int Speed;
        public bool Flying;
        public bool Stealthed;
        public bool Burrowed;
        public bool Reflects;
        public int ReflectChance;
        public UnitState State;
        public int StateTimer;
        public int AttackCooldown;
        public int[] AbilityTimers;
        public int PendingAbility = -1;
        public int PhaseIndex = -1;
        public List<EnemyAbilityDef> ActiveAbilities = new List<EnemyAbilityDef>();
        public int DamagePermille = 1000;
        public int SpeedPermille = 1000;
        public int AttackSpeedPermille = 1000;
        public int Threat = 1;
        public EnemyRank Rank;
        public readonly StatusState Status = new StatusState();
        public int SpawnOrder;
        public int LastHitWeapon = -1;
        public int OwnerWeapon = -1;       // familiars
        public int LifeTicks = -1;         // familiars / timed summons
        public int FamiliarDamage;
        public bool Enraged;
        public bool Revived;
        public int SummonedBy = -1;
        public int BarrierHp;              // frontal barrier blocking projectiles
        public int ThornsPermille;
        public int RegenPerSecond;
        public int MoveDir;                // last movement direction for hysteresis
        public bool Alive { get { return State != UnitState.Dead; } }
        public bool Targetable { get { return State != UnitState.Dead && !Stealthed && !Burrowed; } }
        public bool IsBoss { get { return Rank == EnemyRank.Boss; } }
        public bool IsElite { get { return Rank == EnemyRank.Elite; } }
    }

    public sealed class Projectile
    {
        public int Id;
        public int Team;
        public int X;
        public int Dir;
        public int Speed;
        public int TargetId;
        public int AimX;
        public bool Homing;
        public int Damage;
        public DamageType Type;
        public int Weapon = -1;
        public bool IsProc;
        public int ProcDepth;
        public int PierceLeft;
        public int RicochetLeft;
        public bool Boomerang;
        public bool Returning;
        public int LifeTicks = 30 * 4;
        public int SourceUnit;
        public bool Dead;
        public bool IsCrit;
        public bool IsEnemy;
        public StatusApply EnemyStatus;
        public readonly List<int> HitIds = new List<int>(4);
        public bool Wave;   // ground wave (shockwave / legendary wave): hits everything it passes
        public int StepIndex;
    }

    public enum ZoneKind : byte { Lingering, Delayed, EnemySlam, Singularity, Thunder, Starfall }

    public sealed class Zone
    {
        public int Id;
        public ZoneKind Kind;
        public int Team;
        public int X;
        public int Radius;
        public int TicksLeft;
        public int Interval;
        public int Timer;
        public int Damage;
        public DamageType Type;
        public int Weapon = -1;
        public bool IsProc;
        public int ProcDepth;
        public int SourceUnit;
        public StatusApply Status;
        public bool Dead;
    }

    public enum ScheduledKind : byte { RepeatAttack, LinkProc, BarrageShot, TwinStrike }

    public struct Scheduled
    {
        public int Tick;
        public int Seq;
        public ScheduledKind Kind;
        public int Weapon;
        public int Power;
        public int Link;
        public int TargetId;
        public int ProcDepth;
        public int Step;
    }

    /// <summary>Gameplay trigger raised during a tick and processed (FIFO, depth-limited) at the end of that tick.</summary>
    public struct TriggerEvent
    {
        public TriggerType Type;
        public int Weapon;          // weapon index that caused it (-1 = hero-level)
        public int SourceUnit;
        public int TargetUnit;
        public int Amount;
        public DamageType DamageType;
        public StatusType Status;
        public bool Crit;
        public bool IsProc;
        public int ProcDepth;
        public int OriginRune;      // index into the rune table that generated this (self-trigger guard), -1 if none
    }

    /// <summary>Signal route compiled from a BoardLink.</summary>
    public sealed class CompiledLink
    {
        public int Index;
        public int SourceWeapon;           // index into weapon list
        public int DestWeapon = -1;        // -1 = terminal relay effect
        public TriggerType Listen = TriggerType.OnHit;
        public int Threshold = 4;
        public int PowerPermille = 500;
        public int DelayTicks;
        public int MaxPerBattle;
        public int ConvertDamageType = -1;
        public StatusApply AddStatus;
        public bool Siphon;
        public bool Beacon;
        public bool CapacitorNova;
        public int RadiusMilli = 2000;
        public string Description;
        public readonly List<int> RelayUids = new List<int>();
        // runtime
        public int Charge;
        public int Fired;
    }

    public sealed class HeroSetup
    {
        public string ForgemasterId;
        public int MaxHp = 100;
        public int Hp = 100;
        public int Armor;
        public int SpeedMilli = 70;   // milli-units per tick
        public StatBlock Stats = new StatBlock();
        public readonly List<RuneDef> HeroRunes = new List<RuneDef>();
        public readonly List<WeaponSpec> Weapons = new List<WeaponSpec>();
        public readonly List<CompiledLink> Links = new List<CompiledLink>();
        public TargetPriority Targeting = TargetPriority.Nearest;
        /// <summary>Inactive links / idle pieces from board analysis (reported, never applied).</summary>
        public readonly List<string> DeadLinks = new List<string>();
    }

    public sealed class BattleSetup
    {
        public ContentDatabase Db;
        public ulong Seed;
        public HeroSetup Hero = new HeroSetup();
        public EncounterDef Encounter;
        public int Chapter = 1;
        public StatBlock Difficulty = new StatBlock();
        public int MaxTicks = 30 * 240;
        public bool RecordEvents = true;
        public int MaxProcDepth = 3;
        public int MaxProcsPerTick = 48;
        public int MaxProjectiles = 160;
        public int MaxZones = 40;
        public int MaxFamiliars = 4;
        /// <summary>Training dummy mode: enemies never attack, battle ends at MaxTicks (Workshop).</summary>
        public bool DummyMode;
        /// <summary>Board links/pieces that are inactive (shown in the battle report; they never apply effects).</summary>
        public readonly List<string> DeadLinks = new List<string>();
    }

    public sealed class WeaponStats
    {
        public long Damage, ProcDamage, DotDamage;
        public int Attacks, Hits, Crits, Kills, Procs;
        public int ProjectilesFired;
    }

    public sealed class BattleReport
    {
        public readonly WeaponStats[] Weapons = { new WeaponStats(), new WeaponStats(), new WeaponStats() };
        public readonly Dictionary<string, int> RuneTriggers = new Dictionary<string, int>();
        public readonly Dictionary<string, int> RuneFailures = new Dictionary<string, int>();   // "rune:reason"
        public readonly Dictionary<int, int> LinkFires = new Dictionary<int, int>();
        public readonly Dictionary<int, long> LinkDamage = new Dictionary<int, long>();
        public readonly long[] StatusUptimeTicks = new long[(int)StatusType.Count];
        public readonly Dictionary<string, long> DamageTakenBySource = new Dictionary<string, long>();
        public long DamageTaken, DamageBlockedByShield, DamageMitigatedByArmor, Healing, ShieldGained, LeechHealing;
        public int ProcsSuppressedDepth, ProcsSuppressedTickCap, ProcsSuppressedBudget;
        public int MaxProcsInOneTick;
        public int MaxChainDepthReached;
        public int EnemiesKilled, EnemiesSpawned;
        public int Ticks;
        public int PeakProjectiles, PeakZones, PeakUnits;
        public readonly List<string> DeadLinks = new List<string>();
        public readonly List<string> Insights = new List<string>();
        public string KilledBy;

        public long TotalDamage
        {
            get { long t = 0; foreach (var w in Weapons) t += w.Damage; return t; }
        }
    }

    public sealed class BattleResult
    {
        public bool Victory;
        public bool Timeout;
        public int Ticks;
        public int HeroHp;
        public int GoldBonus;
        public BattleReport Report;
        public string DeathReason;
    }
}
