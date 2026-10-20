using System;
using System.Collections.Generic;
using Forgebound.Core.Content;
using Forgebound.Core.Simulation;
using Forgebound.Core.Weapons;

namespace Forgebound.Core.Combat
{
    public sealed class WeaponRuntime
    {
        public WeaponSpec Spec;
        public int Index;
        public int Cooldown;
        public int StepIndex;
        public int AttackCount;
        public int WindupLeft;
        public int WindupStep = -1;
        public int PendingTarget = -1;
        public int Tempo;
        public bool AmbushReady = true;
        public int Souls;
        public int BladeStacks;
        public int Heat;
        public bool EclipseRadiant;
        public int SignatureTimer;
        public bool PhoenixUsed;
        public int OrbitTimer;
        public int SummonsAlive;
    }

    public sealed class RuneRuntime
    {
        public RuneDef Def;
        public int Weapon;       // -1 = hero-level (forgemaster / blessing), uses primary weapon for scaling
        public int Cooldown;
        public int Count;
        public int Qualifying;
        public int Timer;
        public string Key;
    }

    /// <summary>
    /// Deterministic fixed-tick side-view auto battle. 30 ticks/second, integer math, seeded RNG, stable iteration order.
    /// Tick order: waves → statuses → hero → familiars → enemies → projectiles → zones → scheduled → triggers → cleanup.
    /// Presentation reads <see cref="Events"/> and unit positions; it never feeds back into the simulation.
    /// </summary>
    public sealed partial class Battle
    {
        public const int ArenaLeft = 0;
        public const int ArenaRight = 18000;
        public const int HeroStartX = 3000;
        public const int DotInterval = 15;
        public const int LowHpPermille = 350;

        public readonly BattleSetup Setup;
        private readonly ContentDatabase _db;
        private readonly DetRandom _rng;
        public int Tick { get; private set; }
        public readonly List<Unit> Units = new List<Unit>();
        public Unit Hero { get; private set; }
        public readonly List<Projectile> Projectiles = new List<Projectile>();
        public readonly List<Zone> Zones = new List<Zone>();
        public readonly List<CombatEvent> Events = new List<CombatEvent>();
        public readonly BattleReport Report = new BattleReport();
        public WeaponRuntime[] Weapons { get; private set; }
        public readonly List<CompiledLink> Links;
        private readonly List<RuneRuntime> _runes = new List<RuneRuntime>();
        private readonly List<Scheduled> _scheduled = new List<Scheduled>();
        private readonly List<TriggerEvent> _triggers = new List<TriggerEvent>();
        private int _seq;
        private int _waveIndex;
        private int _waveTimer;
        private int _nextUnitId = 1;
        private int _nextProjectileId = 1;
        private int _nextZoneId = 1;
        private int _goldBonus;
        private int _leechBudget;
        private int _procsThisTick;
        private bool _lowHpFired;
        private int _enrageLevel;
        public bool Finished { get; private set; }
        public BattleResult Result { get; private set; }

        public Battle(BattleSetup setup)
        {
            Setup = setup;
            _db = setup.Db;
            _rng = new DetRandom(setup.Seed);
            Links = setup.Hero.Links;
            Init();
        }

        public static BattleResult RunToEnd(BattleSetup setup)
        {
            var b = new Battle(setup);
            while (!b.Finished) b.Step();
            return b.Result;
        }

        // ------------------------------------------------------------------ setup

        private void Init()
        {
            var h = Setup.Hero;
            var stats = h.Stats;
            Hero = new Unit
            {
                Id = _nextUnitId++,
                Team = 0,
                IsHero = true,
                MaxHp = Math.Max(1, h.MaxHp),
                Hp = Math.Max(1, Math.Min(h.Hp, h.MaxHp)),
                Armor = h.Armor + stats.SumUntagged(StatType.Armor),
                Evasion = stats.SumUntagged(StatType.Evasion),
                X = HeroStartX,
                Radius = 450,
                Speed = FMath.Permille(h.SpeedMilli, 1000 + stats.SumUntagged(StatType.MoveSpeed)),
                Rank = EnemyRank.Minion
            };
            int resistAll = stats.SumUntagged(StatType.ResistAll);
            for (int i = 0; i < Hero.Resist.Length; i++) Hero.Resist[i] = resistAll;
            Hero.ThornsPermille = stats.SumUntagged(StatType.Thorns);
            Hero.RegenPerSecond = stats.SumUntagged(StatType.Regen);
            Hero.Shield = stats.SumUntagged(StatType.ShieldOnStart);
            Units.Add(Hero);
            Report.DeadLinks.AddRange(Setup.DeadLinks);
            Report.DeadLinks.AddRange(Setup.Hero.DeadLinks);

            Weapons = new WeaponRuntime[h.Weapons.Count];
            for (int i = 0; i < h.Weapons.Count; i++)
            {
                Weapons[i] = new WeaponRuntime { Spec = h.Weapons[i], Index = i, Cooldown = 10 + i * 7 };
                foreach (var r in h.Weapons[i].Runes) AddRune(r, i);
            }
            foreach (var r in h.HeroRunes) AddRune(r, -1);

            int firstStrike = stats.SumUntagged(StatType.FirstStrike);
            if (firstStrike > 0) foreach (var w in Weapons) w.Cooldown = 0;

            if (Setup.Encounter != null && Setup.Encounter.Waves.Count > 0) SpawnWave(0);
            Raise(new TriggerEvent { Type = TriggerType.OnBattleStart, Weapon = -1, SourceUnit = Hero.Id, TargetUnit = -1, OriginRune = -1 });
            ProcessTriggers();
        }

