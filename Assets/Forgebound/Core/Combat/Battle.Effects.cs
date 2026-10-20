using System;
using System.Collections.Generic;
using Forgebound.Core.Content;
using Forgebound.Core.Simulation;

namespace Forgebound.Core.Combat
{
    /// <summary>
    /// Rune / board-link / legendary-signature processing.
    /// Proc safety rules (see docs/COMBAT_RULES.md):
    ///  1. A rune never reacts to an event its own effect produced (self-trigger guard).
    ///  2. Proc-generated events only trigger runes that opt in (AllowFromProc) and only while depth &lt; MaxProcDepth.
    ///  3. Board links are charged only by non-proc events, so circuits can never recirculate energy.
    ///  4. At most MaxProcsPerTick rune/link activations per tick; projectile/zone budgets cap VFX load.
    /// Every suppression is counted in the BattleReport so balance tooling can flag pathological builds.
    /// </summary>
    public sealed partial class Battle
    {
        private const int MaxTriggerQueuePerTick = 4000;
        private int _activeOriginRune = -1;
        private int _linkOverrideType = -1;
        private StatusApply _linkExtraStatus;

        private static bool IsWeaponScoped(TriggerType t)
        {
            switch (t)
            {
                case TriggerType.OnAttack:
                case TriggerType.OnHit:
                case TriggerType.OnCrit:
                case TriggerType.OnKill:
                case TriggerType.OnStatusApplied:
                case TriggerType.OnShieldBreak:
                case TriggerType.OnProjectileFired:
                case TriggerType.OnSummon:
                    return true;
                default:
                    return false;
            }
        }

        private void ProcessTriggers()
        {
            int i = 0;
            while (i < _triggers.Count && i < MaxTriggerQueuePerTick)
            {
                var t = _triggers[i++];
                HandleTrigger(t);
            }
            if (_triggers.Count > i) Report.ProcsSuppressedBudget += _triggers.Count - i;
            _triggers.Clear();
        }

        private void HandleTrigger(TriggerEvent t)
        {
            for (int r = 0; r < _runes.Count; r++)
            {
                var rr = _runes[r];
                if (rr.Def.Trigger != t.Type || rr.Def.Trigger == TriggerType.OnTimer) continue;
                if (IsWeaponScoped(t.Type) && rr.Weapon >= 0 && rr.Weapon != t.Weapon) continue;
                TryFireRune(rr, r, t);
            }

            if (!t.IsProc && t.Weapon >= 0 && Hero.Alive)
            {
                for (int l = 0; l < Links.Count; l++)
                {
                    var link = Links[l];
                    if (link.SourceWeapon != t.Weapon || link.Listen != t.Type) continue;
                    if (link.MaxPerBattle > 0 && link.Fired >= link.MaxPerBattle) continue;
                    link.Charge++;
                    Emit(CombatEventType.LinkCharge, Hero.Id, t.TargetUnit, link.Index, link.Charge, t.Weapon, Hero.X);
                    if (link.Charge < link.Threshold) continue;
                    link.Charge = 0;
                    link.Fired++;
                    if (link.DelayTicks > 0)
                        Schedule(new Scheduled { Tick = Tick + link.DelayTicks, Kind = ScheduledKind.LinkProc, Link = l, TargetId = t.TargetUnit, ProcDepth = 1, Weapon = Math.Max(0, link.DestWeapon >= 0 ? link.DestWeapon : link.SourceWeapon) });
                    else
                        FireLink(link, FindUnit(t.TargetUnit), 1);
                }
            }
        }

