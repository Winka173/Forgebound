using System;
using System.Collections.Generic;
using Forgebound.Core.Content;
using Forgebound.Core.Simulation;

namespace Forgebound.Core.Combat
{
    public sealed partial class Battle
    {
        private readonly List<EnemyDef> _graveyard = new List<EnemyDef>();

        private void EnterPhase(Unit u, int index, bool emit)
        {
            var def = u.Def;
            if (index < 0 || index >= def.Phases.Count) return;
            u.PhaseIndex = index;
            var ph = def.Phases[index];
            u.ActiveAbilities.Clear();
            u.ActiveAbilities.AddRange(def.Abilities);
            u.ActiveAbilities.AddRange(ph.Abilities);
            u.SpeedPermille = ph.SpeedPermille;
            u.AttackSpeedPermille = ph.AttackSpeedPermille;
            u.DamagePermille = FMath.Permille(1000 + Setup.Difficulty.SumUntagged(StatType.EnemyDamage), ph.DamagePermille);
            foreach (var kv in def.Resist) u.Resist[(int)kv.Key] = kv.Value;
            foreach (var kv in ph.Resist) u.Resist[(int)kv.Key] = kv.Value;
            if (emit)
            {
                ResetAbilityTimers(u);
                u.State = UnitState.Idle;
                u.PendingAbility = -1;
                Emit(CombatEventType.PhaseChange, u.Id, 0, index, def.Phases.Count, -1, u.X, ph.NameKey);
            }
        }

        private void CheckPhase(Unit u)
        {
            if (u.Def.Phases.Count == 0) return;
            int next = u.PhaseIndex + 1;
            if (next >= u.Def.Phases.Count) return;
            if (u.Hp * 1000L < (long)u.MaxHp * u.Def.Phases[next].HpBelowPermille) EnterPhase(u, next, true);
        }

        private Unit EnemyTarget(Unit e)
        {
            // Enemies engage the closest player-side unit (hero or familiar); taunting familiars could extend this.
            Unit best = null;
            int bestD = int.MaxValue;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (u.Team != 0 || !u.Alive) continue;
                int d = FMath.Abs(u.X - e.X);
                if (d < bestD || (d == bestD && best != null && u.Id < best.Id)) { bestD = d; best = u; }
            }
            return best;
        }

        private int EnemySpeed(Unit e)
        {
            int s = FMath.Permille(e.Speed, e.SpeedPermille);
            if (e.Status.Has(StatusType.Chill)) s = s * (1000 - 120 * e.Status.Get(StatusType.Chill)) / 1000;
            if (e.Status.Has(StatusType.Slow)) s = s * 600 / 1000;
            if (e.Enraged) s = s * 1300 / 1000;
            return Math.Max(5, s);
        }

        private int EnemyCooldownTicks(Unit e, int baseTicks)
        {
            int p = e.AttackSpeedPermille;
            if (e.Status.Has(StatusType.Chill)) p -= 80 * e.Status.Get(StatusType.Chill);
            if (e.Status.Has(StatusType.Slow)) p -= 300;
            if (e.Enraged) p += 300;
            return Math.Max(8, baseTicks * 1000 / Math.Max(300, p));
        }

