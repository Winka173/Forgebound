using System;
using System.Collections.Generic;
using Forgebound.Core.Content;
using Forgebound.Core.Simulation;
using Forgebound.Core.Weapons;

namespace Forgebound.Core.Combat
{
    [Flags]
    public enum HitFlags
    {
        None = 0,
        Secondary = 1,     // explosion / chain / splash: no further impact-stage spawns
        Dot = 2,           // damage over time: no crit, no evasion, no OnHit
        NoCrit = 4,
        IgnoreArmor = 8,
        NoStatus = 16
    }

    public sealed partial class Battle
    {
        private int _kiteDir;
        /// <summary>Enemy DoTs on the hero tick once per second and cap at 3 stacks (readable, counterable pressure).</summary>
        public const int HeroDotInterval = 30;
        public const int HeroDotStackCap = 3;

        // ------------------------------------------------------------------ hero

        private bool IsDisabled(Unit u)
        {
            return u.Status.Has(StatusType.Stun) || u.Status.Has(StatusType.Freeze);
        }

        private int WeaponSpeedPermille(WeaponRuntime w)
        {
            int p = 1000;
            p += Hero.Status.Get(StatusType.Haste) * 100;
            if (w.Spec.TempoPerAttack > 0) p += Math.Min(w.Tempo, Math.Max(w.Spec.TempoMax, w.Spec.TempoPerAttack));
            if (w.Spec.FrenzyAttackSpeed > 0 && Hero.Hp * 1000 < Hero.MaxHp * 500) p += w.Spec.FrenzyAttackSpeed;
            if (w.Spec.Signature == LegendarySignature.Overclock) p += Math.Min(w.Heat * 25, 1500);
            if (Hero.Status.Has(StatusType.Chill)) p -= Hero.Status.Get(StatusType.Chill) * 80;
            if (Hero.Status.Has(StatusType.Slow)) p -= 300;
            return Math.Max(250, p);
        }

        private void UpdateHero()
        {
            if (!Hero.Alive) return;
            if (Tick % FMath.TicksPerSecond == 0 && Hero.RegenPerSecond > 0) HealHero(Hero.RegenPerSecond, -1, false);
            if (Tick % FMath.TicksPerSecond == 0 && Hero.Shield > 0 && _db.Tuning.ShieldDecayPermille > 0)
                Hero.Shield = Math.Max(0, Hero.Shield - Math.Max(1, FMath.Permille(Hero.Shield, _db.Tuning.ShieldDecayPermille)));
            if (Tick % FMath.TicksPerSecond == 0 && Hero.Status.Has(StatusType.Regen)) HealHero(Math.Max(1, Hero.MaxHp * 15 / 1000) * Hero.Status.Get(StatusType.Regen), -1, false);

            for (int i = 0; i < Weapons.Length; i++)
            {
                var w = Weapons[i];
                if (w.WindupLeft <= 0 && w.Cooldown > 0) w.Cooldown -= WeaponSpeedPermille(w);
            }

            if (IsDisabled(Hero))
            {
                // Disabled: windups are interrupted, no movement.
                for (int i = 0; i < Weapons.Length; i++) { Weapons[i].WindupLeft = 0; Weapons[i].PendingTarget = -1; }
                Hero.State = UnitState.Stunned;
                return;
            }

            if (Weapons.Length > 0) MoveHero(Weapons[0]);

            for (int i = 0; i < Weapons.Length; i++) UpdateWeapon(Weapons[i]);
        }

        private void MoveHero(WeaponRuntime primary)
        {
            if (primary.WindupLeft > 0) { Hero.State = UnitState.Windup; return; }
            if (HeroInDanger() && Hero.X > ArenaLeft + 700)
            {
                // Read the telegraph and step out of the impact area (auto-combat counterplay, no player input).
                Hero.X = Math.Max(ArenaLeft + 400, Hero.X - Hero.Speed * 3 / 4);
                Hero.MoveDir = -1;
                Hero.State = UnitState.Moving;
                return;
            }
            var front = FrontEnemy();
            if (front == null) { Hero.State = UnitState.Idle; _kiteDir = 0; return; }
            int gap = Gap(Hero, front);
            int range = primary.Spec.RangeMilli;
            int minRange = primary.Spec.MinRangeMilli;
            int dir = 0;
            if (_kiteDir < 0)
            {
                // Hysteresis: once kiting, keep retreating until comfortably outside min range.
                if (gap < minRange + 700 && Hero.X > ArenaLeft + 700) dir = -1; else _kiteDir = 0;
            }
            if (dir == 0)
            {
                if (minRange > 0 && gap < minRange && Hero.X > ArenaLeft + 700) { dir = -1; _kiteDir = -1; }
                else if (gap > range - 250) dir = 1;
            }
            if (dir == 0) { Hero.State = UnitState.Idle; return; }
            int speed = Hero.Speed;
            if (Hero.Status.Has(StatusType.Slow)) speed = speed * 700 / 1000;
            if (Hero.Status.Has(StatusType.Chill)) speed = speed * (1000 - Hero.Status.Get(StatusType.Chill) * 80) / 1000;
            if (dir < 0) speed = speed * 850 / 1000;
            int nx = Hero.X + dir * speed;
            if (dir > 0) nx = Math.Min(nx, front.X - front.Radius - Hero.Radius);
            Hero.X = FMath.Clamp(nx, ArenaLeft + 400, ArenaRight - 400);
            Hero.MoveDir = dir;
            Hero.State = UnitState.Moving;
        }

        private int StepWindup(WeaponRuntime w, AttackStep step)
        {
            int baseW = step.WindupTicks >= 0 ? step.WindupTicks : w.Spec.Frame.WindupTicks;
            return Math.Max(1, FMath.Permille(baseW, w.Spec.WindupPermille));
        }

        private void UpdateWeapon(WeaponRuntime w)
        {
            var spec = w.Spec;
            if (spec.Steps.Count == 0) return;
            var step = spec.Steps[w.StepIndex % spec.Steps.Count];
            if (w.WindupLeft > 0)
            {
                w.WindupLeft--;
                if (w.WindupLeft == 0) Release(w, step);
                return;
            }
            if (w.Cooldown > 0) return;
            bool melee = step.Kind == StepKind.Melee;
            var target = SelectTarget(spec, Setup.Hero.Targeting, spec.RangeMilli, melee);
            if (target == null) return;
            w.WindupLeft = StepWindup(w, step);
            w.WindupStep = w.StepIndex % spec.Steps.Count;
            w.PendingTarget = target.Id;
            Emit(CombatEventType.AttackWindup, Hero.Id, target.Id, w.WindupStep, w.WindupLeft, w.Index, Hero.X);
        }