        private bool ConditionOk(RuneRuntime rr, TriggerEvent t)
        {
            var c = rr.Def.Condition;
            if (c.DamageType >= 0 && (int)t.DamageType != c.DamageType) return false;
            if (c.StatusIs != StatusType.None && t.Status != c.StatusIs) return false;
            if (c.OnlyPrimary && t.Weapon != 0) return false;
            if (c.HeroHpBelowPermille > 0 && Hero.Hp * 1000L >= (long)Hero.MaxHp * c.HeroHpBelowPermille) return false;
            if (c.TargetHasStatus != StatusType.None || c.TargetHpBelowPermille > 0 || c.TargetIsElite)
            {
                var target = FindUnit(t.TargetUnit);
                if (target == null || target.Team != 1) return false;
                if (c.TargetHasStatus != StatusType.None && !target.Status.Has(c.TargetHasStatus)) return false;
                if (c.TargetHpBelowPermille > 0 && target.Hp * 1000L >= (long)target.MaxHp * c.TargetHpBelowPermille) return false;
                if (c.TargetIsElite && !(target.IsElite || target.IsBoss)) return false;
            }
            return true;
        }

        private void Fail(RuneRuntime rr, string reason)
        {
            string key = rr.Key + ":" + reason;
            int n;
            Report.RuneFailures.TryGetValue(key, out n);
            Report.RuneFailures[key] = n + 1;
        }

        private bool TryFireRune(RuneRuntime rr, int index, TriggerEvent t)
        {
            var def = rr.Def;
            if (t.OriginRune == index) { Fail(rr, "self_trigger"); return false; }
            if (t.IsProc && !def.Condition.AllowFromProc) return false;
            int depth = t.ProcDepth + 1;
            if (depth > Setup.MaxProcDepth) { Report.ProcsSuppressedDepth++; Fail(rr, "depth"); return false; }
            if (!ConditionOk(rr, t)) return false;
            if (def.Condition.EveryN > 1)
            {
                rr.Qualifying++;
                if (rr.Qualifying % def.Condition.EveryN != 0) return false;
            }
            if (rr.Cooldown > Tick) { Fail(rr, "icd"); return false; }
            if (def.MaxPerBattle > 0 && rr.Count >= def.MaxPerBattle) { Fail(rr, "battle_cap"); return false; }
            if (!_rng.Chance(def.ChancePermille)) { Fail(rr, "chance"); return false; }
            if (_procsThisTick >= Setup.MaxProcsPerTick) { Report.ProcsSuppressedTickCap++; Fail(rr, "tick_cap"); return false; }

            _procsThisTick++;
            rr.Count++;
            rr.Cooldown = Tick + def.IcdTicks;
            int n;
            Report.RuneTriggers.TryGetValue(rr.Key, out n);
            Report.RuneTriggers[rr.Key] = n + 1;
            if (depth > Report.MaxChainDepthReached) Report.MaxChainDepthReached = depth;
            int wi = rr.Weapon >= 0 ? rr.Weapon : t.Weapon;
            if (wi >= 0 && wi < Report.Weapons.Length) Report.Weapons[wi].Procs++;
            Emit(CombatEventType.RuneProc, Hero.Id, t.TargetUnit, index, depth, wi, Hero.X, def.Id, false, true);

            int prevOrigin = _activeOriginRune;
            _activeOriginRune = index;
            try
            {
                foreach (var e in def.Effects) ExecuteEffect(e, rr, t, depth);
            }
            finally { _activeOriginRune = prevOrigin; }
            return true;
        }

        private void UpdateRuneTimers()
        {
            for (int r = 0; r < _runes.Count; r++)
            {
                var rr = _runes[r];
                if (rr.Def.Trigger != TriggerType.OnTimer) continue;
                if (--rr.Timer > 0) continue;
                rr.Timer = Math.Max(1, rr.Def.TimerTicks);
                var target = NearestEnemy();
                TryFireRune(rr, r, new TriggerEvent { Type = TriggerType.OnTimer, Weapon = rr.Weapon, SourceUnit = Hero.Id, TargetUnit = target != null ? target.Id : -1, OriginRune = -1 });
            }
        }

        private WeaponRuntime OwnerWeapon(RuneRuntime rr, TriggerEvent t)
        {
            int wi = rr.Weapon >= 0 ? rr.Weapon : (t.Weapon >= 0 ? t.Weapon : 0);
            return wi >= 0 && wi < Weapons.Length ? Weapons[wi] : null;
        }

        private readonly List<Unit> _effectTargets = new List<Unit>();