        private void UpdateEnemies()
        {
            int count = Units.Count;
            for (int i = 0; i < count; i++)
            {
                var e = Units[i];
                if (e.Team != 1 || !e.Alive) continue;
                CheckPhase(e);
                if (IsDisabled(e)) { e.State = UnitState.Stunned; continue; }
                if (e.State == UnitState.Stunned) e.State = UnitState.Idle;
                if (Setup.DummyMode) continue;
                var target = EnemyTarget(e);
                if (target == null) continue;

                if (e.Burrowed)
                {
                    int dx = target.X - e.X;
                    if (Gap(e, target) < 1200)
                    {
                        e.Burrowed = false;
                        e.AttackCooldown = 0;
                        Emit(CombatEventType.AbilityCast, e.Id, target.Id, (int)EnemyAbilityType.Burrow, 0, -1, e.X, "emerge");
                    }
                    else { e.X += FMath.Sign(dx) * EnemySpeed(e) * 3 / 2; continue; }
                }

                // Telegraphed ability in progress.
                if (e.State == UnitState.Ability)
                {
                    if (--e.StateTimer <= 0)
                    {
                        int idx = e.PendingAbility;
                        e.PendingAbility = -1;
                        e.State = UnitState.Idle;
                        if (idx >= 0 && idx < e.ActiveAbilities.Count) ExecuteAbility(e, e.ActiveAbilities[idx], target);
                    }
                    continue;
                }

                // Basic attack windup in progress.
                if (e.State == UnitState.Windup)
                {
                    if (--e.StateTimer <= 0)
                    {
                        e.State = UnitState.Idle;
                        e.AttackCooldown = EnemyCooldownTicks(e, e.Def.AttackCooldownTicks);
                        if (e.Stealthed) { e.Stealthed = false; Emit(CombatEventType.AbilityCast, e.Id, 0, (int)EnemyAbilityType.Stealth, 0, -1, e.X, "reveal"); }
                        EnemyAttack(e, target);
                    }
                    continue;
                }

                // Abilities ready?
                if (TryStartAbility(e, target)) continue;

                if (e.AttackCooldown > 0) e.AttackCooldown--;
                int gap = Gap(e, target);
                int range = e.Def.AttackRangeMilli;
                int speed = EnemySpeed(e);
                if (gap > range)
                {
                    int dir = FMath.Sign(target.X - e.X);
                    int nx = e.X + dir * speed;
                    // Queue behind a ground ally that is closer to the target (readable formation; flyers ignore).
                    if (!e.Flying && !e.Def.AttackRanged)
                    {
                        for (int k = 0; k < count; k++)
                        {
                            var o = Units[k];
                            if (o == e || o.Team != 1 || !o.Alive || o.Flying || o.Burrowed) continue;
                            bool ahead = dir < 0 ? o.X < e.X && o.X > nx - 260 : o.X > e.X && o.X < nx + 260;
                            if (ahead && FMath.Abs(o.X - e.X) < 260 + speed) { nx = e.X; break; }
                        }
                    }
                    if (dir < 0) nx = Math.Max(nx, target.X + target.Radius + e.Radius);
                    else nx = Math.Min(nx, target.X - target.Radius - e.Radius);
                    e.X = FMath.Clamp(nx, ArenaLeft + 300, ArenaRight - 300);
                    e.State = UnitState.Moving;
                }
                else if (e.Def.AttackRanged && gap < range / 3 && e.X < ArenaRight - 600)
                {
                    e.X += speed / 2;   // ranged enemies keep some distance
                    e.State = UnitState.Moving;
                }
                else if (e.AttackCooldown <= 0)
                {
                    e.State = UnitState.Windup;
                    e.StateTimer = Math.Max(1, e.Def.AttackWindupTicks);
                    Emit(CombatEventType.AttackWindup, e.Id, target.Id, 0, e.StateTimer, -1, e.X);
                }
                else e.State = UnitState.Idle;
            }
        }

        private void EnemyAttack(Unit e, Unit target)
        {
            int dmg = Math.Max(1, FMath.Permille(e.Def.AttackDamage, EnemyOutgoingPermille(e)));
            if (e.Def.AttackRanged)
            {
                if (Projectiles.Count >= Setup.MaxProjectiles) return;
                var p = new Projectile
                {
                    Id = _nextProjectileId++,
                    Team = 1,
                    IsEnemy = true,
                    X = e.X - e.Radius,
                    Dir = -1,
                    Speed = Math.Max(100, e.Def.ProjectileSpeed),
                    TargetId = target.Id,
                    AimX = target.X,
                    Homing = true,
                    Damage = dmg,
                    Type = e.Def.AttackType,
                    SourceUnit = e.Id,
                    EnemyStatus = e.Def.AttackStatus
                };
                Projectiles.Add(p);
                Emit(CombatEventType.ProjectileSpawn, p.Id, target.Id, dmg, (int)p.Type, -1, p.X, e.Def.Id);
                return;
            }
            if (Gap(e, target) > e.Def.AttackRangeMilli + 300) return;   // target moved away during windup
            Emit(CombatEventType.AttackRelease, e.Id, target.Id, 0, 0, -1, e.X);
            bool c;
            DealDamage(e, target, dmg, e.Def.AttackType, -1, false, 0, 0, 1000, 0, HitFlags.NoCrit, -1, out c);
            if (e.Def.AttackStatus != null && target.Alive && target.IsHero) ApplyStatusToHero(target, e.Def.AttackStatus, e);
        }

        public void ApplyStatusToHero(Unit hero, StatusApply sa, Unit src)
        {
            int potency = src != null && src.Def != null ? Math.Max(1, src.Def.AttackDamage * 200 / 1000) : 1;
            ApplyStatus(hero, sa, null, false, 0, 0, potency);
        }

