using System;
using System.Collections.Generic;
using System.Text;

namespace Forgebound.Core.Content
{
    public sealed class ValidationIssue
    {
        public string Severity;   // error | warning
        public string Code;
        public string Subject;
        public string Message;
        public override string ToString() { return Severity.ToUpperInvariant() + " [" + Code + "] " + Subject + ": " + Message; }
    }

    public sealed class ValidationReport
    {
        public readonly List<ValidationIssue> Issues = new List<ValidationIssue>();
        public int ErrorCount { get { int n = 0; foreach (var i in Issues) if (i.Severity == "error") n++; return n; } }
        public int WarningCount { get { int n = 0; foreach (var i in Issues) if (i.Severity == "warning") n++; return n; } }
        public void Error(string code, string subject, string msg) { Issues.Add(new ValidationIssue { Severity = "error", Code = code, Subject = subject, Message = msg }); }
        public void Warn(string code, string subject, string msg) { Issues.Add(new ValidationIssue { Severity = "warning", Code = code, Subject = subject, Message = msg }); }

        public JsonValue ToJson()
        {
            var arr = JsonValue.Arr();
            foreach (var i in Issues) arr.Add(JsonValue.Obj().Set("severity", i.Severity).Set("code", i.Code).Set("subject", i.Subject).Set("message", i.Message));
            return JsonValue.Obj().Set("errors", ErrorCount).Set("warnings", WarningCount).Set("issues", arr);
        }
    }

    /// <summary>
    /// Static content validation: ids, references, impossible recipes, dead runes, duplicate clones, outliers,
    /// impossible encounters, unbounded proc loops (via <see cref="ProcGraphAnalyzer"/>), localisation coverage.
    /// Used by the Unity editor menu, the CLI (`forgebound validate`) and the test suite (content must have 0 errors).
    /// </summary>
    public static class ContentValidator
    {
        public static ValidationReport Validate(ContentDatabase db, Dictionary<string, string> locEn = null, Dictionary<string, string> locVi = null)
        {
            var r = new ValidationReport();
            foreach (var e in db.LoadErrors) r.Error("load", "content", e);
            CheckFrames(db, r);
            CheckPieces(db, r);
            CheckModifiers(db, r);
            CheckRunes(db, r);
            CheckLegendaries(db, r);
            CheckForgemasters(db, r);
            CheckEnemies(db, r);
            CheckEncounters(db, r);
            CheckEvents(db, r);
            CheckBiomes(db, r);
            CheckChallenges(db, r);
            CheckBlessings(db, r);
            foreach (var issue in ProcGraphAnalyzer.Analyze(db).Issues) r.Issues.Add(issue);
            if (locEn != null) CheckLocalization(db, r, locEn, "en");
            if (locVi != null) CheckLocalization(db, r, locVi, "vi");
            return r;
        }

        public static IEnumerable<string> RequiredLocKeys(ContentDatabase db)
        {
            foreach (var x in db.Frames) yield return "frame." + x.Id + ".name";
            foreach (var x in db.Cores) yield return "core." + x.Id + ".name";
            foreach (var x in db.Modifiers) yield return "modifier." + x.Id + ".name";
            foreach (var x in db.Runes) yield return "rune." + x.Id + ".name";
            foreach (var x in db.Pieces) yield return "piece." + x.Id + ".name";
            foreach (var x in db.Legendaries) { yield return "legendary." + x.Id + ".name"; yield return "legendary." + x.Id + ".lore"; }
            foreach (var x in db.Forgemasters) { yield return "forgemaster." + x.Id + ".name"; yield return "forgemaster." + x.Id + ".desc"; }
            foreach (var x in db.Enemies) yield return "enemy." + x.Id + ".name";
            foreach (var x in db.Blessings) yield return "blessing." + x.Id + ".name";
            foreach (var x in db.Biomes) yield return "biome." + x.Id + ".name";
            foreach (var x in db.Challenges) { yield return "challenge." + x.Id + ".name"; yield return "challenge." + x.Id + ".desc"; }
            foreach (var x in db.Events)
            {
                yield return "event." + x.Id + ".title";
                yield return "event." + x.Id + ".body";
                foreach (var o in x.Options) yield return o.TextKey;
            }
        }

        private static void CheckLocalization(ContentDatabase db, ValidationReport r, Dictionary<string, string> loc, string lang)
        {
            int missing = 0;
            foreach (var key in RequiredLocKeys(db))
            {
                string v;
                if (!loc.TryGetValue(key, out v) || string.IsNullOrEmpty(v))
                {
                    if (missing < 40) r.Error("loc_missing", lang, key);
                    missing++;
                }
                else if (v.Length > 90 && key.EndsWith(".name")) r.Warn("loc_overflow", lang, key + " is " + v.Length + " chars (name UI budget 90)");
            }
            if (missing > 40) r.Error("loc_missing", lang, (missing - 40) + " more keys missing");
        }