        private List<Unit> ResolveTargets(EffectTarget sel, TriggerEvent t, int count)
        {
            _effectTargets.Clear();
            switch (sel)
            {
                case EffectTarget.Self:
                    _effectTargets.Add(Hero);
                    break;
                case EffectTarget.TriggerTarget:
                    {
                        var u = FindUnit(t.TargetUnit);
                        if (u == null || u.Team != 1 || !u.Alive) u = NearestEnemy();
                        if (u != null) _effectTargets.Add(u);
                        break;
                    }
                case EffectTarget.Nearest:
                    { var u = NearestEnemy(); if (u != null) _effectTargets.Add(u); break; }
                case EffectTarget.LowestHp:
                case EffectTarget.Marked:
                case EffectTarget.Cluster:
                    {
                        var prio = sel == EffectTarget.LowestHp ? TargetPriority.LowestHp : (sel == EffectTarget.Marked ? TargetPriority.Marked : TargetPriority.Cluster);
                        Unit best = null;
                        long bestScore = long.MaxValue;
                        for (int i = 0; i < Units.Count; i++)
                        {
                            var u = Units[i];
                            if (u.Team != 1 || !u.Targetable) continue;
                            long score = prio == TargetPriority.LowestHp ? u.Hp : (prio == TargetPriority.Marked ? (u.Status.Has(StatusType.Mark) ? 0 : 100000) + Gap(Hero, u) : -CountNear(u, 1500) * 100000L + Gap(Hero, u));
                            score = score * 4096 + (u.Id & 4095);
                            if (score < bestScore) { bestScore = score; best = u; }
                        }
                        if (best != null) _effectTargets.Add(best);
                        break;
                    }
                case EffectTarget.Random:
                    {
                        var pool = new List<Unit>();
                        for (int i = 0; i < Units.Count; i++) if (Units[i].Team == 1 && Units[i].Targetable) pool.Add(Units[i]);
                        for (int k = 0; k < Math.Max(1, count) && pool.Count > 0; k++)
                        {
                            int idx = _rng.Next(pool.Count);
                            _effectTargets.Add(pool[idx]);
                            pool.RemoveAt(idx);
                        }
                        break;
                    }
                case EffectTarget.AllEnemies:
                    for (int i = 0; i < Units.Count; i++) if (Units[i].Team == 1 && Units[i].Targetable) _effectTargets.Add(Units[i]);
                    break;
            }
            return _effectTargets;
        }