        private void Release(WeaponRuntime w, AttackStep step)
        {
            var spec = w.Spec;
            bool melee = step.Kind == StepKind.Melee;
            var target = FindUnit(w.PendingTarget);
            if (target == null || !CanWeaponHit(spec, target, melee) || Gap(Hero, target) > spec.RangeMilli + 400)
                target = SelectTarget(spec, Setup.Hero.Targeting, spec.RangeMilli + 400, melee);
            w.PendingTarget = -1;
            if (target == null)
            {
                w.Cooldown = 6 * 1000;   // whiff: short retry delay, combo does not advance
                return;
            }
            int stepIndex = w.StepIndex % spec.Steps.Count;
            int power = 1000;
            if (spec.OverchargeEvery > 0 && (w.AttackCount + 1) % spec.OverchargeEvery == 0) power = FMath.Permille(power, spec.OverchargePermille);
            if (spec.AmbushPermille > 0 && w.AmbushReady) { power = FMath.Permille(power, 1000 + spec.AmbushPermille); w.AmbushReady = false; }
            if (spec.Signature == LegendarySignature.Bloodpact && Hero.Hp * 1000 > Hero.MaxHp * 300)
            {
                int cost = Math.Max(1, Hero.MaxHp * 15 / 1000);
                Hero.Hp -= cost;
                power = power * 2;
            }
            if (spec.Signature == LegendarySignature.Eclipse)
            {
                w.EclipseRadiant = !w.EclipseRadiant;
            }

            Emit(CombatEventType.AttackRelease, Hero.Id, target.Id, stepIndex, power, w.Index, Hero.X);
            PerformAttack(w, stepIndex, target, power, false, 0);
            Report.Weapons[w.Index].Attacks++;
            w.AttackCount++;
            w.StepIndex++;
            int cd = step.CooldownTicks >= 0 ? step.CooldownTicks : spec.CooldownTicks;
            w.Cooldown = Math.Max(4, cd) * 1000;
            if (spec.TempoPerAttack > 0) w.Tempo += spec.TempoPerAttack;
            if (spec.Signature == LegendarySignature.Overclock)
            {
                w.Heat++;
                if (w.Heat >= Math.Max(20, spec.SignatureValue)) { w.Heat = 0; w.Cooldown = 60 * 1000; Emit(CombatEventType.SignatureFire, Hero.Id, 0, (int)spec.Signature, 0, w.Index, Hero.X, "overheat"); }
            }

            for (int r = 0; r < spec.RepeatCount; r++)
                Schedule(new Scheduled { Tick = Tick + spec.RepeatDelayTicks * (r + 1), Kind = ScheduledKind.RepeatAttack, Weapon = w.Index, Power = spec.RepeatPermille, TargetId = target.Id, Step = stepIndex, ProcDepth = 1 });
            if (spec.TwinStrike)
                Schedule(new Scheduled { Tick = Tick + 5, Kind = ScheduledKind.TwinStrike, Weapon = w.Index, Power = 600, TargetId = target.Id, Step = stepIndex });
            for (int b = 0; b < spec.BarrageCount; b++)
                Schedule(new Scheduled { Tick = Tick + 4 * (b + 1), Kind = ScheduledKind.BarrageShot, Weapon = w.Index, Power = 400, TargetId = target.Id, Step = stepIndex });

            OnAttackSignatures(w, target);
            Raise(new TriggerEvent { Type = TriggerType.OnAttack, Weapon = w.Index, SourceUnit = Hero.Id, TargetUnit = target.Id, DamageType = spec.DamageType, OriginRune = -1 });
        }

        /// <summary>Execute one attack step. Used by normal attacks, repeats, echo procs and rune ExtraAttack.</summary>
        public void PerformAttack(WeaponRuntime w, int stepIndex, Unit target, int powerPermille, bool isProc, int depth)
        {
            var spec = w.Spec;
            var step = spec.Steps[stepIndex % spec.Steps.Count];
            int dmg = FMath.Permille(FMath.Permille(spec.BaseDamage, step.DamagePermille), powerPermille);
            if (isProc) dmg = FMath.Permille(dmg, spec.ProcPermille);
            dmg = Math.Max(1, dmg);
            var flags = isProc ? HitFlags.None : HitFlags.None;

            switch (step.Kind)
            {
                case StepKind.Melee:
                case StepKind.Beam:
                    {
                        var targets = GatherShapeTargets(spec, step, target);
                        for (int t = 0; t < targets.Count; t++)
                            for (int h = 0; h < step.Hits; h++)
                                HitUnit(w, targets[t], dmg, isProc, depth, step, flags);
                        if (spec.ShockwavePermille > 0 && step.Kind == StepKind.Melee)
                            SpawnWave(w, FMath.Permille(dmg, spec.ShockwavePermille), isProc, depth, 12);
                        break;
                    }
                case StepKind.Projectile:
                    {
                        int count = 1 + spec.ProjectileAdd + Math.Max(0, step.Hits - 1);
                        int per = count > 1 ? FMath.Permille(dmg, spec.SplitDamagePermille) : dmg;
                        var targets = ProjectileTargets(spec, target, count);
                        for (int i = 0; i < count; i++)
                        {
                            var t = targets[i % targets.Count];
                            FireProjectile(w, t, Math.Max(1, per), isProc, depth, step, i);
                        }
                        Report.Weapons[w.Index].ProjectilesFired += count;
                        if (!isProc) Raise(new TriggerEvent { Type = TriggerType.OnProjectileFired, Weapon = w.Index, SourceUnit = Hero.Id, TargetUnit = target.Id, Amount = count, OriginRune = -1 });
                        break;
                    }
                case StepKind.Nova:
                    {
                        int radius = Math.Max(800, step.WidthMilli + spec.ArcAddMilli);
                        int hit = 0;
                        int maxT = step.MaxTargets + spec.MaxTargetsAdd;
                        for (int i = 0; i < Units.Count && hit < Math.Max(1, maxT); i++)
                        {
                            var u = Units[i];
                            if (u.Team != 1 || !u.Targetable || FMath.Abs(u.X - target.X) > radius + u.Radius) continue;
                            HitUnit(w, u, dmg, isProc, depth, step, flags);
                            hit++;
                        }
                        Emit(CombatEventType.ZoneSpawn, Hero.Id, target.Id, radius, 0, w.Index, target.X, "nova");
                        break;
                    }
                case StepKind.Summon:
                    {
                        int cap = step.SummonCap + spec.SummonCapAdd;
                        if (w.SummonsAlive < cap && CountFamiliars() < Setup.MaxFamiliars) SpawnFamiliar(w, dmg);
                        else
                        {
                            // At cap the tome empowers its familiars and refreshes them instead of idling.
                            for (int i = 0; i < Units.Count; i++)
                            {
                                var u = Units[i];
                                if (u.IsFamiliar && u.Alive && u.OwnerWeapon == w.Index) { u.LifeTicks = Math.Max(u.LifeTicks, 300); u.FamiliarDamage += Math.Max(1, dmg / 10); }
                            }
                        }
                        Raise(new TriggerEvent { Type = TriggerType.OnSummon, Weapon = w.Index, SourceUnit = Hero.Id, TargetUnit = target.Id, OriginRune = -1, IsProc = isProc, ProcDepth = depth });
                        break;
                    }
            }
        }

        private readonly List<Unit> _shapeBuf = new List<Unit>(16);

        private List<Unit> GatherShapeTargets(WeaponSpec spec, AttackStep step, Unit main)
        {
            _shapeBuf.Clear();
            bool melee = step.Kind == StepKind.Melee;
            int maxTargets = Math.Max(1, step.MaxTargets + spec.MaxTargetsAdd);
            if (step.Shape == HitShape.Single && spec.MaxTargetsAdd <= 0)
            {
                _shapeBuf.Add(main);
                return _shapeBuf;
            }
            int reach = spec.RangeMilli + step.WidthMilli + spec.ArcAddMilli;
            _shapeBuf.Add(main);
            // Collect additional targets in front of the hero ordered by distance (deterministic).
            while (_shapeBuf.Count < maxTargets)
            {
                Unit best = null;
                int bestD = int.MaxValue;
                for (int i = 0; i < Units.Count; i++)
                {
                    var u = Units[i];
                    if (_shapeBuf.Contains(u) || !CanWeaponHit(spec, u, melee)) continue;
                    int d = u.X - Hero.X;
                    if (step.Shape == HitShape.Around) d = FMath.Abs(d);
                    else if (d < -Hero.Radius) continue;
                    if (FMath.Abs(d) - u.Radius > reach) continue;
                    if (step.Shape == HitShape.Arc && FMath.Abs(u.X - main.X) > Math.Max(1200, step.WidthMilli + spec.ArcAddMilli)) continue;
                    int ad = FMath.Abs(d);
                    if (ad < bestD || (ad == bestD && best != null && u.Id < best.Id)) { bestD = ad; best = u; }
                }
                if (best == null) break;
                _shapeBuf.Add(best);
            }
            return _shapeBuf;
        }

        private List<Unit> ProjectileTargets(WeaponSpec spec, Unit main, int count)
        {
            var list = new List<Unit>(count) { main };
            if (count <= 1) return list;
            int range = spec.RangeMilli + 1500;
            if (spec.Scatter)
            {
                var pool = new List<Unit>();
                for (int i = 0; i < Units.Count; i++) if (CanWeaponHit(spec, Units[i], false) && Gap(Hero, Units[i]) <= range && Units[i] != main) pool.Add(Units[i]);
                while (list.Count < count && pool.Count > 0)
                {
                    int k = _rng.Next(pool.Count);
                    list.Add(pool[k]);
                    pool.RemoveAt(k);
                }
                return list;
            }
            var exclude = new List<int> { main.Id };
            while (list.Count < count)
            {
                var n = NearestEnemyTo(main.X, -1, exclude, range);
                if (n == null) break;
                list.Add(n);
                exclude.Add(n.Id);
            }
            return list;
        }