        private static void CheckFrames(ContentDatabase db, ValidationReport r)
        {
            var dps = new List<long>();
            foreach (var f in db.Frames)
            {
                if (f.Footprint.Count < 2 || f.Footprint.Count > 5) r.Warn("footprint", "frame:" + f.Id, "weapon footprint should be 2-5 cells, has " + f.Footprint.Count);
                CheckShape(r, "frame:" + f.Id, f.Footprint, f.Ports);
                bool hasOut = false, hasIn = false;
                foreach (var p in f.Ports) { if (p.Kind != PortKind.In) hasOut = true; if (p.Kind != PortKind.Out) hasIn = true; }
                if (!hasOut || !hasIn) r.Warn("ports", "frame:" + f.Id, "weapon should have at least one in and one out port");
                dps.Add(EstimateDps(f));
                foreach (var s in f.Steps)
                    if (s.Kind == StepKind.Summon && s.SummonCap <= 0) r.Error("summon", "frame:" + f.Id, "summon step with cap 0");
            }
            if (dps.Count > 2)
            {
                var sorted = new List<long>(dps);
                sorted.Sort();
                long median = sorted[sorted.Count / 2];
                for (int i = 0; i < db.Frames.Count; i++)
                {
                    if (dps[i] * 10 > median * 18) r.Warn("outlier", "frame:" + db.Frames[i].Id, "estimated single-target DPS " + dps[i] / 100.0 + " is >1.8x median " + median / 100.0);
                    if (dps[i] * 10 < median * 5) r.Warn("outlier", "frame:" + db.Frames[i].Id, "estimated single-target DPS " + dps[i] / 100.0 + " is <0.5x median " + median / 100.0);
                }
            }
        }

        /// <summary>Rough single-target DPS x100 (estimate only; the balance simulator measures real values).</summary>
        public static long EstimateDps(FrameDef f)
        {
            long dmg = 0, ticks = 0;
            foreach (var s in f.Steps)
            {
                dmg += (long)f.BaseDamage * s.DamagePermille * s.Hits;
                int cd = s.CooldownTicks >= 0 ? s.CooldownTicks : f.CooldownTicks;
                int wu = s.WindupTicks >= 0 ? s.WindupTicks : f.WindupTicks;
                ticks += cd + wu;
            }
            if (ticks == 0) return 0;
            return dmg * 30 / 10 / ticks;
        }

        private static void CheckShape(ValidationReport r, string subject, List<Cell> cells, List<PortDef> ports)
        {
            var set = new HashSet<Cell>();
            foreach (var c in cells) if (!set.Add(c)) r.Error("footprint", subject, "duplicate cell " + c);
            foreach (var p in ports)
                if (p.CellIndex < 0 || p.CellIndex >= cells.Count) r.Error("ports", subject, "port references cell " + p.CellIndex + " outside footprint");
            // Ports must face outward (not into the piece's own cell).
            foreach (var p in ports)
            {
                if (p.CellIndex < 0 || p.CellIndex >= cells.Count) continue;
                var c = cells[p.CellIndex];
                int[] dx = { 0, 1, 0, -1 }, dy = { -1, 0, 1, 0 };
                if (set.Contains(new Cell(c.X + dx[(int)p.Dir], c.Y + dy[(int)p.Dir]))) r.Error("ports", subject, "port on cell " + p.CellIndex + " faces into its own footprint");
            }
        }

        private static void CheckPieces(ContentDatabase db, ValidationReport r)
        {
            var sigs = new Dictionary<string, string>();
            foreach (var p in db.Pieces)
            {
                CheckShape(r, "piece:" + p.Id, p.Footprint, p.Ports);
                if (p.Footprint.Count > 2) r.Warn("footprint", "piece:" + p.Id, "relay/support pieces should be 1-2 cells");
                if (p.Kind == PieceKind.Relay && p.Relay != null)
                {
                    bool hasIn = false, hasOut = false;
                    foreach (var q in p.Ports) { if (q.Kind != PortKind.Out) hasIn = true; if (q.Kind != PortKind.In) hasOut = true; }
                    if (!hasIn) r.Error("dead_circuit", "piece:" + p.Id, "relay has no input port; it can never be part of a link");
                    bool terminal = p.Relay.Type == RelayType.Capacitor || p.Relay.Type == RelayType.Siphon || p.Relay.Type == RelayType.Beacon;
                    if (!hasOut && !terminal) r.Error("dead_circuit", "piece:" + p.Id, "non-terminal relay without output port");
                    if (p.Relay.Type == RelayType.Converter && p.Relay.ConvertDamageType < 0 && p.Relay.Status == null) r.Error("relay", "piece:" + p.Id, "converter without conversion or status");
                }
                string sig = p.Kind + "|" + (p.Relay != null ? p.Relay.Type + ":" + p.Relay.Listen + ":" + p.Relay.Threshold + ":" + p.Relay.Permille + ":" + p.Relay.ConvertDamageType : "") + "|" + StatsSig(p.AdjacentStats) + "|" + p.Footprint.Count + "|" + PortsSig(p.Ports);
                string other;
                if (sigs.TryGetValue(sig, out other)) r.Error("clone", "piece:" + p.Id, "identical behaviour to piece:" + other);
                else sigs[sig] = p.Id;
            }
        }