        private void ExecuteEffect(EffectDef e, RuneRuntime rr, TriggerEvent t, int depth)
        {
            var w = OwnerWeapon(rr, t);
            if (!_rng.Chance(e.ChancePermille)) return;
            int baseDmg = w != null ? w.Spec.BaseDamage : 5;
            int amount = FMath.Permille(baseDmg, e.Permille) + e.Flat;
            var type = e.DamageType >= 0 ? (DamageType)e.DamageType : (w != null ? w.Spec.DamageType : DamageType.Physical);
            bool c;
            switch (e.Type)
            {
                case EffectType.Damage:
                    {
                        var targets = new List<Unit>(ResolveTargets(e.Target, t, e.Count));
                        foreach (var u in targets)
                        {
                            if (u.Team != 1) continue;
                            DealDamage(Hero, u, Math.Max(1, amount), type, w != null ? w.Index : -1, true, depth, w != null ? w.Spec.CritChance : 0, w != null ? w.Spec.CritDamage : 1500, 0, HitFlags.Secondary, _activeOriginRune, out c);
                        }
                        break;
                    }
                case EffectType.Nova:
                    {
                        var centerList = ResolveTargets(e.Target == EffectTarget.Self ? EffectTarget.Self : e.Target, t, 1);
                        if (centerList.Count == 0) break;
                        int cx = centerList[0].X;
                        var hit = new List<Unit>();
                        for (int i = 0; i < Units.Count; i++)
                        {
                            var u = Units[i];
                            if (u.Team == 1 && u.Targetable && FMath.Abs(u.X - cx) <= e.RadiusMilli + u.Radius) hit.Add(u);
                        }
                        Emit(CombatEventType.ZoneSpawn, Hero.Id, 0, e.RadiusMilli, (int)type, w != null ? w.Index : -1, cx, "rune_nova", false, true);
                        foreach (var u in hit)
                        {
                            DealDamage(Hero, u, Math.Max(1, amount), type, w != null ? w.Index : -1, true, depth, 0, 1000, 0, HitFlags.Secondary | HitFlags.NoCrit, _activeOriginRune, out c);
                            if (e.Status != StatusType.None && u.Alive) ApplyStatus(u, new StatusApply { Status = e.Status, ChancePermille = 1000, Stacks = e.Stacks, DurationTicks = e.DurationTicks }, w, true, depth, 0);
                        }
                        break;
                    }
                case EffectType.Chain:
                    {
                        var start = ResolveTargets(e.Target, t, 1);
                        if (start.Count == 0 || w == null) break;
                        var from = start[0];
                        if (from.Team != 1) break;
                        int prevType = _linkOverrideType;
                        DealDamage(Hero, from, Math.Max(1, amount), type, w.Index, true, depth, 0, 1000, 0, HitFlags.Secondary | HitFlags.NoCrit, _activeOriginRune, out c);
                        ChainFrom(w, from, Math.Max(1, amount), Math.Max(1, e.Count), type, true, depth, Math.Max(2500, e.RadiusMilli));
                        _linkOverrideType = prevType;
                        break;
                    }
                case EffectType.ApplyStatus:
                case EffectType.Mark:
                    {
                        var st = e.Type == EffectType.Mark ? StatusType.Mark : e.Status;
                        var targets = new List<Unit>(ResolveTargets(e.Target, t, e.Count));
                        foreach (var u in targets)
                        {
                            if (u.Team == 1) ApplyStatus(u, new StatusApply { Status = st, ChancePermille = 1000, Stacks = e.Stacks, DurationTicks = e.DurationTicks }, w, true, depth, 0);
                            else if (u.IsHero) ApplyStatus(u, new StatusApply { Status = st, ChancePermille = 1000, Stacks = e.Stacks, DurationTicks = e.DurationTicks }, null, true, depth, 0);
                        }
                        break;
                    }
                case EffectType.Shield:
                    GainShield(e.FromMaxHp ? FMath.Permille(Hero.MaxHp, e.Permille) + e.Flat : amount, w != null ? w.Index : -1);
                    break;
                case EffectType.Heal:
                    HealHero(e.FromMaxHp ? FMath.Permille(Hero.MaxHp, e.Permille) + e.Flat : amount, w != null ? w.Index : -1);
                    break;
                case EffectType.ExtraAttack:
                    {
                        if (w == null) break;
                        var targets = ResolveTargets(e.Target, t, 1);
                        var target = targets.Count > 0 && targets[0].Team == 1 ? targets[0] : NearestEnemy();
                        if (target == null) break;
                        int step = w.StepIndex % w.Spec.Steps.Count;
                        Emit(CombatEventType.AttackRelease, Hero.Id, target.Id, step, e.Permille, w.Index, Hero.X, "rune_extra", false, true);
                        PerformAttack(w, step, target, e.Permille > 0 ? e.Permille : 1000, true, depth);
                        break;
                    }
                case EffectType.Projectile:
                    {
                        if (w == null) break;
                        var targets = new List<Unit>(ResolveTargets(e.Target, t, e.Count));
                        if (targets.Count == 0) break;
                        AttackStep step = null;
                        foreach (var s in w.Spec.Steps) if (s.Kind == StepKind.Projectile) { step = s; break; }
                        if (step == null) step = w.Spec.Steps[0];
                        for (int k = 0; k < Math.Max(1, e.Count); k++)
                        {
                            var target = targets[k % targets.Count];
                            if (target.Team != 1) continue;
                            FireProjectile(w, target, Math.Max(1, amount), true, depth, step, k);
                        }
                        break;
                    }
                case EffectType.Haste:
                    ApplyStatus(Hero, new StatusApply { Status = StatusType.Haste, ChancePermille = 1000, Stacks = Math.Max(1, e.Stacks), DurationTicks = e.DurationTicks }, null, true, depth, 0);
                    break;
                case EffectType.Fortify:
                    ApplyStatus(Hero, new StatusApply { Status = StatusType.Fortify, ChancePermille = 1000, Stacks = 1, DurationTicks = e.DurationTicks }, null, true, depth, 0);
                    break;
                case EffectType.CooldownReduce:
                    for (int i = 0; i < Weapons.Length; i++)
                    {
                        var ww = Weapons[i];
                        int reduce = e.Flat > 0 ? e.Flat * 1000 : FMath.Permille(ww.Cooldown, e.Permille);
                        ww.Cooldown = Math.Max(0, ww.Cooldown - reduce);
                    }
                    break;
                case EffectType.Summon:
                    if (w != null && CountFamiliars() < Setup.MaxFamiliars) SpawnFamiliar(w, Math.Max(1, amount));
                    break;
                case EffectType.Execute:
                    {
                        var targets = ResolveTargets(e.Target, t, 1);
                        if (targets.Count == 0) break;
                        var u = targets[0];
                        if (u.Team != 1 || !u.Alive) break;
                        int threshold = e.Permille > 0 ? e.Permille : 150;
                        if (!u.IsBoss && u.Hp * 1000L < (long)u.MaxHp * threshold)
                        {
                            Emit(CombatEventType.SignatureFire, Hero.Id, u.Id, 0, u.Hp, w != null ? w.Index : -1, u.X, "execute", false, true);
                            DealDamage(Hero, u, u.Hp + u.Shield + 1, DamageType.True, w != null ? w.Index : -1, true, depth, 0, 1000, 0, HitFlags.Secondary | HitFlags.NoCrit, _activeOriginRune, out c);
                        }
                        else if (u.IsBoss && u.Hp * 1000L < (long)u.MaxHp * threshold)
                            DealDamage(Hero, u, Math.Max(1, baseDmg * 2), DamageType.True, w != null ? w.Index : -1, true, depth, 0, 1000, 0, HitFlags.Secondary | HitFlags.NoCrit, _activeOriginRune, out c);
                        break;
                    }
                case EffectType.Pull:
                case EffectType.Knockback:
                    {
                        var targets = new List<Unit>(ResolveTargets(e.Target, t, e.Count));
                        foreach (var u in targets)
                            if (u.Team == 1 && !u.IsBoss) Push(u, e.Type == EffectType.Pull ? -e.RadiusMilli : e.RadiusMilli);
                        break;
                    }
                case EffectType.Cleanse:
                    {
                        StatusType[] debuffs = { StatusType.Burn, StatusType.Poison, StatusType.Curse, StatusType.Weaken, StatusType.Slow, StatusType.Chill, StatusType.Bleed, StatusType.Stun };
                        foreach (var d in debuffs)
                        {
                            if (Hero.Status.Has(d)) { Hero.Status.Clear(d); Emit(CombatEventType.StatusExpired, Hero.Id, Hero.Id, (int)d, 0, -1, Hero.X, "cleanse"); break; }
                        }
                        break;
                    }
                case EffectType.Gold:
                    _goldBonus = Math.Min(30, _goldBonus + Math.Max(1, e.Flat));
                    break;
                case EffectType.SpreadStatus:
                    {
                        var targets = ResolveTargets(e.Target, t, 1);
                        if (targets.Count > 0 && w != null && targets[0].Team == 1) SpreadStatuses(targets[0], w, e.RadiusMilli);
                        break;
                    }
                case EffectType.Detonate:
                    {
                        var targets = ResolveTargets(e.Target, t, 1);
                        if (targets.Count == 0) break;
                        var u = targets[0];
                        if (u.Team != 1 || !u.Alive || e.Status == StatusType.None) break;
                        int stacks = u.Status.Get(e.Status);
                        if (stacks <= 0) break;
                        u.Status.Clear(e.Status);
                        Emit(CombatEventType.StatusExpired, Hero.Id, u.Id, (int)e.Status, stacks, w != null ? w.Index : -1, u.X, "detonate");
                        DealDamage(Hero, u, Math.Max(1, amount * stacks), type, w != null ? w.Index : -1, true, depth, 0, 1000, 0, HitFlags.Secondary | HitFlags.NoCrit, _activeOriginRune, out c);
                        break;
                    }
            }
        }