        private void AddRune(RuneDef def, int weapon)
        {
            _runes.Add(new RuneRuntime { Def = def, Weapon = weapon, Timer = def.TimerTicks, Key = def.Id });
        }

        private void SpawnWave(int index)
        {
            _waveIndex = index;
            _waveTimer = 0;
            var wave = Setup.Encounter.Waves[index];
            Emit(CombatEventType.WaveStart, 0, 0, index, Setup.Encounter.Waves.Count, -1, 0);
            int slot = 0;
            foreach (var sp in wave.Spawns)
            {
                var def = _db.Enemy(sp.Enemy);
                if (def == null) continue;
                for (int i = 0; i < sp.Count; i++)
                {
                    int x = ArenaRight - 800 - slot * 650;
                    if (x < HeroStartX + 5000) x = HeroStartX + 5000 + (slot % 5) * 300;
                    SpawnEnemy(def, x, -1);
                    slot++;
                }
            }
            foreach (var w in Weapons) w.AmbushReady = true;
            Raise(new TriggerEvent { Type = TriggerType.OnWaveStart, Weapon = -1, SourceUnit = Hero.Id, TargetUnit = -1, OriginRune = -1 });
        }

        public Unit SpawnEnemy(EnemyDef def, int x, int summonedBy)
        {
            var diff = Setup.Difficulty;
            int hpMul = 1000 + diff.SumUntagged(StatType.EnemyHp);
            int chapterScale = 1000 + (Setup.Chapter - 1) * 0;   // chapter scaling is authored per-encounter, not HP sponge
            var u = new Unit
            {
                Id = _nextUnitId++,
                Team = 1,
                Def = def,
                MaxHp = Math.Max(1, FMath.Permille(FMath.Permille(def.MaxHp, hpMul), chapterScale)),
                Armor = def.Armor,
                Shield = def.Shield,
                Evasion = def.Evasion,
                X = FMath.Clamp(x, ArenaLeft + 500, ArenaRight - 300),
                Radius = def.RadiusMilli,
                Speed = FMath.Permille(def.SpeedMilli, 1000 + diff.SumUntagged(StatType.EnemySpeed)),
                Flying = def.Flying,
                Threat = def.Threat,
                Rank = def.Rank,
                DamagePermille = 1000 + diff.SumUntagged(StatType.EnemyDamage),
                SpawnOrder = _nextUnitId,
                SummonedBy = summonedBy,
                AttackCooldown = def.AttackCooldownTicks / 2 + _rng.Next(10)
            };
            u.Hp = u.MaxHp;
            foreach (var kv in def.Resist) u.Resist[(int)kv.Key] = kv.Value;
            u.ActiveAbilities.AddRange(def.Abilities);
            if (def.Phases.Count > 0) EnterPhase(u, 0, false);
            ResetAbilityTimers(u);
            foreach (var ab in u.ActiveAbilities)
            {
                if (ab.Type == EnemyAbilityType.Stealth) u.Stealthed = true;
                if (ab.Type == EnemyAbilityType.ReflectProjectiles) { u.Reflects = true; u.ReflectChance = ab.Permille; }
                if (ab.Type == EnemyAbilityType.Thorns) u.ThornsPermille = ab.Permille;
                if (ab.Type == EnemyAbilityType.Regen) u.RegenPerSecond = ab.Value;
                if (ab.Type == EnemyAbilityType.Barrier) u.BarrierHp = ab.Value;
                if (ab.Type == EnemyAbilityType.Burrow) u.Burrowed = true;
            }
            Units.Add(u);
            Report.EnemiesSpawned++;
            Emit(CombatEventType.Spawn, u.Id, summonedBy, 0, 0, -1, u.X, def.Id);
            return u;
        }