        private static string PortsSig(List<PortDef> ports)
        {
            var sb = new StringBuilder();
            foreach (var p in ports) sb.Append(p.CellIndex).Append(p.Dir).Append(p.Kind).Append(';');
            return sb.ToString();
        }

        private static string StatsSig(List<StatMod> stats)
        {
            var parts = new List<string>();
            foreach (var s in stats) parts.Add(s.Stat + ":" + s.Value + ":" + s.Tag);
            parts.Sort(StringComparer.Ordinal);
            return string.Join(",", parts);
        }

        private static readonly HashSet<string> KnownFrameTags = new HashSet<string>();

        private static HashSet<string> AllWeaponTags(ContentDatabase db)
        {
            var tags = new HashSet<string>();
            foreach (var f in db.Frames) foreach (var t in f.Tags) tags.Add(t);
            foreach (var c in db.Cores) foreach (var t in c.Tags) tags.Add(t);
            return tags;
        }

        private static void CheckModifiers(ContentDatabase db, ValidationReport r)
        {
            var tags = AllWeaponTags(db);
            var sigs = new Dictionary<string, string>();
            foreach (var m in db.Modifiers)
            {
                foreach (var t in m.RequiresTags) if (!tags.Contains(t)) r.Error("impossible", "modifier:" + m.Id, "requires tag '" + t + "' that no frame/core has");
                bool anyFrame = false;
                foreach (var f in db.Frames)
                {
                    var ft = new List<string>(f.Tags);
                    bool ok = m.RequiresTags.Count == 0;
                    foreach (var t in m.RequiresTags) if (ft.Contains(t)) ok = true;
                    foreach (var t in m.ExcludesTags) if (ft.Contains(t)) ok = false;
                    if (ok) anyFrame = true;
                }
                if (!anyFrame && m.RequiresTags.Count > 0)
                {
                    // could still be satisfied by a core tag
                    bool coreOk = false;
                    foreach (var c in db.Cores) foreach (var t in m.RequiresTags) if (c.Tags.Contains(t)) coreOk = true;
                    if (!coreOk) r.Error("impossible", "modifier:" + m.Id, "no frame can ever socket this modifier");
                }
                var ops = new List<string>();
                foreach (var o in m.Ops) ops.Add(o.Op + ":" + o.Value + ":" + o.Tag);
                ops.Sort(StringComparer.Ordinal);
                string sig = string.Join(",", ops) + "|" + string.Join(",", m.RequiresTags);
                string other;
                if (sigs.TryGetValue(sig, out other)) r.Error("clone", "modifier:" + m.Id, "identical ops to modifier:" + other);
                else sigs[sig] = m.Id;
                foreach (var o in m.Ops)
                {
                    if (o.Op == ModOp.None) r.Error("op", "modifier:" + m.Id, "unknown op");
                    if ((o.Op == ModOp.CooldownPermille || o.Op == ModOp.DamagePermille || o.Op == ModOp.SplitDamage) && (o.Value < 200 || o.Value > 3000))
                        r.Warn("outlier", "modifier:" + m.Id, o.Op + " " + o.Value + " outside 200..3000 permille");
                    if (o.Op == ModOp.OrbitBlades) r.Error("unimplemented", "modifier:" + m.Id, "OrbitBlades op has no runtime implementation");
                }
            }
        }

        /// <summary>Status types that some content can actually apply (to enemies).</summary>
        public static HashSet<StatusType> ProducibleStatuses(ContentDatabase db)
        {
            var s = new HashSet<StatusType> { StatusType.Stun, StatusType.ArmorBreak, StatusType.Bleed, StatusType.Freeze, StatusType.Slow, StatusType.Mark };
            Action<StatusApply> add = a => { if (a != null && a.Status != StatusType.None) s.Add(a.Status); };
            Action<List<RuneDef>> runes = list => { foreach (var rr in list) foreach (var e in rr.Effects) { if (e.Status != StatusType.None) s.Add(e.Status); if (e.Type == EffectType.Mark) s.Add(StatusType.Mark); } };
            foreach (var c in db.Cores) { add(c.Status); runes(c.InnateRunes); }
            foreach (var f in db.Frames) foreach (var st in f.Steps) add(st.Status);
            runes(db.Runes);
            foreach (var l in db.Legendaries) runes(l.InnateRunes);
            foreach (var p in db.Pieces) if (p.Relay != null) add(p.Relay.Status);
            foreach (var b in db.Blessings) runes(b.Runes);
            foreach (var f in db.Forgemasters) runes(f.InnateRunes);
            return s;
        }