        /// <summary>A charged board link fires: destination weapon performs a capped proc attack, or a terminal relay resolves.</summary>
        public void FireLink(CompiledLink link, Unit target, int depth)
        {
            if (!Hero.Alive) return;
            if (_procsThisTick >= Setup.MaxProcsPerTick) { Report.ProcsSuppressedTickCap++; return; }
            _procsThisTick++;
            int fires;
            Report.LinkFires.TryGetValue(link.Index, out fires);
            Report.LinkFires[link.Index] = fires + 1;
            long before = Report.TotalDamage;
            var src = Weapons[link.SourceWeapon];

            if (link.DestWeapon >= 0)
            {
                var w = Weapons[link.DestWeapon];
                var step = w.Spec.Steps[w.StepIndex % w.Spec.Steps.Count];
                bool melee = step.Kind == StepKind.Melee;
                if (target == null || !CanWeaponHit(w.Spec, target, melee) || Gap(Hero, target) > w.Spec.RangeMilli + 1500)
                    target = SelectTarget(w.Spec, Setup.Hero.Targeting, w.Spec.RangeMilli + 1500, melee);
                if (target == null) return;
                Emit(CombatEventType.LinkFire, Hero.Id, target.Id, link.Index, link.PowerPermille, w.Index, Hero.X, link.Description, false, true);
                if (link.Beacon) ApplyStatus(target, new StatusApply { Status = StatusType.Mark, ChancePermille = 1000, Stacks = 1, DurationTicks = 120 }, w, true, depth, 0);
                int prevType = _linkOverrideType;
                var prevStatus = _linkExtraStatus;
                _linkOverrideType = link.ConvertDamageType;
                _linkExtraStatus = link.AddStatus;
                var savedType = w.Spec.DamageType;
                if (link.ConvertDamageType >= 0) w.Spec.DamageType = (DamageType)link.ConvertDamageType;
                try
                {
                    PerformAttack(w, w.StepIndex % w.Spec.Steps.Count, target, link.PowerPermille, true, depth);
                    if (link.AddStatus != null && target.Alive) ApplyStatus(target, link.AddStatus, w, true, depth, 0);
                }
                finally
                {
                    w.Spec.DamageType = savedType;
                    _linkOverrideType = prevType;
                    _linkExtraStatus = prevStatus;
                }
            }
            else
            {
                if (target == null || !target.Alive) target = NearestEnemy();
                int amount = FMath.Permille(src.Spec.BaseDamage, link.PowerPermille);
                Emit(CombatEventType.LinkFire, Hero.Id, target != null ? target.Id : 0, link.Index, link.PowerPermille, -1, Hero.X, link.Description, false, true);
                if (link.CapacitorNova && target != null)
                {
                    var type = link.ConvertDamageType >= 0 ? (DamageType)link.ConvertDamageType : src.Spec.DamageType;
                    Emit(CombatEventType.ZoneSpawn, Hero.Id, target.Id, link.RadiusMilli, (int)type, src.Index, target.X, "capacitor", false, true);
                    var hit = new List<Unit>();
                    for (int i = 0; i < Units.Count; i++)
                    {
                        var u = Units[i];
                        if (u.Team == 1 && u.Targetable && FMath.Abs(u.X - target.X) <= link.RadiusMilli + u.Radius) hit.Add(u);
                    }
                    foreach (var u in hit)
                    {
                        bool c;
                        DealDamage(Hero, u, Math.Max(1, amount), type, src.Index, true, depth, 0, 1000, 0, HitFlags.Secondary | HitFlags.NoCrit, -1, out c);
                        if (link.AddStatus != null && u.Alive) ApplyStatus(u, link.AddStatus, src, true, depth, 0);
                    }
                }
                if (link.Beacon && target != null)
                    ApplyStatus(target, new StatusApply { Status = StatusType.Mark, ChancePermille = 1000, Stacks = 1, DurationTicks = 150 }, src, true, depth, 0);
                if (link.Siphon && !link.CapacitorNova) GainShield(Math.Max(1, amount), src.Index);
            }
            long gained = Report.TotalDamage - before;
            long cur;
            Report.LinkDamage.TryGetValue(link.Index, out cur);
            Report.LinkDamage[link.Index] = cur + gained;
            if (link.Siphon && gained > 0) HealHero((int)Math.Min(int.MaxValue, gained / 10), link.DestWeapon);
        }