        private void FireProjectile(WeaponRuntime w, Unit target, int dmg, bool isProc, int depth, AttackStep step, int index)
        {
            if (Projectiles.Count >= Setup.MaxProjectiles) { Report.ProcsSuppressedBudget++; return; }
            var spec = w.Spec;
            int speed = step.ProjectileSpeed > 0 ? step.ProjectileSpeed : spec.ProjectileSpeed;
            var p = new Projectile
            {
                Id = _nextProjectileId++,
                Team = 0,
                X = Hero.X + 300,
                Dir = target.X >= Hero.X ? 1 : -1,
                Speed = Math.Max(100, speed),
                TargetId = target.Id,
                AimX = target.X,
                Homing = spec.Homing,
                Damage = dmg,
                Type = spec.DamageType,
                Weapon = w.Index,
                IsProc = isProc,
                ProcDepth = depth,
                PierceLeft = spec.Pierce + step.Pierce,
                RicochetLeft = spec.Ricochet,
                Boomerang = spec.Boomerang,
                SourceUnit = Hero.Id,
                StepIndex = spec.Steps.IndexOf(step)
            };
            Projectiles.Add(p);
            Emit(CombatEventType.ProjectileSpawn, p.Id, target.Id, dmg, (int)p.Type, w.Index, p.X, null, false, isProc);
        }

        private void SpawnWave(WeaponRuntime w, int dmg, bool isProc, int depth, int life)
        {
            if (Projectiles.Count >= Setup.MaxProjectiles) { Report.ProcsSuppressedBudget++; return; }
            var p = new Projectile
            {
                Id = _nextProjectileId++,
                Team = 0,
                X = Hero.X + Hero.Radius,
                Dir = 1,
                Speed = 260,
                TargetId = -1,
                AimX = ArenaRight,
                Damage = Math.Max(1, dmg),
                Type = w.Spec.DamageType,
                Weapon = w.Index,
                IsProc = isProc,
                ProcDepth = depth,
                LifeTicks = life,
                Wave = true,
                SourceUnit = Hero.Id
            };
            Projectiles.Add(p);
            Emit(CombatEventType.ProjectileSpawn, p.Id, -1, p.Damage, (int)p.Type, w.Index, p.X, "wave", false, isProc);
        }

        private int CountFamiliars()
        {
            int n = 0;
            for (int i = 0; i < Units.Count; i++) if (Units[i].IsFamiliar && Units[i].Alive) n++;
            return n;
        }

        private void SpawnFamiliar(WeaponRuntime w, int dmg)
        {
            int power = 1000 + Setup.Hero.Stats.SumUntagged(StatType.SummonPower);
            var f = new Unit
            {
                Id = _nextUnitId++,
                Team = 0,
                IsFamiliar = true,
                MaxHp = Math.Max(8, Hero.MaxHp / 10),
                X = Hero.X + 600,
                Radius = 300,
                Speed = 90,
                OwnerWeapon = w.Index,
                LifeTicks = 9 * FMath.TicksPerSecond,
                FamiliarDamage = Math.Max(1, FMath.Permille(dmg, 700 * power / 1000)),
                AttackCooldown = 10
            };
            f.Hp = f.MaxHp;
            Units.Add(f);
            w.SummonsAlive++;
            Emit(CombatEventType.Summon, Hero.Id, f.Id, w.Index, 0, w.Index, f.X, w.Spec.Frame.Id);
        }

        private void UpdateFamiliars()
        {
            for (int i = 0; i < Units.Count; i++)
            {
                var f = Units[i];
                if (!f.IsFamiliar || !f.Alive) continue;
                if (--f.LifeTicks <= 0) { KillFamiliar(f); continue; }
                if (IsDisabled(f)) continue;
                var t = NearestEnemyTo(f.X, -1);
                if (t == null) continue;
                int gap = Gap(f, t);
                if (gap > 700) { f.X += FMath.Sign(t.X - f.X) * f.Speed; f.State = UnitState.Moving; continue; }
                if (f.AttackCooldown > 0) { f.AttackCooldown--; continue; }
                f.AttackCooldown = 30;
                var w = f.OwnerWeapon >= 0 && f.OwnerWeapon < Weapons.Length ? Weapons[f.OwnerWeapon] : null;
                if (w == null) continue;
                Emit(CombatEventType.AttackRelease, f.Id, t.Id, 0, 0, w.Index, f.X, "familiar");
                HitUnit(w, t, f.FamiliarDamage, false, 0, null, HitFlags.Secondary);
            }
        }

        private void KillFamiliar(Unit f)
        {
            f.State = UnitState.Dead;
            f.StateTimer = 0;
            if (f.OwnerWeapon >= 0 && f.OwnerWeapon < Weapons.Length) Weapons[f.OwnerWeapon].SummonsAlive--;
            Emit(CombatEventType.Death, f.Id, 0, 0, 0, f.OwnerWeapon, f.X, "familiar");
        }

        // ------------------------------------------------------------------ hit + damage pipeline