        private static void CheckRuneDef(ContentDatabase db, ValidationReport r, RuneDef rd, string subject, HashSet<StatusType> producible)
        {
            var c = rd.Condition;
            if (rd.Trigger == TriggerType.OnStatusApplied && c.StatusIs != StatusType.None && !producible.Contains(c.StatusIs))
                r.Error("dead_rune", subject, "listens for status " + c.StatusIs + " that no content applies");
            if (c.TargetHasStatus != StatusType.None && !producible.Contains(c.TargetHasStatus))
                r.Error("dead_rune", subject, "requires target status " + c.TargetHasStatus + " that no content applies");
            if (rd.ChancePermille <= 0) r.Error("dead_rune", subject, "chance is 0");
            if (rd.MaxPerBattle < 0) r.Error("dead_rune", subject, "negative max per battle");
            foreach (var e in rd.Effects)
            {
                if ((e.Type == EffectType.Damage || e.Type == EffectType.Nova || e.Type == EffectType.Chain || e.Type == EffectType.Detonate) && e.Permille <= 0 && e.Flat <= 0)
                    r.Error("no_effect", subject, e.Type + " effect with zero power");
                if ((e.Type == EffectType.ApplyStatus || e.Type == EffectType.Detonate) && e.Status == StatusType.None)
                    r.Error("no_effect", subject, e.Type + " without status");
                if ((e.Type == EffectType.Shield || e.Type == EffectType.Heal) && e.Permille <= 0 && e.Flat <= 0) r.Error("no_effect", subject, e.Type + " with zero amount");
                if (e.Type == EffectType.Damage && e.Target == EffectTarget.Self) r.Error("no_effect", subject, "damage effect targeting self is ignored by the engine");
            }
        }

        private static void CheckRunes(ContentDatabase db, ValidationReport r)
        {
            var producible = ProducibleStatuses(db);
            var sigs = new Dictionary<string, string>();
            foreach (var rd in db.Runes)
            {
                CheckRuneDef(db, r, rd, "rune:" + rd.Id, producible);
                var sb = new StringBuilder();
                sb.Append(rd.Trigger).Append('|').Append(rd.Condition.EveryN).Append(rd.Condition.StatusIs).Append(rd.Condition.TargetHasStatus).Append(rd.ChancePermille).Append('|');
                foreach (var e in rd.Effects) sb.Append(e.Type).Append(e.Target).Append(e.Permille).Append(e.Flat).Append(e.Status).Append(e.Count).Append(e.DamageType).Append(';');
                string other;
                if (sigs.TryGetValue(sb.ToString(), out other)) r.Error("clone", "rune:" + rd.Id, "identical behaviour to rune:" + other);
                else sigs[sb.ToString()] = rd.Id;
            }
            foreach (var c in db.Cores) foreach (var rd in c.InnateRunes) CheckRuneDef(db, r, rd, "core:" + c.Id + "/" + rd.Id, producible);
            foreach (var l in db.Legendaries) foreach (var rd in l.InnateRunes) CheckRuneDef(db, r, rd, "legendary:" + l.Id + "/" + rd.Id, producible);
            foreach (var b in db.Blessings) foreach (var rd in b.Runes) CheckRuneDef(db, r, rd, "blessing:" + b.Id + "/" + rd.Id, producible);
            foreach (var f in db.Forgemasters) foreach (var rd in f.InnateRunes) CheckRuneDef(db, r, rd, "forgemaster:" + f.Id + "/" + rd.Id, producible);
        }