        // ------------------------------------------------------------------ legendary signatures

        private void OnAttackSignatures(WeaponRuntime w, Unit target)
        {
            var spec = w.Spec;
            switch (spec.Signature)
            {
                case LegendarySignature.WaveOnAttack:
                    SpawnWave(w, FMath.Permille(spec.BaseDamage, Math.Max(400, spec.SignatureValue)), true, 1, 45);
                    Emit(CombatEventType.SignatureFire, Hero.Id, target.Id, (int)spec.Signature, 0, w.Index, Hero.X, "wave");
                    break;
                case LegendarySignature.QuakeEveryThird:
                    if (w.AttackCount % 3 == 0)
                    {
                        Emit(CombatEventType.SignatureFire, Hero.Id, target.Id, (int)spec.Signature, 0, w.Index, target.X, "quake");
                        for (int i = 0; i < Units.Count; i++)
                        {
                            var u = Units[i];
                            if (u.Team != 1 || !u.Targetable || u.Flying || FMath.Abs(u.X - target.X) > 2500) continue;
                            HitUnit(w, u, FMath.Permille(spec.BaseDamage, Math.Max(600, spec.SignatureValue)), true, 1, null, HitFlags.Secondary);
                            if (u.Alive && !u.IsBoss) ApplyStatus(u, new StatusApply { Status = StatusType.Stun, ChancePermille = 1000, Stacks = 1, DurationTicks = 24 }, w, true, 1, 0);
                        }
                    }
                    break;
                case LegendarySignature.Mirror:
                    {
                        var far = SelectTarget(spec, TargetPriority.Farthest, spec.RangeMilli + 3000, false);
                        if (far != null && far != target)
                        {
                            Emit(CombatEventType.SignatureFire, Hero.Id, far.Id, (int)spec.Signature, 0, w.Index, far.X, "mirror");
                            PerformAttack(w, (w.StepIndex + spec.Steps.Count - 1) % spec.Steps.Count, far, Math.Max(500, spec.SignatureValue), true, 1);
                        }
                        break;
                    }
                case LegendarySignature.Gearstorm:
                    {
                        int cogs = Math.Max(1, spec.SignatureValue);
                        AttackStep step = null;
                        foreach (var s in spec.Steps) if (s.Kind == StepKind.Projectile) { step = s; break; }
                        if (step == null) step = new AttackStep { Kind = StepKind.Projectile };
                        for (int k = 0; k < cogs; k++)
                        {
                            var t = NearestEnemyTo(target.X + k * 600, -1);
                            if (t != null) FireProjectile(w, t, FMath.Permille(spec.BaseDamage, 350), true, 1, step, k);
                        }
                        break;
                    }
            }
        }