        /// <summary>
        /// A weapon hit lands on a unit: damage pipeline, then on-hit riders (status, stagger, armor break,
        /// knockback/pull), then impact-stage spawns (explosion, delayed, lingering, chain) unless Secondary.
        /// </summary>
        public int HitUnit(WeaponRuntime w, Unit target, int dmg, bool isProc, int depth, AttackStep step, HitFlags flags)
        {
            if (target == null || !target.Alive) return 0;
            var spec = w.Spec;
            int amount = dmg;
            // Conditional bonuses (target stage).
            if (spec.FocusBonusPermille > 0 && (target.IsBoss || target.IsElite)) amount = FMath.Permille(amount, 1000 + spec.FocusBonusPermille);
            if (spec.BonusVsStatus != StatusType.None && target.Status.Has(spec.BonusVsStatus)) amount = FMath.Permille(amount, 1000 + spec.BonusVsStatusPermille);
            if (spec.ShatterBonusPermille > 0 && target.Status.Has(StatusType.Freeze))
            {
                amount = FMath.Permille(amount, 1000 + spec.ShatterBonusPermille);
                target.Status.Clear(StatusType.Freeze);
                target.Status.FreezeImmuneTicks = 60;
                Emit(CombatEventType.StatusExpired, Hero.Id, target.Id, (int)StatusType.Freeze, 1, w.Index, target.X, "shatter");
            }
            if (spec.ExecuteBelowPermille > 0 && target.Hp * 1000 < target.MaxHp * spec.ExecuteBelowPermille) amount = FMath.Permille(amount, spec.ExecuteMultPermille);
            if (spec.Signature == LegendarySignature.Rootbind && target.Status.Has(StatusType.Slow)) amount = FMath.Permille(amount, 1000 + Math.Max(300, spec.SignatureValue));
            if (spec.Signature == LegendarySignature.SoulHarvest) amount = FMath.Permille(amount, 1000 + Math.Min(w.Souls, 30) * 40);
            if (spec.Signature == LegendarySignature.Eclipse)
            {
                bool radiantTurn = w.EclipseRadiant;
                if (radiantTurn && target.Status.Has(StatusType.Curse)) amount = FMath.Permille(amount, 1500);
                if (!radiantTurn && target.Status.Has(StatusType.Mark)) amount = FMath.Permille(amount, 1500);
            }
            if (target.Flying && step != null && step.Kind == StepKind.Melee && !spec.Tags.Contains("reach")) amount = FMath.Permille(amount, 700);
            var stats = Setup.Hero.Stats;
            if (target.IsElite) amount = FMath.Permille(amount, 1000 + stats.SumUntagged(StatType.DamageVsElite));
            if (target.IsBoss) amount = FMath.Permille(amount, 1000 + stats.SumUntagged(StatType.DamageVsBoss));
            if (Hero.Hp * 2 < Hero.MaxHp) amount = FMath.Permille(amount, 1000 + stats.SumUntagged(StatType.LowHpDamage));
            for (int s = 1; s < (int)StatusType.Count; s++)
            {
                if (target.Status.Stacks[s] <= 0) continue;
                int bonus = stats.Sum(StatType.DamageVsStatus, null, ((StatusType)s).ToString());
                if (bonus != 0) amount = FMath.Permille(amount, 1000 + bonus);
            }

            DamageType type = spec.DamageType;
            if (spec.Signature == LegendarySignature.Eclipse) type = w.EclipseRadiant ? DamageType.Radiant : DamageType.Shadow;
            bool crit;
            int dealt = DealDamage(Hero, target, amount, type, w.Index, isProc, depth, spec.CritChance, spec.CritDamage, spec.ArmorPen, flags, -1, out crit);
            if (dealt < 0) return 0; // missed

            if ((flags & HitFlags.NoStatus) == 0 && target.Alive)
            {
                if (spec.Status != null) ApplyStatus(target, spec.Status, w, isProc, depth, spec.StatusChanceAdd);
                if (step != null && step.Status != null) ApplyStatus(target, step.Status, w, isProc, depth, spec.StatusChanceAdd);
                if (spec.BleedChance > 0) ApplyStatus(target, new StatusApply { Status = StatusType.Bleed, ChancePermille = spec.BleedChance, Stacks = 1, DurationTicks = 120 }, w, isProc, depth, 0);
                int stagger = spec.StaggerChance + (step != null ? step.StaggerChance : 0);
                if (stagger > 0 && !target.IsBoss) ApplyStatus(target, new StatusApply { Status = StatusType.Stun, ChancePermille = stagger, Stacks = 1, DurationTicks = 12 }, w, isProc, depth, 0);
                else if (stagger > 0 && target.IsBoss && _rng.Chance(stagger / 3)) InterruptWindup(target);
                int ab = spec.ArmorBreakStacks + (step != null ? step.ArmorBreak : 0);
                if (ab > 0) ApplyStatus(target, new StatusApply { Status = StatusType.ArmorBreak, ChancePermille = 1000, Stacks = ab, DurationTicks = 180 }, w, isProc, depth, 0);
                if (spec.Signature == LegendarySignature.Rootbind) ApplyStatus(target, new StatusApply { Status = StatusType.Slow, ChancePermille = 1000, Stacks = 1, DurationTicks = 60 }, w, isProc, depth, 0);
                int kb = spec.KnockbackMilli;
                if (spec.Signature == LegendarySignature.Colossus) kb += 900;
                if (kb > 0 && !target.IsBoss) Push(target, kb);
                int pull = spec.PullMilli + (step != null ? step.PullMilli : 0);
                if (pull > 0 && !target.IsBoss) Push(target, -pull);
            }

            if (step != null && step.ShieldPermille > 0) GainShield(FMath.Permille(Math.Max(0, dealt), step.ShieldPermille) + 1, w.Index);

            if ((flags & HitFlags.Secondary) == 0)
            {
                if (spec.ExplosionRadius > 0)
                {
                    int splash = FMath.Permille(amount, spec.ExplosionPermille);
                    for (int i = 0; i < Units.Count; i++)
                    {
                        var u = Units[i];
                        if (u == target || u.Team != 1 || !u.Targetable) continue;
                        if (FMath.Abs(u.X - target.X) <= spec.ExplosionRadius + u.Radius) HitUnit(w, u, splash, isProc, depth, null, HitFlags.Secondary | HitFlags.NoStatus);
                    }
                    Emit(CombatEventType.ZoneSpawn, Hero.Id, target.Id, spec.ExplosionRadius, 0, w.Index, target.X, "explosion");
                }
                if (spec.DelayedDetonatePermille > 0) AddZone(ZoneKind.Delayed, target.X, 1300, 30, 30, FMath.Permille(amount, spec.DelayedDetonatePermille), type, w.Index, isProc, depth, null);
                if (spec.LingeringPermille > 0) AddZone(ZoneKind.Lingering, target.X, 1200, 90, 15, Math.Max(1, FMath.Permille(amount, spec.LingeringPermille)), type, w.Index, isProc, depth, spec.Status);
                if (spec.ChainCount > 0) ChainFrom(w, target, FMath.Permille(amount, spec.ChainPermille), spec.ChainCount, type, isProc, depth, 4000);
                if (spec.Signature == LegendarySignature.InfinitePierce) ChainFrom(w, target, FMath.Permille(amount, 350), 1, DamageType.Lightning, isProc, depth, 3000);
                if (spec.Signature == LegendarySignature.ShatterFrozen && target.Alive && target.Status.Has(StatusType.Chill) && target.Status.Get(StatusType.Chill) >= 3 && !target.IsBoss)
                {
                    int burst = target.MaxHp * Math.Max(80, spec.SignatureValue) / 1000;
                    bool c2;
                    DealDamage(Hero, target, burst, DamageType.Frost, w.Index, true, depth + 1, 0, 1000, 0, HitFlags.NoCrit | HitFlags.Secondary, -1, out c2);
                    Emit(CombatEventType.SignatureFire, Hero.Id, target.Id, (int)spec.Signature, burst, w.Index, target.X, "shatter");
                }
                if (spec.Signature == LegendarySignature.BladeStorm && !isProc)
                {
                    w.BladeStacks++;
                    int need = Math.Max(6, spec.SignatureValue);
                    if (w.BladeStacks >= need)
                    {
                        w.BladeStacks = 0;
                        for (int i = 0; i < Units.Count; i++)
                        {
                            var u = Units[i];
                            if (u.Team == 1 && u.Targetable && FMath.Abs(u.X - Hero.X) <= 4000) HitUnit(w, u, FMath.Permille(spec.BaseDamage, 900), true, depth + 1, null, HitFlags.Secondary);
                        }
                        Emit(CombatEventType.SignatureFire, Hero.Id, 0, (int)spec.Signature, 0, w.Index, Hero.X, "blade_storm");
                    }
                }
            }
            return dealt;
        }

        private void ChainFrom(WeaponRuntime w, Unit from, int dmg, int count, DamageType type, bool isProc, int depth, int range)
        {
            var hit = new List<int> { from.Id };
            var cur = from;
            for (int c = 0; c < count; c++)
            {
                var next = NearestEnemyTo(cur.X, -1, hit, range);
                if (next == null) break;
                hit.Add(next.Id);
                bool crit;
                Emit(CombatEventType.ProjectileHit, cur.Id, next.Id, dmg, (int)type, w.Index, next.X, "chain", false, true);
                DealDamage(Hero, next, Math.Max(1, dmg), type, w.Index, true, depth, w.Spec.CritChance, w.Spec.CritDamage, w.Spec.ArmorPen, HitFlags.Secondary, -1, out crit);
                if (w.Spec.Status != null && next.Alive) ApplyStatus(next, w.Spec.Status, w, true, depth, w.Spec.StatusChanceAdd);
                cur = next;
            }
        }

        private void Push(Unit u, int amount)
        {
            int dir = u.X >= Hero.X ? 1 : -1;
            int before = u.X;
            u.X = FMath.Clamp(u.X + dir * amount, Hero.X + Hero.Radius + u.Radius, ArenaRight - 300);
            if (u.X != before) Emit(CombatEventType.Knockback, Hero.Id, u.Id, u.X - before, 0, -1, u.X);
        }

        private void InterruptWindup(Unit u)
        {
            if (u.State == UnitState.Windup || u.State == UnitState.Ability)
            {
                u.State = UnitState.Idle;
                u.PendingAbility = -1;
                u.AttackCooldown = Math.Max(u.AttackCooldown, 20);
                Emit(CombatEventType.StatusApplied, Hero.Id, u.Id, (int)StatusType.Stun, 0, -1, u.X, "interrupt");
            }
        }