        private static void CheckLegendaries(ContentDatabase db, ValidationReport r)
        {
            var seenCombos = new Dictionary<string, string>();
            foreach (var l in db.Legendaries)
            {
                string subj = "legendary:" + l.Id;
                var frame = db.Frame(l.Frame);
                if (frame == null) { r.Error("ref", subj, "unknown frame " + l.Frame); continue; }
                CoreDef core = null;
                if (l.Core != null) { core = db.Core(l.Core); if (core == null) r.Error("ref", subj, "unknown core " + l.Core); }
                var tags = new List<string>(frame.Tags);
                if (core != null) tags.AddRange(core.Tags);
                int slotCost = 0;
                var groups = new HashSet<string>();
                foreach (var mid in l.RequiresModifiers)
                {
                    var m = db.Modifier(mid);
                    if (m == null) { r.Error("ref", subj, "unknown modifier " + mid); continue; }
                    bool ok = m.RequiresTags.Count == 0;
                    foreach (var t in m.RequiresTags) if (tags.Contains(t)) ok = true;
                    foreach (var t in m.ExcludesTags) if (tags.Contains(t)) ok = false;
                    if (!ok) r.Error("impossible_recipe", subj, "modifier " + mid + " cannot be socketed into " + l.Frame);
                    if (m.ConflictGroup != null && !groups.Add(m.ConflictGroup)) r.Error("impossible_recipe", subj, "required modifiers conflict (" + m.ConflictGroup + ")");
                    slotCost += m.SlotCost;
                }
                int slots = frame.ModifierSlots + (l.MinTier >= 2 ? 1 : 0);
                if (slotCost > slots) r.Error("impossible_recipe", subj, "needs " + slotCost + " modifier slots but frame has " + slots + " at tier " + l.MinTier);
                foreach (var rid in l.RequiresAnyRunes) if (db.Rune(rid) == null) r.Error("ref", subj, "unknown rune " + rid);
                if (l.MinTier < 1 || l.MinTier > 3) r.Error("impossible_recipe", subj, "minTier out of range");
                if (l.EssenceCost < 1 || l.EssenceCost > 6) r.Warn("economy", subj, "essence cost " + l.EssenceCost + " outside 1..6");
                string combo = l.Frame + "|" + l.Core + "|" + string.Join("+", l.RequiresModifiers) + "|" + string.Join("+", l.RequiresAnyRunes);
                string other;
                if (seenCombos.TryGetValue(combo, out other)) r.Error("ambiguous_recipe", subj, "same requirements as legendary:" + other);
                else seenCombos[combo] = l.Id;
            }
            foreach (var f in db.Frames)
            {
                int n = 0;
                foreach (var l in db.Legendaries) if (l.Frame == f.Id) n++;
                if (n == 0) r.Warn("coverage", "frame:" + f.Id, "no legendary evolution path");
            }
        }

        private static void CheckItemRef(ContentDatabase db, ValidationReport r, string subject, string spec)
        {
            var parts = spec.Split(':');
            ItemCategory cat;
            if (parts.Length != 2 || !Enum.TryParse(parts[0], true, out cat) || !db.ItemExists(cat, parts[1])) r.Error("ref", subject, "bad item reference '" + spec + "'");
        }

        private static void CheckForgemasters(ContentDatabase db, ValidationReport r)
        {
            foreach (var f in db.Forgemasters)
            {
                string s = "forgemaster:" + f.Id;
                if (f.StartFrame == null || db.Frame(f.StartFrame) == null) r.Error("ref", s, "missing/unknown start frame");
                if (f.StartCore != null && db.Core(f.StartCore) == null) r.Error("ref", s, "unknown start core " + f.StartCore);
                foreach (var p in f.StartPieces) if (db.Piece(p) == null) r.Error("ref", s, "unknown start piece " + p);
                foreach (var i in f.StartItems) CheckItemRef(db, r, s, i);
                if (f.MaxHp < 50) r.Warn("outlier", s, "very low max HP");
            }
        }

        private static void CheckEnemies(ContentDatabase db, ValidationReport r)
        {
            var sigs = new Dictionary<string, string>();
            foreach (var e in db.Enemies)
            {
                string s = "enemy:" + e.Id;
                foreach (var ab in e.Abilities) CheckAbility(db, r, s, ab, e);
                foreach (var ph in e.Phases) foreach (var ab in ph.Abilities) CheckAbility(db, r, s, ab, e);
                for (int i = 1; i < e.Phases.Count; i++)
                    if (e.Phases[i].HpBelowPermille >= e.Phases[i - 1].HpBelowPermille) r.Error("phases", s, "phase thresholds must be strictly decreasing");
                if (e.Rank == EnemyRank.Boss && e.Phases.Count < 2) r.Warn("boss", s, "boss has fewer than 2 phases");
                if (e.AttackDamage <= 0) r.Error("enemy", s, "attack damage must be > 0");
                if (e.SpeedMilli <= 0) r.Error("enemy", s, "speed must be > 0");
                var abl = new List<string>();
                foreach (var ab in e.Abilities) abl.Add(ab.Type.ToString());
                abl.Sort(StringComparer.Ordinal);
                string sig = e.MaxHp + "|" + e.Armor + "|" + e.AttackDamage + "|" + e.AttackRanged + "|" + e.Flying + "|" + e.SpeedMilli + "|" + string.Join(",", abl);
                string other;
                if (sigs.TryGetValue(sig, out other)) r.Error("clone", s, "identical stat block and abilities to enemy:" + other);
                else sigs[sig] = e.Id;
            }
        }