        private void UpdateSignatures()
        {
            if (!Hero.Alive) return;
            for (int i = 0; i < Weapons.Length; i++)
            {
                var w = Weapons[i];
                var spec = w.Spec;
                if (spec.Signature == LegendarySignature.None) continue;
                w.SignatureTimer++;
                switch (spec.Signature)
                {
                    case LegendarySignature.Singularity:
                        if (w.SignatureTimer >= Math.Max(120, spec.SignatureValue))
                        {
                            w.SignatureTimer = 0;
                            var center = SelectTarget(spec, TargetPriority.Cluster, spec.RangeMilli + 4000, false);
                            if (center != null)
                            {
                                AddZone(ZoneKind.Singularity, center.X, 2200, 75, 15, FMath.Permille(spec.BaseDamage, 300), DamageType.Arcane, i, true, 1, null);
                                Emit(CombatEventType.SignatureFire, Hero.Id, center.Id, (int)spec.Signature, 0, i, center.X, "singularity");
                            }
                        }
                        break;
                    case LegendarySignature.Thunderhead:
                        if (w.SignatureTimer >= Math.Max(18, spec.SignatureValue))
                        {
                            w.SignatureTimer = 0;
                            var pool = new List<Unit>();
                            for (int k = 0; k < Units.Count; k++) if (Units[k].Team == 1 && Units[k].Targetable) pool.Add(Units[k]);
                            if (pool.Count > 0)
                            {
                                var t = pool[_rng.Next(pool.Count)];
                                Emit(CombatEventType.SignatureFire, Hero.Id, t.Id, (int)spec.Signature, 0, i, t.X, "thunder");
                                HitUnit(w, t, FMath.Permille(spec.BaseDamage, 700), true, 1, null, HitFlags.Secondary);
                                if (t.Alive) ApplyStatus(t, new StatusApply { Status = StatusType.Shock, ChancePermille = 1000, Stacks = 1, DurationTicks = 90 }, w, true, 1, 0);
                            }
                        }
                        break;
                    case LegendarySignature.Sanctuary:
                        if (w.SignatureTimer >= 60)
                        {
                            w.SignatureTimer = 0;
                            if (Hero.Shield * 2 >= Hero.MaxHp) HealHero(Math.Max(1, Hero.MaxHp * 30 / 1000), i);
                            else GainShield(Math.Max(1, Hero.MaxHp * Math.Max(30, spec.SignatureValue) / 1000), i);
                            Emit(CombatEventType.SignatureFire, Hero.Id, Hero.Id, (int)spec.Signature, 0, i, Hero.X, "sanctuary");
                        }
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ post-battle insights (grounded in logged data)

        private void BuildInsights()
        {
            long total = Report.TotalDamage;
            for (int i = 0; i < Weapons.Length; i++)
            {
                var ws = Report.Weapons[i];
                if (total > 0 && ws.Damage * 10 < total) Report.Insights.Add("insight.weapon_low_share|" + Weapons[i].Spec.Frame.Id + "|" + (ws.Damage * 100 / Math.Max(1, total)));
                if (ws.Attacks > 0 && ws.Hits == 0) Report.Insights.Add("insight.weapon_never_hit|" + Weapons[i].Spec.Frame.Id);
            }
            foreach (var rr in _runes)
            {
                int n;
                Report.RuneTriggers.TryGetValue(rr.Key, out n);
                if (n == 0) Report.Insights.Add("insight.rune_never_triggered|" + rr.Key);
                int icd;
                if (Report.RuneFailures.TryGetValue(rr.Key + ":icd", out icd) && icd > n * 3 && icd > 10) Report.Insights.Add("insight.rune_icd_wasted|" + rr.Key + "|" + icd);
            }
            foreach (var l in Links)
            {
                int fires;
                Report.LinkFires.TryGetValue(l.Index, out fires);
                if (fires == 0) Report.Insights.Add("insight.link_never_fired|" + l.Index + "|" + l.Listen);
            }
            if (Report.ProcsSuppressedTickCap + Report.ProcsSuppressedDepth > 0)
                Report.Insights.Add("insight.proc_caps_hit|" + (Report.ProcsSuppressedTickCap + Report.ProcsSuppressedDepth));
            if (Report.DeadLinks.Count > 0) Report.Insights.Add("insight.dead_links|" + Report.DeadLinks.Count);
            if (!Hero.Alive || Report.KilledBy != null)
            {
                string top = null;
                long topV = 0;
                foreach (var kv in Report.DamageTakenBySource) if (kv.Value > topV || (kv.Value == topV && string.CompareOrdinal(kv.Key, top) < 0)) { top = kv.Key; topV = kv.Value; }
                if (top != null) Report.Insights.Add("insight.top_damage_source|" + top + "|" + topV);
            }
        }
    }
}