        /// <summary>
        /// Core damage pipeline (order documented in docs/COMBAT_RULES.md):
        /// evasion → crit → vulnerability (shock/curse/mark) → armor or resist → shield → HP → events/triggers → death.
        /// Returns damage dealt to HP+shield, or -1 on a miss.
        /// </summary>
        public int DealDamage(Unit src, Unit target, int amount, DamageType type, int weapon, bool isProc, int depth,
            int critChance, int critDamage, int armorPen, HitFlags flags, int originRune, out bool crit)
        {
            crit = false;
            if (target == null || !target.Alive || amount <= 0) return 0;
            if (originRune < 0) originRune = _activeOriginRune;
            bool dot = (flags & HitFlags.Dot) != 0;
            if (!dot && target.Evasion > 0 && _rng.Chance(target.Evasion))
            {
                Emit(CombatEventType.Miss, src != null ? src.Id : 0, target.Id, 0, 0, weapon, target.X);
                return -1;
            }
            int dmg = amount;
            if (!dot && (flags & HitFlags.NoCrit) == 0 && src != null && src.Team == 0)
            {
                int cc = critChance + (target.Status.Has(StatusType.Mark) ? 200 : 0);
                if (_rng.Chance(cc)) { crit = true; dmg = FMath.Permille(dmg, Math.Max(1000, critDamage)); }
            }
            // Vulnerabilities
            if (target.Status.Has(StatusType.Shock) && !dot)
            {
                dmg = FMath.Permille(dmg, 1000 + 150 * target.Status.Get(StatusType.Shock));
                target.Status.Stacks[(int)StatusType.Shock]--;
                if (target.Status.Stacks[(int)StatusType.Shock] <= 0) target.Status.Clear(StatusType.Shock);
            }
            if (target.Status.Has(StatusType.Curse)) dmg = FMath.Permille(dmg, 1000 + 60 * target.Status.Get(StatusType.Curse));
            if (target.Status.Has(StatusType.Fortify)) dmg = FMath.Permille(dmg, 750);
            if (target.Team == 0)
            {
                dmg = FMath.Permille(dmg, 1000 + Setup.Hero.Stats.SumUntagged(StatType.IncomingDamage));
            }

            // Mitigation
            int before = dmg;
            if (type == DamageType.Physical && (flags & HitFlags.IgnoreArmor) == 0)
            {
                int armor = Math.Max(0, target.Armor - target.Status.Get(StatusType.ArmorBreak) * 5 - armorPen);
                dmg = (int)((long)dmg * 100 / (100 + armor));
            }
            else if (type != DamageType.True)
            {
                int resist = FMath.Clamp(target.Resist[(int)type], -500, 900);
                if (target.Team == 1 && target.Status.Has(StatusType.ArmorBreak)) resist -= target.Status.Get(StatusType.ArmorBreak) * 20;
                dmg = (int)((long)dmg * (1000 - FMath.Clamp(resist, -500, 900)) / 1000);
            }
            dmg = Math.Max(1, dmg);
            if (target.Team == 1) Report.DamageMitigatedByArmor += Math.Max(0, before - dmg);

            // Shield
            int absorbed = 0;
            if (target.Shield > 0)
            {
                absorbed = Math.Min(target.Shield, dmg);
                target.Shield -= absorbed;
                dmg -= absorbed;
                if (target.Team == 0 && target.IsHero)
                {
                    Report.DamageBlockedByShield += absorbed;
                    Raise(new TriggerEvent { Type = TriggerType.OnBlock, Weapon = -1, SourceUnit = src != null ? src.Id : 0, TargetUnit = target.Id, Amount = absorbed, OriginRune = -1 });
                }
                if (target.Shield == 0 && target.Team == 1)
                {
                    Emit(CombatEventType.ShieldBreak, src != null ? src.Id : 0, target.Id, absorbed, 0, weapon, target.X);
                    Raise(new TriggerEvent { Type = TriggerType.OnShieldBreak, Weapon = weapon, SourceUnit = Hero.Id, TargetUnit = target.Id, IsProc = isProc, ProcDepth = depth, OriginRune = originRune });
                }
            }
            target.Hp -= dmg;
            int total = dmg + absorbed;
            Emit(CombatEventType.Damage, src != null ? src.Id : 0, target.Id, total, (int)type, weapon, target.X, null, crit, isProc);

            if (target.Team == 1)
            {
                target.LastHitWeapon = weapon;
                if (weapon >= 0 && weapon < Report.Weapons.Length)
                {
                    var ws = Report.Weapons[weapon];
                    ws.Damage += total;
                    if (dot) ws.DotDamage += total;
                    else
                    {
                        ws.Hits++;
                        if (isProc) ws.ProcDamage += total;
                        if (crit) ws.Crits++;
                    }
                }
                if (!dot && weapon >= 0 && weapon < Weapons.Length)
                {
                    var spec = Weapons[weapon].Spec;
                    if (spec.LeechPermille > 0 && !isProc) Leech(FMath.Permille(total, Math.Min(spec.LeechPermille, 250)));
                    if (spec.ShieldOnDamagePermille > 0) GainShield(FMath.Permille(total, spec.ShieldOnDamagePermille), weapon);
                    if (target.ThornsPermille > 0 && !isProc && src == Hero)
                    {
                        bool c2;
                        DealDamage(target, Hero, Math.Max(1, FMath.Permille(total, target.ThornsPermille)), DamageType.True, -1, true, depth + 1, 0, 1000, 0, HitFlags.Dot, -1, out c2);
                        CreditDamageTaken(target, 0);
                    }
                    Raise(new TriggerEvent { Type = TriggerType.OnHit, Weapon = weapon, SourceUnit = Hero.Id, TargetUnit = target.Id, Amount = total, DamageType = type, Crit = crit, IsProc = isProc, ProcDepth = depth, OriginRune = originRune });
                    if (crit) Raise(new TriggerEvent { Type = TriggerType.OnCrit, Weapon = weapon, SourceUnit = Hero.Id, TargetUnit = target.Id, Amount = total, DamageType = type, Crit = true, IsProc = isProc, ProcDepth = depth, OriginRune = originRune });
                    if (crit && spec.Signature == LegendarySignature.Starfall && !isProc)
                        AddZone(ZoneKind.Starfall, target.X, 1400, 20, 20, FMath.Permille(spec.BaseDamage, Math.Max(1200, spec.SignatureValue)), DamageType.Radiant, weapon, true, depth + 1, null);
                }
            }
            else if (target.IsHero)
            {
                Report.DamageTaken += total;
                if (src != null) CreditDamageTaken(src, total);
                if (Hero.ThornsPermille > 0 && src != null && src.Team == 1 && !dot)
                {
                    bool c2;
                    DealDamage(Hero, src, Math.Max(1, FMath.Permille(total, Hero.ThornsPermille)), DamageType.True, -1, true, depth + 1, 0, 1000, 0, HitFlags.Dot, -1, out c2);
                }
                Raise(new TriggerEvent { Type = TriggerType.OnDamageTaken, Weapon = -1, SourceUnit = src != null ? src.Id : 0, TargetUnit = Hero.Id, Amount = total, DamageType = type, OriginRune = -1 });
                if (!_lowHpFired && Hero.Hp > 0 && Hero.Hp * 1000 < Hero.MaxHp * LowHpPermille)
                {
                    _lowHpFired = true;
                    Raise(new TriggerEvent { Type = TriggerType.OnLowHp, Weapon = -1, SourceUnit = src != null ? src.Id : 0, TargetUnit = Hero.Id, OriginRune = -1 });
                }
            }

            if (target.Hp <= 0) Kill(target, src, weapon, isProc, depth);
            return total;
        }

        private void CreditDamageTaken(Unit src, int amount)
        {
            string key = src.IsHero ? "hero" : (src.Def != null ? src.Def.Id : (src.IsFamiliar ? "familiar" : "unknown"));
            long cur;
            Report.DamageTakenBySource.TryGetValue(key, out cur);
            Report.DamageTakenBySource[key] = cur + amount;
        }

        private void Leech(int amount)
        {
            if (amount <= 0 || _leechBudget <= 0) return;
            int a = Math.Min(amount, _leechBudget);
            _leechBudget -= a;
            Report.LeechHealing += HealHero(a, -1, false);
        }

        public int HealHero(int amount, int weapon, bool emit = true)
        {
            if (!Hero.Alive || amount <= 0) return 0;
            amount = FMath.Permille(amount, 1000 + Setup.Hero.Stats.SumUntagged(StatType.HealingPower));
            int healed = Math.Min(amount, Hero.MaxHp - Hero.Hp);
            if (healed <= 0) return 0;
            Hero.Hp += healed;
            Report.Healing += healed;
            Emit(CombatEventType.Heal, Hero.Id, Hero.Id, healed, 0, weapon, Hero.X);
            return healed;
        }