        private static void CheckAbility(ContentDatabase db, ValidationReport r, string s, EnemyAbilityDef ab, EnemyDef owner)
        {
            if (ab.Type == EnemyAbilityType.None) r.Error("ability", s, "unknown ability type");
            if ((ab.Type == EnemyAbilityType.Summon || ab.Type == EnemyAbilityType.SplitOnDeath))
            {
                var t = db.Enemy(ab.EnemyId);
                if (t == null) r.Error("ref", s, ab.Type + " references unknown enemy '" + ab.EnemyId + "'");
                else if (t.Id == owner.Id) r.Error("unbounded", s, ab.Type + " spawns itself (unbounded growth)");
                else if (t.Rank == EnemyRank.Boss) r.Error("ability", s, "cannot summon a boss");
                if (ab.Type == EnemyAbilityType.Summon && ab.Cap <= 0) r.Error("unbounded", s, "summon without cap");
            }
            bool needsValue = ab.Type == EnemyAbilityType.Slam || ab.Type == EnemyAbilityType.Charge || ab.Type == EnemyAbilityType.Leap || ab.Type == EnemyAbilityType.Beam
                || ab.Type == EnemyAbilityType.Volley || ab.Type == EnemyAbilityType.HealAlly || ab.Type == EnemyAbilityType.ShieldAlly || ab.Type == EnemyAbilityType.DrainHero
                || ab.Type == EnemyAbilityType.ExplodeOnDeath || ab.Type == EnemyAbilityType.Barrier;
            if (needsValue && ab.Value <= 0) r.Error("ability", s, ab.Type + " has no value");
            if ((ab.Type == EnemyAbilityType.Slam || ab.Type == EnemyAbilityType.Beam) && ab.WindupTicks < 15)
                r.Warn("telegraph", s, ab.Type + " telegraph shorter than 0.5s is hard to read");
            if ((ab.Type == EnemyAbilityType.CurseHero || ab.Type == EnemyAbilityType.StatusAura) && ab.Status == null) r.Error("ability", s, ab.Type + " without status");
        }

        private static void CheckEncounters(ContentDatabase db, ValidationReport r)
        {
            foreach (var e in db.Encounters)
            {
                string s = "encounter:" + e.Id;
                if (e.Biome != null && db.Biome(e.Biome) == null) r.Error("ref", s, "unknown biome " + e.Biome);
                bool hasBoss = false, hasElite = false;
                int total = 0;
                foreach (var w in e.Waves)
                    foreach (var sp in w.Spawns)
                    {
                        var en = db.Enemy(sp.Enemy);
                        if (en == null) { r.Error("ref", s, "unknown enemy " + sp.Enemy); continue; }
                        total += sp.Count;
                        if (en.Rank == EnemyRank.Boss) hasBoss = true;
                        if (en.Rank == EnemyRank.Elite) hasElite = true;
                    }
                if (total == 0) r.Error("impossible_encounter", s, "no enemies");
                if (total > 14) r.Warn("performance", s, total + " initial enemies (mobile budget 14)");
                if (e.Kind == NodeType.Boss && !hasBoss) r.Error("impossible_encounter", s, "boss encounter without a boss enemy");
                if (e.Kind == NodeType.Elite && !hasElite && total < 4) r.Warn("elite", s, "elite encounter without elite-rank enemy");
                if (e.Kind != NodeType.Boss && hasBoss) r.Error("encounter", s, "boss enemy in a non-boss encounter");
                if (e.MinChapter > e.MaxChapter) r.Error("impossible_encounter", s, "minChapter > maxChapter");
            }
            for (int ch = 1; ch <= 3; ch++)
            {
                foreach (var b in db.Biomes)
                {
                    if (b.Order != ch) continue;
                    foreach (var kind in new[] { NodeType.Battle, NodeType.Boss })
                    {
                        int n = 0;
                        foreach (var e in db.Encounters) if (e.Kind == kind && e.Biome == b.Id && ch >= e.MinChapter && ch <= e.MaxChapter) n++;
                        if (n == 0) r.Error("impossible_encounter", "biome:" + b.Id, "no " + kind + " encounter for chapter " + ch);
                        if (kind == NodeType.Battle && n < 4) r.Warn("variety", "biome:" + b.Id, "only " + n + " battle encounters for chapter " + ch);
                    }
                }
            }
        }