        private void ResetAbilityTimers(Unit u)
        {
            u.AbilityTimers = new int[u.ActiveAbilities.Count];
            for (int i = 0; i < u.ActiveAbilities.Count; i++)
            {
                var ab = u.ActiveAbilities[i];
                u.AbilityTimers[i] = ab.InitialDelayTicks > 0 ? ab.InitialDelayTicks : ab.CooldownTicks / 2 + _rng.Next(15);
            }
        }

        // ------------------------------------------------------------------ main loop

        public void Step()
        {
            if (Finished) return;
            Tick++;
            _procsThisTick = 0;
            if (Tick % FMath.TicksPerSecond == 1) _leechBudget = Math.Max(1, Hero.MaxHp * 40 / 1000);

            UpdateWaves();
            UpdateStatuses();
            UpdateHero();
            UpdateFamiliars();
            UpdateEnemies();
            UpdateProjectiles();
            UpdateZones();
            UpdateScheduled();
            UpdateRuneTimers();
            UpdateSignatures();
            ProcessTriggers();
            Cleanup();
            TrackAnalytics();
            CheckEnd();
        }

        private void UpdateWaves()
        {
            if (Setup.Encounter == null) return;
            _waveTimer++;
            int next = _waveIndex + 1;
            if (next >= Setup.Encounter.Waves.Count) return;
            bool cleared = CountAliveEnemies() == 0;
            if (cleared || _waveTimer >= Setup.Encounter.Waves[next].DelayTicks && Setup.Encounter.Waves[next].DelayTicks > 0)
                SpawnWave(next);
        }

        public int CountAliveEnemies()
        {
            int n = 0;
            for (int i = 0; i < Units.Count; i++) if (Units[i].Team == 1 && Units[i].Alive) n++;
            return n;
        }

        private void CheckEnd()
        {
            if (!Hero.Alive)
            {
                Finish(false, false, "hero_defeated");
                return;
            }
            bool lastWave = Setup.Encounter == null || _waveIndex >= Setup.Encounter.Waves.Count - 1;
            if (!Setup.DummyMode && lastWave && CountAliveEnemies() == 0)
            {
                Finish(true, false, null);
                return;
            }
            if (Tick >= Setup.MaxTicks)
            {
                if (Setup.DummyMode) Finish(true, false, null);
                else Finish(false, true, "timeout");
            }
        }

        private void Finish(bool victory, bool timeout, string reason)
        {
            Finished = true;
            Report.Ticks = Tick;
            if (!victory && reason == "hero_defeated" && Report.KilledBy == null) Report.KilledBy = "unknown";
            BuildInsights();
            Result = new BattleResult
            {
                Victory = victory,
                Timeout = timeout,
                Ticks = Tick,
                HeroHp = Math.Max(0, Hero.Hp),
                GoldBonus = _goldBonus,
                Report = Report,
                DeathReason = reason
            };
            Emit(CombatEventType.BattleEnd, Hero.Id, 0, victory ? 1 : 0, 0, -1, Hero.X);
        }

        private void TrackAnalytics()
        {
            int units = 0;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (!u.Alive) continue;
                units++;
                if (u.Team != 1) continue;
                for (int s = 1; s < (int)StatusType.Count; s++) if (u.Status.Stacks[s] > 0) Report.StatusUptimeTicks[s]++;
            }
            if (units > Report.PeakUnits) Report.PeakUnits = units;
            if (Projectiles.Count > Report.PeakProjectiles) Report.PeakProjectiles = Projectiles.Count;
            if (Zones.Count > Report.PeakZones) Report.PeakZones = Zones.Count;
            if (_procsThisTick > Report.MaxProcsInOneTick) Report.MaxProcsInOneTick = _procsThisTick;
        }