        public void GainShield(int amount, int weapon)
        {
            if (!Hero.Alive || amount <= 0) return;
            int cap = Math.Max(1, FMath.Permille(Hero.MaxHp, _db.Tuning.ShieldCapPermille));
            int before = Hero.Shield;
            Hero.Shield = Math.Min(cap, Hero.Shield + amount);
            int gained = Hero.Shield - before;
            if (gained > 0)
            {
                Report.ShieldGained += gained;
                Emit(CombatEventType.ShieldGain, Hero.Id, Hero.Id, gained, Hero.Shield, weapon, Hero.X);
            }
        }

        private void Kill(Unit target, Unit killer, int weapon, bool isProc, int depth)
        {
            if (target.State == UnitState.Dead) return;
            if (target.IsHero)
            {
                for (int i = 0; i < Weapons.Length; i++)
                {
                    var w = Weapons[i];
                    if (w.Spec.Signature == LegendarySignature.Phoenix && !w.PhoenixUsed)
                    {
                        w.PhoenixUsed = true;
                        Hero.Hp = Math.Max(1, Hero.MaxHp * Math.Max(300, w.Spec.SignatureValue) / 1000);
                        Emit(CombatEventType.Revive, Hero.Id, 0, Hero.Hp, 0, i, Hero.X, "phoenix");
                        for (int k = 0; k < Units.Count; k++)
                        {
                            var u = Units[k];
                            if (u.Team == 1 && u.Targetable && FMath.Abs(u.X - Hero.X) <= 3500) HitUnit(w, u, w.Spec.BaseDamage * 3, true, 1, null, HitFlags.Secondary);
                        }
                        return;
                    }
                }
                target.State = UnitState.Dead;
                Report.KilledBy = killer != null && killer.Def != null ? killer.Def.Id : (killer != null && killer.IsHero ? "self" : "status");
                Emit(CombatEventType.Death, target.Id, killer != null ? killer.Id : 0, 0, 0, -1, target.X, "hero");
                return;
            }
            if (target.IsFamiliar) { KillFamiliar(target); return; }

            target.State = UnitState.Dead;
            target.StateTimer = 0;
            Report.EnemiesKilled++;
            if (weapon >= 0 && weapon < Report.Weapons.Length) Report.Weapons[weapon].Kills++;
            Emit(CombatEventType.Death, target.Id, killer != null ? killer.Id : 0, 0, 0, weapon, target.X, target.Def != null ? target.Def.Id : null);
            OnEnemyDeath(target);

            if (weapon >= 0 && weapon < Weapons.Length)
            {
                var w = Weapons[weapon];
                var spec = w.Spec;
                if (spec.VolatileOnKillPermille > 0)
                {
                    int boom = FMath.Permille(spec.BaseDamage, spec.VolatileOnKillPermille);
                    for (int i = 0; i < Units.Count; i++)
                    {
                        var u = Units[i];
                        if (u.Team == 1 && u.Targetable && FMath.Abs(u.X - target.X) <= 1500 + u.Radius)
                            HitUnit(w, u, boom, true, depth + 1, null, HitFlags.Secondary | HitFlags.NoStatus);
                    }
                    Emit(CombatEventType.ZoneSpawn, Hero.Id, target.Id, 1500, 0, weapon, target.X, "volatile");
                }
                if (spec.Signature == LegendarySignature.StatusSpreadOnKill || spec.Signature == LegendarySignature.Plague) SpreadStatuses(target, w, 2500);
                if (spec.Signature == LegendarySignature.SoulHarvest)
                {
                    w.Souls++;
                    if (w.Souls % 10 == 0) SpawnFamiliar(w, spec.BaseDamage * 2);
                }
            }
            Raise(new TriggerEvent { Type = TriggerType.OnKill, Weapon = weapon, SourceUnit = Hero.Id, TargetUnit = target.Id, IsProc = isProc, ProcDepth = depth, OriginRune = -1 });
        }

        private void SpreadStatuses(Unit from, WeaponRuntime w, int radius)
        {
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (u == from || u.Team != 1 || !u.Alive || FMath.Abs(u.X - from.X) > radius) continue;
                for (int s = 1; s < (int)StatusType.Haste; s++)
                {
                    int stacks = from.Status.Stacks[s];
                    if (stacks <= 0 || s == (int)StatusType.Freeze || s == (int)StatusType.Stun) continue;
                    ApplyStatus(u, new StatusApply { Status = (StatusType)s, ChancePermille = 1000, Stacks = Math.Max(1, stacks / 2), DurationTicks = Math.Max(60, from.Status.Ticks[s]) }, w, true, 1, 0);
                }
            }
            Emit(CombatEventType.SignatureFire, Hero.Id, from.Id, (int)w.Spec.Signature, 0, w.Index, from.X, "spread");
        }

        // ------------------------------------------------------------------ statuses

        public static int MaxStacks(StatusType s)
        {
            switch (s)
            {
                case StatusType.Burn: return 10;
                case StatusType.Poison: return 20;
                case StatusType.Bleed: return 5;
                case StatusType.Chill: return 5;
                case StatusType.Shock: return 3;
                case StatusType.ArmorBreak: return 10;
                case StatusType.Curse: return 5;
                case StatusType.Haste: return 5;
                case StatusType.Regen: return 5;
                default: return 1;
            }
        }

        /// <summary>Apply a status from a hero weapon to an enemy (or from an enemy to the hero when w == null).</summary>
        public bool ApplyStatus(Unit target, StatusApply sa, WeaponRuntime w, bool isProc, int depth, int chanceBonus, int potencyOverride = 0)
        {
            if (sa == null || sa.Status == StatusType.None || target == null || !target.Alive) return false;
            if (!_rng.Chance(sa.ChancePermille + chanceBonus)) return false;
            var st = target.Status;
            var s = sa.Status;
            int idx = (int)s;
            int duration = sa.DurationTicks;
            if (w != null) duration = FMath.Permille(duration, w.Spec.StatusDurationPermille);
            if (target.IsBoss && (s == StatusType.Stun || s == StatusType.Freeze)) duration /= 3;   // bosses resist hard CC (not immune)
            if (s == StatusType.Freeze && st.FreezeImmuneTicks > 0) return false;

            if (s == StatusType.Chill)
            {
                int stacks = Math.Min(MaxStacks(s), st.Stacks[idx] + sa.Stacks);
                if (stacks >= 5 && st.FreezeImmuneTicks <= 0)
                {
                    st.Clear(StatusType.Chill);
                    return ApplyStatus(target, new StatusApply { Status = StatusType.Freeze, ChancePermille = 1000, Stacks = 1, DurationTicks = 45 }, w, isProc, depth, 1000);
                }
                st.Stacks[idx] = stacks;
                st.Ticks[idx] = Math.Max(st.Ticks[idx], duration);
            }
            else
            {
                int cap = target.Team == 0 && (s == StatusType.Burn || s == StatusType.Poison || s == StatusType.Bleed) ? HeroDotStackCap : MaxStacks(s);
                st.Stacks[idx] = Math.Min(cap, st.Stacks[idx] + sa.Stacks);
                st.Ticks[idx] = Math.Max(st.Ticks[idx], duration);
            }
            if (s == StatusType.Stun || s == StatusType.Freeze) InterruptWindupSilently(target);
            int potency = potencyOverride;
            if (potency <= 0)
            {
                int baseDmg = w != null ? w.Spec.BaseDamage : 4;
                switch (s)
                {
                    case StatusType.Burn: potency = Math.Max(1, FMath.Permille(baseDmg, 100 + Setup.Hero.Stats.SumUntagged(StatType.BurnDamage) / 10)); break;
                    case StatusType.Poison: potency = Math.Max(1, FMath.Permille(baseDmg, 60 + Setup.Hero.Stats.SumUntagged(StatType.PoisonDamage) / 10)); break;
                    case StatusType.Bleed: potency = Math.Max(1, FMath.Permille(baseDmg, 90)); break;
                }
            }
            if (potency > st.Potency[idx]) st.Potency[idx] = potency;
            st.SourceWeapon[idx] = w != null ? w.Index : -1;
            Emit(CombatEventType.StatusApplied, w != null ? Hero.Id : 0, target.Id, idx, st.Stacks[idx], w != null ? w.Index : -1, target.X, null, false, isProc);
            if (w != null && target.Team == 1)
                Raise(new TriggerEvent { Type = TriggerType.OnStatusApplied, Weapon = w.Index, SourceUnit = Hero.Id, TargetUnit = target.Id, Status = s, IsProc = isProc, ProcDepth = depth, OriginRune = _activeOriginRune });
            return true;
        }

