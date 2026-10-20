using System;
using System.Collections.Generic;
using Forgebound.Core.Content;

namespace Forgebound.Core.Weapons
{
    /// <summary>A weapon owned during a run (or a saved blueprint): Frame + optional Core + Modifiers + Runes + tier/evolution.</summary>
    public sealed class WeaponInstance
    {
        public int Uid;
        public string FrameId;
        public string CoreId;           // null = no core yet (allowed early game)
        public readonly List<string> Modifiers = new List<string>();
        public readonly List<string> Runes = new List<string>();
        public int Tier = 1;            // 1..3 quality
        public string LegendaryId;      // set once evolved
        public bool Locked;

        public WeaponInstance Clone()
        {
            var w = new WeaponInstance { Uid = Uid, FrameId = FrameId, CoreId = CoreId, Tier = Tier, LegendaryId = LegendaryId, Locked = Locked };
            w.Modifiers.AddRange(Modifiers);
            w.Runes.AddRange(Runes);
            return w;
        }

        public string Signature()
        {
            return FrameId + "|" + (CoreId ?? "-") + "|" + string.Join("+", Modifiers) + "|" + string.Join("+", Runes) + "|t" + Tier + "|" + (LegendaryId ?? "-");
        }
    }

    /// <summary>Aggregated stat modifiers from Forgemaster perks, blessings, curses, supports, difficulty.</summary>
    public sealed class StatBlock
    {
        public readonly List<StatMod> Mods = new List<StatMod>();

        public void Add(StatMod m) { if (m != null) Mods.Add(m); }
        public void AddRange(IEnumerable<StatMod> ms) { if (ms != null) foreach (var m in ms) Add(m); }

        /// <summary>Sum of a stat; mods with a tag only count when the tag is in `tags` (or matches `extraTag`).</summary>
        public int Sum(StatType stat, ICollection<string> tags = null, string extraTag = null)
        {
            int total = 0;
            for (int i = 0; i < Mods.Count; i++)
            {
                var m = Mods[i];
                if (m.Stat != stat) continue;
                if (m.Tag != null)
                {
                    bool ok = (extraTag != null && string.Equals(m.Tag, extraTag, StringComparison.OrdinalIgnoreCase)) || (tags != null && tags.Contains(m.Tag));
                    if (!ok) continue;
                }
                total += m.Value;
            }
            return total;
        }

        public int SumUntagged(StatType stat)
        {
            int total = 0;
            for (int i = 0; i < Mods.Count; i++) if (Mods[i].Stat == stat && Mods[i].Tag == null) total += Mods[i].Value;
            return total;
        }

        public StatBlock Clone()
        {
            var s = new StatBlock();
            s.Mods.AddRange(Mods);
            return s;
        }
    }

    public sealed class AssemblyIssue
    {
        public string Code;   // slot_overflow, rune_overflow, requires_tag, excluded_tag, conflict, duplicate, unknown
        public string ItemId;
        public string Detail;
        public override string ToString() { return Code + ":" + ItemId + (Detail != null ? " (" + Detail + ")" : ""); }
    }

    public static class AssemblyRules
    {
        public const int MaxTier = 3;

        public static List<string> WeaponTags(ContentDatabase db, WeaponInstance w)
        {
            var tags = new List<string>();
            var f = db.Frame(w.FrameId);
            if (f != null) tags.AddRange(f.Tags);
            var c = w.CoreId != null ? db.Core(w.CoreId) : null;
            if (c != null) foreach (var t in c.Tags) if (!tags.Contains(t)) tags.Add(t);
            if (f != null) tags.Add("frame:" + f.Id);
            if (c != null) tags.Add("core:" + c.Id);
            return tags;
        }

        public static int ModifierSlots(ContentDatabase db, WeaponInstance w, StatBlock stats)
        {
            var f = db.Frame(w.FrameId);
            int slots = f == null ? 0 : f.ModifierSlots;
            if (w.Tier >= 2) slots += 1;
            if (w.LegendaryId != null) slots += 1;
            if (stats != null) slots += stats.Sum(StatType.ModifierSlots, WeaponTags(db, w));
            return slots;
        }

        public static int RuneSlots(ContentDatabase db, WeaponInstance w, StatBlock stats)
        {
            var f = db.Frame(w.FrameId);
            int slots = f == null ? 0 : f.RuneSlots;
            if (w.Tier >= 3) slots += 1;
            if (stats != null) slots += stats.Sum(StatType.RuneSlots, WeaponTags(db, w));
            return slots;
        }

        public static bool TagsMatchAny(List<string> required, List<string> tags)
        {
            if (required == null || required.Count == 0) return true;
            foreach (var r in required) if (tags.Contains(r)) return true;
            return false;
        }