        private static void CheckEvents(ContentDatabase db, ValidationReport r)
        {
            foreach (var e in db.Events)
            {
                string s = "event:" + e.Id;
                if (e.Biome != null && db.Biome(e.Biome) == null) r.Error("ref", s, "unknown biome " + e.Biome);
                bool anyFree = false;
                foreach (var o in e.Options)
                {
                    if (o.RequireGold == 0 && o.RequireEssence == 0 && o.RequireTag == null) anyFree = true;
                    var all = new List<OutcomeDef>(o.Outcomes);
                    all.AddRange(o.FailOutcomes);
                    if (o.Outcomes.Count == 0) r.Error("event", s, o.TextKey + " has no outcomes");
                    if (o.SuccessPermille < 1000 && o.FailOutcomes.Count == 0) r.Warn("event", s, o.TextKey + " can fail but has no fail outcomes");
                    foreach (var oc in all)
                    {
                        if (oc.Type == OutcomeType.None) r.Error("event", s, "unknown outcome type");
                        if (oc.Type == OutcomeType.GainItem && (oc.Id == null || !db.ItemExists(oc.Category, oc.Id))) r.Error("ref", s, "gainItem references unknown " + oc.Category + ":" + oc.Id);
                        if ((oc.Type == OutcomeType.GainBlessing || oc.Type == OutcomeType.GainCurse) && oc.Id != null && db.Blessing(oc.Id) == null) r.Error("ref", s, "unknown blessing " + oc.Id);
                        if (oc.Type == OutcomeType.Battle && oc.Id != null && db.Encounter(oc.Id) == null) r.Error("ref", s, "unknown encounter " + oc.Id);
                    }
                }
                if (!anyFree) r.Error("softlock", s, "every option has a requirement; player could be stuck");
            }
        }

        private static void CheckBiomes(ContentDatabase db, ValidationReport r)
        {
            for (int order = 1; order <= 3; order++)
            {
                bool any = false;
                foreach (var b in db.Biomes) if (b.Order == order) any = true;
                if (!any) r.Error("campaign", "biomes", "no biome for chapter order " + order);
            }
            foreach (var b in db.Biomes)
            {
                foreach (var id in b.EliteEncounters) if (db.Encounter(id) == null) r.Error("ref", "biome:" + b.Id, "unknown elite encounter " + id);
                foreach (var id in b.BossEncounters) if (db.Encounter(id) == null) r.Error("ref", "biome:" + b.Id, "unknown boss encounter " + id);
            }
        }

        private static void CheckChallenges(ContentDatabase db, ValidationReport r)
        {
            foreach (var c in db.Challenges)
            {
                string s = "challenge:" + c.Id;
                if (c.Encounter == null || db.Encounter(c.Encounter) == null) r.Error("ref", s, "unknown encounter " + c.Encounter);
                if (c.Forgemaster != null && db.Forgemaster(c.Forgemaster) == null) r.Error("ref", s, "unknown forgemaster " + c.Forgemaster);
                foreach (var i in c.GivenItems) CheckItemRef(db, r, s, i);
                foreach (var f in c.AllowedFrames) if (db.Frame(f) == null) r.Error("ref", s, "unknown allowed frame " + f);
                bool frame = false;
                foreach (var i in c.GivenItems) if (i.StartsWith("frame:")) frame = true;
                if (!frame && c.Forgemaster == null) r.Error("impossible", s, "trial gives no frame and no forgemaster start weapon");
            }
        }

        private static void CheckBlessings(ContentDatabase db, ValidationReport r)
        {
            var sigs = new Dictionary<string, string>();
            int blessings = 0, curses = 0;
            foreach (var b in db.Blessings)
            {
                if (b.IsCurse) curses++; else blessings++;
                var sb = new StringBuilder(StatsSig(b.Stats));
                foreach (var rd in b.Runes) { sb.Append('|').Append(rd.Trigger); foreach (var e in rd.Effects) sb.Append(e.Type).Append(e.Permille).Append(e.Status); }
                string other;
                if (sigs.TryGetValue(sb.ToString(), out other)) r.Error("clone", "blessing:" + b.Id, "identical effect to blessing:" + other);
                else sigs[sb.ToString()] = b.Id;
                foreach (var st in b.Stats) if (st.Value == 0) r.Error("no_effect", "blessing:" + b.Id, st.Stat + " with value 0");
            }
        }
    }

    /// <summary>
    /// Proc Graph Analyzer: builds a graph Trigger → (rune) → produced triggers and finds cycles that rely only on
    /// runtime caps to terminate. Cycles through runes that allow proc triggering are reported as warnings with the
    /// bounding mechanism, cycles with no bound at all would be errors (the engine always bounds depth, so these are
    /// flagged for designer review rather than crashes).
    /// </summary>
    public static class ProcGraphAnalyzer
    {
        public sealed class Result
        {
            public readonly List<ValidationIssue> Issues = new List<ValidationIssue>();
            public readonly List<string> Edges = new List<string>();
            public readonly List<string> Cycles = new List<string>();
        }