        private void InterruptWindupSilently(Unit u)
        {
            if (u.IsHero) return;
            if (u.State == UnitState.Windup || u.State == UnitState.Ability)
            {
                u.State = UnitState.Stunned;
                u.PendingAbility = -1;
            }
        }

        private void UpdateStatuses()
        {
            int count = Units.Count;
            for (int i = 0; i < count; i++)
            {
                var u = Units[i];
                if (!u.Alive) continue;
                var st = u.Status;
                if (st.FreezeImmuneTicks > 0) st.FreezeImmuneTicks--;
                if (u.Team == 1 && u.RegenPerSecond > 0 && Tick % FMath.TicksPerSecond == 0 && !st.Has(StatusType.Burn) && !st.Has(StatusType.Poison))
                    u.Hp = Math.Min(u.MaxHp, u.Hp + u.RegenPerSecond);
                for (int s = 1; s < (int)StatusType.Count; s++)
                {
                    if (st.Stacks[s] <= 0) continue;
                    int interval = u.Team == 0 ? HeroDotInterval : DotInterval;
                    if (Tick % interval == 0 && (s == (int)StatusType.Burn || s == (int)StatusType.Poison || s == (int)StatusType.Bleed))
                    {
                        int dmg = st.Potency[s] * st.Stacks[s];
                        var type = s == (int)StatusType.Burn ? DamageType.Fire : (s == (int)StatusType.Poison ? DamageType.Poison : DamageType.Physical);
                        int weapon = st.SourceWeapon[s];
                        var src = u.Team == 1 ? Hero : null;
                        bool c;
                        DealDamage(src, u, Math.Max(1, dmg), type, u.Team == 1 ? weapon : -1, true, 1, 0, 1000, 0, HitFlags.Dot | HitFlags.IgnoreArmor, -1, out c);
                        if (!u.Alive) break;
                    }
                    if (st.Ticks[s] > 0 && --st.Ticks[s] <= 0)
                    {
                        // Plague legendary: poison never expires while the weapon is active.
                        if (s == (int)StatusType.Poison && u.Team == 1 && HasSignature(LegendarySignature.Plague)) { st.Ticks[s] = 30; continue; }
                        st.Stacks[s] = 0;
                        st.Potency[s] = 0;
                        if (s == (int)StatusType.Freeze) st.FreezeImmuneTicks = 60;
                        if ((s == (int)StatusType.Stun || s == (int)StatusType.Freeze) && u.State == UnitState.Stunned) u.State = UnitState.Idle;
                        Emit(CombatEventType.StatusExpired, 0, u.Id, s, 0, -1, u.X);
                    }
                }
            }
        }

        private bool HasSignature(LegendarySignature sig)
        {
            for (int i = 0; i < Weapons.Length; i++) if (Weapons[i].Spec.Signature == sig) return true;
            return false;
        }

        // ------------------------------------------------------------------ projectiles & zones

        private void UpdateProjectiles()
        {
            int count = Projectiles.Count;
            for (int i = 0; i < count; i++)
            {
                var p = Projectiles[i];
                if (p.Dead) continue;
                if (--p.LifeTicks <= 0) { p.Dead = true; Emit(CombatEventType.ProjectileExpire, p.Id, 0, 0, 0, p.Weapon, p.X); continue; }
                if (p.IsEnemy) { UpdateEnemyProjectile(p); continue; }

                if (p.Wave)
                {
                    p.X += p.Dir * p.Speed;
                    if (p.X <= ArenaLeft || p.X >= ArenaRight) { p.Dead = true; continue; }
                    for (int k = 0; k < Units.Count; k++)
                    {
                        var u = Units[k];
                        if (u.Team != 1 || !u.Targetable || u.Flying || p.HitIds.Contains(u.Id)) continue;
                        if (FMath.Abs(u.X - p.X) <= u.Radius + 200)
                        {
                            p.HitIds.Add(u.Id);
                            HitUnit(Weapons[p.Weapon], u, p.Damage, p.IsProc, p.ProcDepth, null, HitFlags.Secondary);
                        }
                    }
                    continue;
                }

                if (p.Returning)
                {
                    p.AimX = Hero.X;
                }
                else
                {
                    var target = FindUnit(p.TargetId);
                    if (p.Homing && target != null && target.Targetable) p.AimX = target.X;
                }
                int dx = p.AimX - p.X;
                int move = Math.Min(FMath.Abs(dx), p.Speed);
                p.X += FMath.Sign(dx) * move;
                if (dx != 0) p.Dir = FMath.Sign(dx);

                if (p.Returning)
                {
                    TryProjectileCollide(p, false);
                    if (FMath.Abs(p.X - Hero.X) <= 300) p.Dead = true;
                    continue;
                }
                bool arrived = FMath.Abs(p.AimX - p.X) <= 50;
                if (!TryProjectileCollide(p, arrived) && arrived)
                {
                    // Landed where the target was; nothing there: miss.
                    if (p.PierceLeft > 0 && p.HitIds.Count > 0) { p.AimX = p.Dir > 0 ? ArenaRight : ArenaLeft; continue; }
                    p.Dead = true;
                    Emit(CombatEventType.ProjectileExpire, p.Id, 0, 0, 0, p.Weapon, p.X, "miss");
                }
            }
        }

        private bool TryProjectileCollide(Projectile p, bool arrived)
        {
            Unit hit = null;
            int bestD = int.MaxValue;
            for (int k = 0; k < Units.Count; k++)
            {
                var u = Units[k];
                if (u.Team != 1 || !u.Targetable || p.HitIds.Contains(u.Id)) continue;
                int d = FMath.Abs(u.X - p.X);
                if (d > u.Radius + 150) continue;
                bool designated = u.Id == p.TargetId;
                // Barriers intercept projectiles that pass through them (anti-projectile counterplay).
                bool barrier = u.BarrierHp > 0;
                bool canHit = designated || barrier || p.HitIds.Count > 0 || arrived || p.Returning;
                if (!canHit) continue;
                if (d < bestD || (d == bestD && hit != null && u.Id < hit.Id)) { bestD = d; hit = u; }
            }
            if (hit == null) return false;
            var w = Weapons[p.Weapon];
            if (hit.BarrierHp > 0)
            {
                hit.BarrierHp -= p.Damage;
                Emit(CombatEventType.ProjectileHit, p.Id, hit.Id, p.Damage, -1, p.Weapon, p.X, "barrier");
                if (hit.BarrierHp <= 0) { hit.BarrierHp = 0; Emit(CombatEventType.ShieldBreak, Hero.Id, hit.Id, 0, 1, p.Weapon, hit.X, "barrier"); }
                p.Dead = true;
                return true;
            }
            if (hit.Reflects && !p.IsProc && _rng.Chance(hit.ReflectChance))
            {
                Emit(CombatEventType.ProjectileHit, p.Id, hit.Id, 0, -2, p.Weapon, p.X, "reflect");
                p.IsEnemy = true;
                p.Team = 1;
                p.Damage = Math.Max(1, p.Damage / 2);
                p.TargetId = Hero.Id;
                p.Homing = true;
                p.SourceUnit = hit.Id;
                p.HitIds.Clear();
                return true;
            }
            p.HitIds.Add(hit.Id);
            Emit(CombatEventType.ProjectileHit, p.Id, hit.Id, p.Damage, (int)p.Type, p.Weapon, p.X, null, false, p.IsProc);
            var step = p.StepIndex >= 0 && p.StepIndex < w.Spec.Steps.Count ? w.Spec.Steps[p.StepIndex] : null;
            HitUnit(w, hit, p.Damage, p.IsProc, p.ProcDepth, step, HitFlags.None);
            if (w.Spec.Signature == LegendarySignature.InfinitePierce) { p.AimX = p.Dir > 0 ? ArenaRight : ArenaLeft; p.TargetId = -1; return true; }
            if (p.PierceLeft > 0)
            {
                p.PierceLeft--;
                p.TargetId = -1;
                p.Homing = false;
                p.AimX = p.Dir > 0 ? ArenaRight : ArenaLeft;
                return true;
            }
            if (p.RicochetLeft > 0 || w.Spec.Signature == LegendarySignature.Ricochet)
            {
                var next = NearestEnemyTo(hit.X, hit.Id, p.HitIds, 5000);
                if (next != null && (p.RicochetLeft > 0 || p.HitIds.Count < 12))
                {
                    if (p.RicochetLeft > 0) p.RicochetLeft--;
                    p.TargetId = next.Id;
                    p.AimX = next.X;
                    p.Homing = true;
                    return true;
                }
            }
            if (p.Boomerang && !p.Returning)
            {
                p.Returning = true;
                p.HitIds.Clear();
                p.HitIds.Add(hit.Id);
                return true;
            }
            p.Dead = true;
            return true;
        }