        private bool AbilityConditionMet(Unit e, EnemyAbilityDef ab, Unit target)
        {
            switch (ab.Type)
            {
                case EnemyAbilityType.HealAlly:
                    return LowestAlly(e, ab.RadiusMilli, true) != null;
                case EnemyAbilityType.Summon:
                    return CountSummons(e) < ab.Cap;
                case EnemyAbilityType.Enrage:
                    return !e.Enraged && e.Hp * 2 < e.MaxHp;
                case EnemyAbilityType.Revive:
                    return _graveyard.Count > 0;
                case EnemyAbilityType.Stealth:
                    return !e.Stealthed;
                case EnemyAbilityType.Burrow:
                    return !e.Burrowed && Gap(e, target) < 1500;
                case EnemyAbilityType.Barrier:
                    return e.BarrierHp <= 0;
                case EnemyAbilityType.Charge:
                case EnemyAbilityType.Leap:
                    return Gap(e, target) > 1500;
                case EnemyAbilityType.Slam:
                case EnemyAbilityType.StatusAura:
                    return Gap(e, target) <= ab.RadiusMilli + 1500;
                case EnemyAbilityType.Cleanse:
                    return AllyWithDebuff(e, ab.RadiusMilli) != null;
                case EnemyAbilityType.ExplodeOnDeath:
                case EnemyAbilityType.SplitOnDeath:
                case EnemyAbilityType.ReflectProjectiles:
                case EnemyAbilityType.Regen:
                case EnemyAbilityType.Thorns:
                case EnemyAbilityType.Taunt:
                    return false;  // passives
                default:
                    return true;
            }
        }

        private bool TryStartAbility(Unit e, Unit target)
        {
            if (e.AbilityTimers == null) return false;
            for (int a = 0; a < e.ActiveAbilities.Count; a++)
            {
                if (e.AbilityTimers[a] > 0) { e.AbilityTimers[a]--; continue; }
                var ab = e.ActiveAbilities[a];
                if (!AbilityConditionMet(e, ab, target)) { e.AbilityTimers[a] = 15; continue; }
                e.AbilityTimers[a] = Math.Max(30, ab.CooldownTicks);
                if (ab.WindupTicks <= 0) { ExecuteAbility(e, ab, target); return true; }
                e.State = UnitState.Ability;
                e.StateTimer = ab.WindupTicks;
                e.PendingAbility = a;
                // Telegraph: presentation shows wind-up + area so the player can read what is coming.
                int x = ab.Type == EnemyAbilityType.Slam || ab.Type == EnemyAbilityType.Leap ? target.X : e.X;
                Emit(CombatEventType.Telegraph, e.Id, target.Id, (int)ab.Type, ab.RadiusMilli, ab.WindupTicks, x, ab.Type.ToString());
                if (ab.Type == EnemyAbilityType.Slam)
                    AddZone(ZoneKind.EnemySlam, target.X, ab.RadiusMilli, ab.WindupTicks, ab.WindupTicks, Math.Max(1, FMath.Permille(ab.Value, EnemyOutgoingPermille(e))), e.Def.AttackType, -1, false, 0, ab.Status, 1, e.Id);
                return true;
            }
            return false;
        }