        public static List<TriggerType> Produces(EffectDef e)
        {
            var list = new List<TriggerType>();
            switch (e.Type)
            {
                case EffectType.Damage:
                case EffectType.Nova:
                case EffectType.Chain:
                case EffectType.Detonate:
                    list.Add(TriggerType.OnHit); list.Add(TriggerType.OnCrit); list.Add(TriggerType.OnKill); list.Add(TriggerType.OnShieldBreak);
                    break;
                case EffectType.Execute:
                    list.Add(TriggerType.OnHit); list.Add(TriggerType.OnKill);
                    break;
                case EffectType.ExtraAttack:
                case EffectType.Projectile:
                    list.Add(TriggerType.OnHit); list.Add(TriggerType.OnCrit); list.Add(TriggerType.OnKill); list.Add(TriggerType.OnStatusApplied);
                    break;
                case EffectType.ApplyStatus:
                case EffectType.Mark:
                case EffectType.SpreadStatus:
                    list.Add(TriggerType.OnStatusApplied);
                    break;
                case EffectType.Summon:
                    list.Add(TriggerType.OnSummon);
                    break;
            }
            return list;
        }

        public static Result Analyze(ContentDatabase db)
        {
            var res = new Result();
            var all = new List<KeyValuePair<string, RuneDef>>();
            foreach (var r in db.Runes) all.Add(new KeyValuePair<string, RuneDef>("rune:" + r.Id, r));
            foreach (var c in db.Cores) foreach (var r in c.InnateRunes) all.Add(new KeyValuePair<string, RuneDef>("core:" + c.Id, r));
            foreach (var l in db.Legendaries) foreach (var r in l.InnateRunes) all.Add(new KeyValuePair<string, RuneDef>("legendary:" + l.Id, r));
            foreach (var b in db.Blessings) foreach (var r in b.Runes) all.Add(new KeyValuePair<string, RuneDef>("blessing:" + b.Id, r));
            foreach (var f in db.Forgemasters) foreach (var r in f.InnateRunes) all.Add(new KeyValuePair<string, RuneDef>("forgemaster:" + f.Id, r));

            // Node = rune; edge A -> B when an effect of A produces B's trigger AND B accepts proc events.
            int n = all.Count;
            var adj = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                adj[i] = new List<int>();
                var produced = new HashSet<TriggerType>();
                foreach (var e in all[i].Value.Effects) foreach (var t in Produces(e)) produced.Add(t);
                for (int j = 0; j < n; j++)
                {
                    var b = all[j].Value;
                    if (!b.Condition.AllowFromProc) continue;
                    if (!produced.Contains(b.Trigger)) continue;
                    adj[i].Add(j);
                    res.Edges.Add(all[i].Value.Id + " -> " + b.Id);
                }
            }
            // Detect cycles (Tarjan-free simple DFS with colouring; content sizes are small).
            var color = new int[n];
            var stack = new List<int>();
            for (int i = 0; i < n; i++) if (color[i] == 0) Dfs(i, adj, color, stack, all, res);
            foreach (var cyc in res.Cycles)
                res.Issues.Add(new ValidationIssue
                {
                    Severity = "warning",
                    Code = "proc_cycle",
                    Subject = "proc-graph",
                    Message = "potential feedback loop " + cyc + " (bounded at runtime by self-trigger guard, depth cap and per-tick proc cap)"
                });
            // Unbounded single-rune risk: a proc-accepting rune with no ICD, chance 100% and AoE/chain output.
            foreach (var kv in all)
            {
                var r = kv.Value;
                if (!r.Condition.AllowFromProc || r.IcdTicks > 0 || r.ChancePermille < 1000 || r.MaxPerBattle > 0) continue;
                foreach (var e in r.Effects)
                    if (e.Type == EffectType.Nova || e.Type == EffectType.Chain)
                        res.Issues.Add(new ValidationIssue { Severity = "warning", Code = "proc_storm", Subject = kv.Key + "/" + r.Id, Message = "proc-accepting AoE rune without ICD or chance gate" });
            }
            return res;
        }

        private static void Dfs(int u, List<int>[] adj, int[] color, List<int> stack, List<KeyValuePair<string, RuneDef>> all, Result res)
        {
            color[u] = 1;
            stack.Add(u);
            foreach (var v in adj[u])
            {
                if (color[v] == 1)
                {
                    int start = stack.IndexOf(v);
                    var names = new List<string>();
                    for (int k = start; k < stack.Count; k++) names.Add(all[stack[k]].Value.Id);
                    names.Add(all[v].Value.Id);
                    if (res.Cycles.Count < 50) res.Cycles.Add(string.Join(" -> ", names));
                }
                else if (color[v] == 0) Dfs(v, adj, color, stack, all, res);
            }
            stack.RemoveAt(stack.Count - 1);
            color[u] = 2;
        }
    }
}