        /// <summary>Validate a full weapon assembly. Empty list means the weapon is legal.</summary>
        public static List<AssemblyIssue> Validate(ContentDatabase db, WeaponInstance w, StatBlock stats)
        {
            var issues = new List<AssemblyIssue>();
            var frame = db.Frame(w.FrameId);
            if (frame == null) { issues.Add(new AssemblyIssue { Code = "unknown", ItemId = w.FrameId, Detail = "frame" }); return issues; }
            if (w.CoreId != null && db.Core(w.CoreId) == null) issues.Add(new AssemblyIssue { Code = "unknown", ItemId = w.CoreId, Detail = "core" });
            if (w.Tier < 1 || w.Tier > MaxTier) issues.Add(new AssemblyIssue { Code = "bad_tier", ItemId = w.Tier.ToString() });
            if (w.LegendaryId != null && db.Legendary(w.LegendaryId) == null) issues.Add(new AssemblyIssue { Code = "unknown", ItemId = w.LegendaryId, Detail = "legendary" });

            var tags = WeaponTags(db, w);
            int used = 0;
            var groups = new HashSet<string>();
            var seen = new HashSet<string>();
            foreach (var mid in w.Modifiers)
            {
                var m = db.Modifier(mid);
                if (m == null) { issues.Add(new AssemblyIssue { Code = "unknown", ItemId = mid, Detail = "modifier" }); continue; }
                if (!seen.Add(mid)) issues.Add(new AssemblyIssue { Code = "duplicate", ItemId = mid });
                if (!TagsMatchAny(m.RequiresTags, tags)) issues.Add(new AssemblyIssue { Code = "requires_tag", ItemId = mid, Detail = string.Join("|", m.RequiresTags) });
                foreach (var ex in m.ExcludesTags) if (tags.Contains(ex)) issues.Add(new AssemblyIssue { Code = "excluded_tag", ItemId = mid, Detail = ex });
                if (m.ConflictGroup != null && !groups.Add(m.ConflictGroup)) issues.Add(new AssemblyIssue { Code = "conflict", ItemId = mid, Detail = m.ConflictGroup });
                used += m.SlotCost;
            }
            int slots = ModifierSlots(db, w, stats);
            if (used > slots) issues.Add(new AssemblyIssue { Code = "slot_overflow", ItemId = w.FrameId, Detail = used + "/" + slots });

            var seenR = new HashSet<string>();
            foreach (var rid in w.Runes)
            {
                var r = db.Rune(rid);
                if (r == null) { issues.Add(new AssemblyIssue { Code = "unknown", ItemId = rid, Detail = "rune" }); continue; }
                if (!seenR.Add(rid)) issues.Add(new AssemblyIssue { Code = "duplicate", ItemId = rid });
                if (!TagsMatchAny(r.RequiresTags, tags)) issues.Add(new AssemblyIssue { Code = "requires_tag", ItemId = rid, Detail = string.Join("|", r.RequiresTags) });
            }
            int rslots = RuneSlots(db, w, stats);
            if (w.Runes.Count > rslots) issues.Add(new AssemblyIssue { Code = "rune_overflow", ItemId = w.FrameId, Detail = w.Runes.Count + "/" + rslots });
            return issues;
        }

        /// <summary>Would adding this modifier keep the weapon legal? Returns the blocking issues (preview for UI).</summary>
        public static List<AssemblyIssue> PreviewAddModifier(ContentDatabase db, WeaponInstance w, string modifierId, StatBlock stats)
        {
            var probe = w.Clone();
            probe.Modifiers.Add(modifierId);
            return Validate(db, probe, stats);
        }

        public static List<AssemblyIssue> PreviewAddRune(ContentDatabase db, WeaponInstance w, string runeId, StatBlock stats)
        {
            var probe = w.Clone();
            probe.Runes.Add(runeId);
            return Validate(db, probe, stats);
        }
    }

    public sealed class RecipeStatus
    {
        public LegendaryDef Recipe;
        public bool Eligible;
        public readonly List<string> Missing = new List<string>();   // human-readable missing components: "core:fire", "modifier:split", "tier:2", "essence:3"
        public int MatchedComponents;
        public int TotalComponents;
    }

    public static class LegendaryRules
    {
        public static RecipeStatus Evaluate(ContentDatabase db, WeaponInstance w, LegendaryDef l, int essenceAvailable)
        {
            var s = new RecipeStatus { Recipe = l };
            s.TotalComponents = 2 + l.RequiresModifiers.Count + (l.RequiresAnyRunes.Count > 0 ? 1 : 0) + 1;
            if (w.LegendaryId != null) { s.Missing.Add("already_legendary"); return s; }
            if (w.FrameId == l.Frame) s.MatchedComponents++; else s.Missing.Add("frame:" + l.Frame);
            if (l.Core == null || w.CoreId == l.Core) s.MatchedComponents++; else s.Missing.Add("core:" + l.Core);
            foreach (var m in l.RequiresModifiers)
            {
                if (w.Modifiers.Contains(m)) s.MatchedComponents++; else s.Missing.Add("modifier:" + m);
            }
            if (l.RequiresAnyRunes.Count > 0)
            {
                bool any = false;
                foreach (var r in l.RequiresAnyRunes) if (w.Runes.Contains(r)) any = true;
                if (any) s.MatchedComponents++; else s.Missing.Add("rune:" + string.Join("|", l.RequiresAnyRunes));
            }
            if (w.Tier >= l.MinTier) s.MatchedComponents++; else s.Missing.Add("tier:" + l.MinTier);
            if (essenceAvailable < l.EssenceCost) s.Missing.Add("essence:" + l.EssenceCost);
            s.Eligible = s.Missing.Count == 0;
            return s;
        }

        /// <summary>All recipes for this weapon's frame sorted by closeness (for progressive-disclosure hints).</summary>
        public static List<RecipeStatus> Candidates(ContentDatabase db, WeaponInstance w, int essence)
        {
            var list = new List<RecipeStatus>();
            foreach (var l in db.Legendaries)
            {
                if (l.Frame != w.FrameId) continue;
                list.Add(Evaluate(db, w, l, essence));
            }
            list.Sort((a, b) =>
            {
                int c = a.Missing.Count.CompareTo(b.Missing.Count);
                return c != 0 ? c : string.CompareOrdinal(a.Recipe.Id, b.Recipe.Id);
            });
            return list;
        }
    }
}