        private Unit LowestAlly(Unit e, int radius, bool woundedOnly)
        {
            Unit best = null;
            long bestRatio = long.MaxValue;
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (u.Team != 1 || !u.Alive || FMath.Abs(u.X - e.X) > radius) continue;
                if (woundedOnly && u.Hp >= u.MaxHp) continue;
                long ratio = (long)u.Hp * 1000 / u.MaxHp;
                if (ratio < bestRatio || (ratio == bestRatio && best != null && u.Id < best.Id)) { bestRatio = ratio; best = u; }
            }
            return best;
        }

        private Unit AllyWithDebuff(Unit e, int radius)
        {
            for (int i = 0; i < Units.Count; i++)
            {
                var u = Units[i];
                if (u.Team != 1 || !u.Alive || FMath.Abs(u.X - e.X) > radius) continue;
                var st = u.Status;
                if (st.Has(StatusType.Burn) || st.Has(StatusType.Poison) || st.Has(StatusType.Curse) || st.Has(StatusType.Chill) || st.Has(StatusType.Bleed) || st.Has(StatusType.ArmorBreak)) return u;
            }
            return null;
        }

        private int CountSummons(Unit e)
        {
            int n = 0;
            for (int i = 0; i < Units.Count; i++) if (Units[i].Alive && Units[i].SummonedBy == e.Id) n++;
            return n;
        }

        private void ExecuteAbility(Unit e, EnemyAbilityDef ab, Unit target)
        {
            int power = EnemyOutgoingPermille(e);
            Emit(CombatEventType.AbilityCast, e.Id, target != null ? target.Id : 0, (int)ab.Type, ab.Value, -1, e.X, ab.Type.ToString());
            bool c;
            switch (ab.Type)
            {
                case EnemyAbilityType.HealAlly:
                    {
                        var ally = LowestAlly(e, ab.RadiusMilli, true);
                        if (ally != null)
                        {
                            int heal = Math.Min(ab.Value, ally.MaxHp - ally.Hp);
                            ally.Hp += heal;
                            Emit(CombatEventType.Heal, e.Id, ally.Id, heal, 0, -1, ally.X);
                        }
                        break;
                    }
                case EnemyAbilityType.ShieldAlly:
                    for (int i = 0; i < Units.Count; i++)
                    {
                        var u = Units[i];
                        if (u.Team == 1 && u.Alive && FMath.Abs(u.X - e.X) <= ab.RadiusMilli)
                        {
                            u.Shield = Math.Min(u.MaxHp, u.Shield + ab.Value);
                            Emit(CombatEventType.ShieldGain, e.Id, u.Id, ab.Value, u.Shield, -1, u.X);
                        }
                    }
                    break;
                case EnemyAbilityType.Summon:
                    {
                        var def = _db.Enemy(ab.EnemyId);
                        if (def != null)
                            for (int k = 0; k < ab.Count && CountSummons(e) < ab.Cap; k++) SpawnEnemy(def, e.X - 500 - k * 300, e.Id);
                        break;
                    }
                case EnemyAbilityType.Charge:
                    if (target != null)
                    {
                        e.X = Math.Max(target.X + target.Radius + e.Radius, e.X - 4000);
                        DealDamage(e, target, Math.Max(1, FMath.Permille(ab.Value, power)), e.Def.AttackType, -1, false, 0, 0, 1000, 0, HitFlags.NoCrit, -1, out c);
                        if (ab.Status != null && target.Alive && target.IsHero) ApplyStatusToHero(target, ab.Status, e);
                    }
                    break;
                case EnemyAbilityType.Leap:
                    if (target != null)
                    {
                        e.X = target.X + target.Radius + e.Radius;
                        AddZone(ZoneKind.EnemySlam, target.X, ab.RadiusMilli, 1, 1, Math.Max(1, FMath.Permille(ab.Value, power)), e.Def.AttackType, -1, false, 0, ab.Status, 1, e.Id);
                    }
                    break;
                case EnemyAbilityType.Stealth:
                    e.Stealthed = true;
                    break;
                case EnemyAbilityType.Enrage:
                    e.Enraged = true;
                    e.DamagePermille = e.DamagePermille * 1500 / 1000;
                    Emit(CombatEventType.Enrage, e.Id, 0, 1, 0, -1, e.X);
                    break;
                case EnemyAbilityType.Teleport:
                    if (target != null) e.X = FMath.Clamp(target.X + 1200 + _rng.Next(1500), ArenaLeft + 500, ArenaRight - 500);
                    break;
                case EnemyAbilityType.BuffAllies:
                    for (int i = 0; i < Units.Count; i++)
                    {
                        var u = Units[i];
                        if (u.Team == 1 && u.Alive && u != e && FMath.Abs(u.X - e.X) <= ab.RadiusMilli)
                            u.DamagePermille = Math.Min(2000, u.DamagePermille + Math.Max(50, ab.Value));
                    }
                    break;
                case EnemyAbilityType.Volley:
                    if (target != null)
                        for (int k = 0; k < Math.Max(1, ab.Count) && Projectiles.Count < Setup.MaxProjectiles; k++)
                        {
                            var p = new Projectile
                            {
                                Id = _nextProjectileId++, Team = 1, IsEnemy = true, X = e.X - e.Radius - k * 200, Dir = -1,
                                Speed = Math.Max(150, e.Def.ProjectileSpeed), TargetId = target.Id, AimX = target.X, Homing = true,
                                Damage = Math.Max(1, FMath.Permille(ab.Value, power)), Type = e.Def.AttackType, SourceUnit = e.Id, EnemyStatus = ab.Status
                            };
                            Projectiles.Add(p);
                            Emit(CombatEventType.ProjectileSpawn, p.Id, target.Id, p.Damage, (int)p.Type, -1, p.X, "volley");
                        }
                    break;
                case EnemyAbilityType.Slam:
                    break;   // resolved by the EnemySlam zone created at telegraph time
                case EnemyAbilityType.Burrow:
                    e.Burrowed = true;
                    e.X = Math.Min(ArenaRight - 500, e.X + 2500);
                    break;
                case EnemyAbilityType.Barrier:
                    e.BarrierHp = ab.Value;
                    Emit(CombatEventType.ShieldGain, e.Id, e.Id, ab.Value, 1, -1, e.X, "barrier");
                    break;
                case EnemyAbilityType.CurseHero:
                case EnemyAbilityType.StatusAura:
                    if (target != null && target.IsHero && ab.Status != null && FMath.Abs(target.X - e.X) <= ab.RadiusMilli + 4000) ApplyStatusToHero(target, ab.Status, e);
                    break;
                case EnemyAbilityType.ArmorUp:
                    e.Status.Stacks[(int)StatusType.Fortify] = 1;
                    e.Status.Ticks[(int)StatusType.Fortify] = Math.Max(60, ab.Value);
                    e.Armor += 0;
                    break;
                case EnemyAbilityType.DrainHero:
                    if (target != null)
                    {
                        int dealt = DealDamage(e, target, Math.Max(1, FMath.Permille(ab.Value, power)), DamageType.Shadow, -1, false, 0, 0, 1000, 0, HitFlags.NoCrit, -1, out c);
                        if (dealt > 0) e.Hp = Math.Min(e.MaxHp, e.Hp + dealt);
                    }
                    break;
                case EnemyAbilityType.Revive:
                    {
                        var def = _graveyard[_graveyard.Count - 1];
                        _graveyard.RemoveAt(_graveyard.Count - 1);
                        var u = SpawnEnemy(def, e.X - 600, e.Id);
                        u.Hp = Math.Max(1, u.MaxHp / 2);
                        u.Revived = true;
                        Emit(CombatEventType.Revive, e.Id, u.Id, u.Hp, 0, -1, u.X, def.Id);
                        break;
                    }
                case EnemyAbilityType.Cleanse:
                    {
                        var ally = AllyWithDebuff(e, ab.RadiusMilli);
                        if (ally != null)
                        {
                            ally.Status.Clear(StatusType.Burn); ally.Status.Clear(StatusType.Poison); ally.Status.Clear(StatusType.Curse);
                            ally.Status.Clear(StatusType.Chill); ally.Status.Clear(StatusType.Bleed); ally.Status.Clear(StatusType.ArmorBreak);
                            Emit(CombatEventType.StatusExpired, e.Id, ally.Id, 0, 0, -1, ally.X, "cleanse");
                        }
                        break;
                    }
                case EnemyAbilityType.Beam:
                    if (target != null)
                    {
                        DealDamage(e, target, Math.Max(1, FMath.Permille(ab.Value, power)), e.Def.AttackType == DamageType.Physical ? DamageType.Arcane : e.Def.AttackType, -1, false, 0, 0, 1000, 0, HitFlags.NoCrit, -1, out c);
                        if (ab.Status != null && target.Alive && target.IsHero) ApplyStatusToHero(target, ab.Status, e);
                    }
                    break;
            }
        }

        private void OnEnemyDeath(Unit dead)
        {
            if (dead.Def == null) return;
            if (!dead.Revived && dead.Rank == EnemyRank.Minion && _graveyard.Count < 12) _graveyard.Add(dead.Def);
            foreach (var ab in dead.Def.Abilities)
            {
                if (ab.Type == EnemyAbilityType.ExplodeOnDeath)
                {
                    Emit(CombatEventType.Telegraph, dead.Id, 0, (int)ab.Type, ab.RadiusMilli, Math.Max(1, ab.WindupTicks), dead.X, "death_burst");
                    AddZone(ZoneKind.EnemySlam, dead.X, ab.RadiusMilli, Math.Max(1, ab.WindupTicks), Math.Max(1, ab.WindupTicks), Math.Max(1, ab.Value), dead.Def.AttackType, -1, false, 0, ab.Status, 1, dead.Id);
                }
                else if (ab.Type == EnemyAbilityType.SplitOnDeath)
                {
                    var def = _db.Enemy(ab.EnemyId);
                    if (def != null && def != dead.Def)
                        for (int k = 0; k < Math.Max(1, ab.Count); k++) SpawnEnemy(def, dead.X + k * 300, dead.Id);
                }
            }
        }

        /// <summary>Hero AI dodge: step out of a telegraphed enemy slam if not committed to an attack.</summary>
        public bool HeroInDanger()
        {
            for (int i = 0; i < Zones.Count; i++)
            {
                var z = Zones[i];
                // 8-tick reaction delay so short telegraphs still land on slow / committed heroes.
                if (z.Kind == ZoneKind.EnemySlam && !z.Dead && z.Interval - z.TicksLeft >= 8 && FMath.Abs(z.X - Hero.X) <= z.Radius + Hero.Radius) return true;
            }
            return false;
        }
    }
}