        private void UpdateEnemyProjectile(Projectile p)
        {
            Unit target = FindUnit(p.TargetId);
            if (target == null || !target.Alive) target = Hero;
            p.AimX = target.X;
            int dx = p.AimX - p.X;
            p.X += FMath.Sign(dx) * Math.Min(FMath.Abs(dx), p.Speed);
            if (FMath.Abs(target.X - p.X) <= target.Radius)
            {
                p.Dead = true;
                var src = FindUnit(p.SourceUnit);
                bool c;
                DealDamage(src, target, p.Damage, p.Type, -1, false, 0, 0, 1000, 0, HitFlags.NoCrit, -1, out c);
                if (p.EnemyStatus != null && target.Alive) ApplyStatusToHero(target, p.EnemyStatus, src);
            }
        }

        public void AddZone(ZoneKind kind, int x, int radius, int duration, int interval, int damage, DamageType type, int weapon, bool isProc, int depth, StatusApply status, int team = 0, int sourceUnit = 0)
        {
            if (Zones.Count >= Setup.MaxZones) { Report.ProcsSuppressedBudget++; return; }
            var z = new Zone
            {
                Id = _nextZoneId++,
                Kind = kind,
                Team = team,
                X = x,
                Radius = radius,
                TicksLeft = duration,
                Interval = Math.Max(1, interval),
                Timer = interval,
                Damage = Math.Max(1, damage),
                Type = type,
                Weapon = weapon,
                IsProc = isProc,
                ProcDepth = depth,
                Status = status,
                SourceUnit = sourceUnit
            };
            Zones.Add(z);
            Emit(CombatEventType.ZoneSpawn, sourceUnit, z.Id, radius, duration, weapon, x, kind.ToString());
        }

        private void UpdateZones()
        {
            int count = Zones.Count;
            for (int i = 0; i < count; i++)
            {
                var z = Zones[i];
                if (z.Dead) continue;
                z.TicksLeft--;
                z.Timer--;
                bool fire = z.Timer <= 0;
                if (fire) z.Timer = z.Interval;
                switch (z.Kind)
                {
                    case ZoneKind.Delayed:
                    case ZoneKind.Starfall:
                        if (z.TicksLeft <= 0) ZoneHitEnemies(z, z.Kind == ZoneKind.Starfall ? HitFlags.Secondary : HitFlags.Secondary | HitFlags.NoStatus);
                        break;
                    case ZoneKind.Lingering:
                        if (fire) ZoneHitEnemies(z, HitFlags.Secondary | HitFlags.NoCrit);
                        break;
                    case ZoneKind.Singularity:
                        for (int k = 0; k < Units.Count; k++)
                        {
                            var u = Units[k];
                            if (u.Team != 1 || !u.Alive || u.IsBoss) continue;
                            int d = z.X - u.X;
                            if (FMath.Abs(d) <= z.Radius) u.X += FMath.Sign(d) * Math.Min(FMath.Abs(d), 40);
                        }
                        if (fire) ZoneHitEnemies(z, HitFlags.Secondary | HitFlags.NoCrit);
                        break;
                    case ZoneKind.Thunder:
                        if (fire && z.Weapon >= 0)
                        {
                            var pool = new List<Unit>();
                            for (int k = 0; k < Units.Count; k++) if (Units[k].Team == 1 && Units[k].Targetable) pool.Add(Units[k]);
                            if (pool.Count > 0)
                            {
                                var t = pool[_rng.Next(pool.Count)];
                                Emit(CombatEventType.SignatureFire, Hero.Id, t.Id, (int)LegendarySignature.Thunderhead, z.Damage, z.Weapon, t.X, "thunder");
                                HitUnit(Weapons[z.Weapon], t, z.Damage, true, z.ProcDepth, null, HitFlags.Secondary);
                            }
                        }
                        break;
                    case ZoneKind.EnemySlam:
                        if (z.TicksLeft <= 0)
                        {
                            var src = FindUnit(z.SourceUnit);
                            for (int k = 0; k < Units.Count; k++)
                            {
                                var u = Units[k];
                                if (u.Team != 0 || !u.Alive || FMath.Abs(u.X - z.X) > z.Radius + u.Radius) continue;
                                bool c;
                                DealDamage(src, u, z.Damage, z.Type, -1, false, 0, 0, 1000, 0, HitFlags.NoCrit, -1, out c);
                                if (z.Status != null && u.IsHero && u.Alive) ApplyStatusToHero(u, z.Status, src);
                            }
                            Emit(CombatEventType.AbilityCast, z.SourceUnit, 0, z.Radius, z.Damage, -1, z.X, "slam_impact");
                        }
                        break;
                }
                if (z.TicksLeft <= 0) z.Dead = true;
            }
        }

        private void ZoneHitEnemies(Zone z, HitFlags flags)
        {
            if (z.Weapon < 0 || z.Weapon >= Weapons.Length) return;
            var w = Weapons[z.Weapon];
            for (int k = 0; k < Units.Count; k++)
            {
                var u = Units[k];
                if (u.Team != 1 || !u.Targetable || FMath.Abs(u.X - z.X) > z.Radius + u.Radius) continue;
                HitUnit(w, u, z.Damage, z.IsProc, z.ProcDepth, null, flags);
                if (z.Status != null && u.Alive && z.Kind == ZoneKind.Lingering) ApplyStatus(u, z.Status, w, true, z.ProcDepth, 0);
            }
        }

        private void UpdateScheduled()
        {
            if (_scheduled.Count == 0) return;
            // Stable: process due actions ordered by (tick, seq).
            _scheduled.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Seq.CompareTo(b.Seq));
            int processed = 0;
            while (processed < _scheduled.Count && _scheduled[processed].Tick <= Tick) processed++;
            if (processed == 0) return;
            var due = _scheduled.GetRange(0, processed);
            _scheduled.RemoveRange(0, processed);
            foreach (var s in due)
            {
                if (!Hero.Alive) return;
                var w = s.Weapon >= 0 && s.Weapon < Weapons.Length ? Weapons[s.Weapon] : null;
                if (w == null) continue;
                var target = FindUnit(s.TargetId);
                var step = w.Spec.Steps[s.Step % w.Spec.Steps.Count];
                bool melee = step.Kind == StepKind.Melee;
                if (target == null || !CanWeaponHit(w.Spec, target, melee)) target = SelectTarget(w.Spec, Setup.Hero.Targeting, w.Spec.RangeMilli + 600, melee);
                if (target == null) continue;
                switch (s.Kind)
                {
                    case ScheduledKind.RepeatAttack:
                    case ScheduledKind.TwinStrike:
                    case ScheduledKind.BarrageShot:
                        Emit(CombatEventType.AttackRelease, Hero.Id, target.Id, s.Step, s.Power, w.Index, Hero.X, s.Kind.ToString(), false, true);
                        PerformAttack(w, s.Step, target, s.Power, true, Math.Max(1, s.ProcDepth));
                        break;
                    case ScheduledKind.LinkProc:
                        FireLink(Links[s.Link], target, s.ProcDepth);
                        break;
                }
            }
        }
    }
}