        private void Cleanup()
        {
            for (int i = Projectiles.Count - 1; i >= 0; i--) if (Projectiles[i].Dead) Projectiles.RemoveAt(i);
            for (int i = Zones.Count - 1; i >= 0; i--) if (Zones[i].Dead) Zones.RemoveAt(i);
            // Dead units are kept one second for presentation then removed (stable order preserved).
            for (int i = Units.Count - 1; i >= 0; i--)
            {
                var u = Units[i];
                if (u.State == UnitState.Dead && !u.IsHero && u.StateTimer++ > FMath.TicksPerSecond) Units.RemoveAt(i);
            }
            // Soft limit: long fights escalate enemy damage so stalemates resolve (readable "Enrage" event).
            if (Setup.Encounter != null && !Setup.DummyMode && Tick > Setup.Encounter.SoftLimitTicks)
            {
                int level = (Tick - Setup.Encounter.SoftLimitTicks) / (10 * FMath.TicksPerSecond) + 1;
                if (level > _enrageLevel)
                {
                    _enrageLevel = level;
                    Emit(CombatEventType.Enrage, 0, 0, level, 0, -1, 0);
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        public void Emit(CombatEventType type, int a, int b, int value, int value2, int weapon, int x, string tag = null, bool crit = false, bool proc = false)
        {
            if (!Setup.RecordEvents) return;
            Events.Add(new CombatEvent { Tick = Tick, Type = type, A = a, B = b, Value = value, Value2 = value2, Weapon = weapon, X = x, Tag = tag, Crit = crit, Proc = proc });
        }

        public Unit FindUnit(int id)
        {
            for (int i = 0; i < Units.Count; i++) if (Units[i].Id == id) return Units[i];
            return null;
        }

        private void Raise(TriggerEvent e) { _triggers.Add(e); }

        private void Schedule(Scheduled s)
        {
            s.Seq = _seq++;
            _scheduled.Add(s);
        }

        private static int Dist(Unit a, Unit b) { return FMath.Abs(a.X - b.X); }

        /// <summary>Gap between two unit edges (0 when overlapping).</summary>
        private static int Gap(Unit a, Unit b) { return Math.Max(0, FMath.Abs(a.X - b.X) - a.Radius - b.Radius); }

        private int EnemyOutgoingPermille(Unit e)
        {
            int p = e.DamagePermille + _enrageLevel * 100;
            if (e.Status.Has(StatusType.Weaken)) p = p * 750 / 1000;
            return p;
        }

        // ------------------------------------------------------------------ targeting

        private bool CanWeaponHit(WeaponSpec w, Unit u, bool melee)
        {
            if (!u.Targetable || u.Team != 1) return false;
            if (melee && u.Flying && !w.Tags.Contains("reach") && u.State != UnitState.Windup) return false;
            return true;
        }

        /// <summary>Select the primary target for a weapon. Tie-breaks are deterministic: distance, then unit id.</summary>
        public Unit SelectTarget(WeaponSpec w, TargetPriority prio, int maxRange, bool melee)
        {
            Unit best = null;
            long bestScore = long.MaxValue;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (!CanWeaponHit(w, u, melee)) continue;
                int gap = Gap(Hero, u);
                if (gap > maxRange) continue;
                long score;
                switch (prio)
                {
                    case TargetPriority.LowestHp: score = (long)u.Hp * 100000 + gap; break;
                    case TargetPriority.HighestThreat: score = -(long)(u.Threat + (u.IsBoss ? 100 : 0) + (u.IsElite ? 10 : 0)) * 100000 + gap; break;
                    case TargetPriority.Cluster: score = -(long)CountNear(u, 1500) * 100000 + gap; break;
                    case TargetPriority.Marked: score = (u.Status.Has(StatusType.Mark) ? 0 : 1000000000L) + gap; break;
                    case TargetPriority.Farthest: score = -gap; break;
                    default: score = gap; break;
                }
                score = score * 4096 + (u.Id & 4095);
                if (score < bestScore) { bestScore = score; best = u; }
            }
            return best;
        }

        private int CountNear(Unit center, int radius)
        {
            int n = 0;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (u.Team == center.Team && u.Alive && FMath.Abs(u.X - center.X) <= radius) n++;
            }
            return n;
        }

        public Unit NearestEnemyTo(int x, int excludeId, List<int> excludeList = null, int maxDist = int.MaxValue)
        {
            Unit best = null;
            int bestD = int.MaxValue;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (u.Team != 1 || !u.Targetable || u.Id == excludeId) continue;
                if (excludeList != null && excludeList.Contains(u.Id)) continue;
                int d = FMath.Abs(u.X - x);
                if (d > maxDist) continue;
                if (d < bestD || (d == bestD && best != null && u.Id < best.Id)) { bestD = d; best = u; }
            }
            return best;
        }

        public Unit NearestEnemy() { return NearestEnemyTo(Hero.X, -1); }

        /// <summary>Nearest enemy that blocks the hero's path regardless of targetability (for spacing).</summary>
        private Unit FrontEnemy()
        {
            Unit best = null;
            int bestD = int.MaxValue;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (u.Team != 1 || !u.Alive || u.Burrowed) continue;
                int d = FMath.Abs(u.X - Hero.X);
                if (d < bestD) { bestD = d; best = u; }
            }
            return best;
        }
    }
}
