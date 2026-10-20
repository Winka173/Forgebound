using System;
using System.Collections.Generic;
using Forgebound.Core.Content;
using Forgebound.Core.Run;
using Forgebound.Core.Weapons;

namespace Forgebound.Core.Progression
{
    public sealed class RunSummary
    {
        public string Seed;
        public string Forgemaster;
        public int Difficulty;
        public string Outcome;
        public int Chapter;
        public int Step;
        public int Battles;
        public long Damage;
        public string DeathReason;
        public readonly List<string> FinalWeapons = new List<string>();
        public readonly List<string> Blessings = new List<string>();
        public string Date;

        public JsonValue ToJson()
        {
            var o = JsonValue.Obj().Set("seed", Seed).Set("fm", Forgemaster).Set("difficulty", Difficulty).Set("outcome", Outcome).Set("chapter", Chapter)
                .Set("step", Step).Set("battles", Battles).Set("damage", Damage).Set("death", JsonValue.Str(DeathReason)).Set("date", JsonValue.Str(Date));
            var w = JsonValue.Arr(); foreach (var s in FinalWeapons) w.Add(JsonValue.Str(s)); o.Set("weapons", w);
            var b = JsonValue.Arr(); foreach (var s in Blessings) b.Add(JsonValue.Str(s)); o.Set("blessings", b);
            return o;
        }

        public static RunSummary FromJson(JsonValue j)
        {
            var r = new RunSummary
            {
                Seed = j.Str("seed"), Forgemaster = j.Str("fm"), Difficulty = j.Int("difficulty"), Outcome = j.Str("outcome"), Chapter = j.Int("chapter"),
                Step = j.Int("step"), Battles = j.Int("battles"), Damage = j["damage"].AsLong(), DeathReason = j.Str("death"), Date = j.Str("date")
            };
            r.FinalWeapons.AddRange(j.StrList("weapons"));
            r.Blessings.AddRange(j.StrList("blessings"));
            return r;
        }
    }

    public sealed class SavedDesign
    {
        public string Name;
        public bool Favorite;
        public WeaponInstance Weapon;
    }

    public sealed class Settings
    {
        public string Language = "en";
        public int TextScalePercent = 100;      // 80..150
        public bool ReducedFlashes;
        public int ScreenShakePercent = 60;     // 0..100
        public string ColorblindMode = "none";  // none | protan | deutan | tritan (patterns + palette)
        public int DefaultBattleSpeed = 1;      // 1 or 2
        public int ParticleBudgetPercent = 100; // 25..100
        public bool TargetFps30;
        public bool HintsEnabled = true;
        public bool SkipForgeAnimations;
        public int MasterVolume = 80, MusicVolume = 70, SfxVolume = 80;

        public JsonValue ToJson()
        {
            return JsonValue.Obj().Set("language", Language).Set("textScale", TextScalePercent).Set("reducedFlashes", ReducedFlashes).Set("shake", ScreenShakePercent)
                .Set("colorblind", ColorblindMode).Set("speed", DefaultBattleSpeed).Set("particles", ParticleBudgetPercent).Set("fps30", TargetFps30)
                .Set("hints", HintsEnabled).Set("skipForge", SkipForgeAnimations).Set("master", MasterVolume).Set("music", MusicVolume).Set("sfx", SfxVolume);
        }

        public static Settings FromJson(JsonValue j)
        {
            var s = new Settings();
            if (j.Kind != JsonKind.Object) return s;
            s.Language = j.Str("language", "en") == "vi" ? "vi" : "en";
            s.TextScalePercent = Math.Max(80, Math.Min(150, j.Int("textScale", 100)));
            s.ReducedFlashes = j.BoolOf("reducedFlashes");
            s.ScreenShakePercent = Math.Max(0, Math.Min(100, j.Int("shake", 60)));
            s.ColorblindMode = j.Str("colorblind", "none");
            s.DefaultBattleSpeed = j.Int("speed", 1) == 2 ? 2 : 1;
            s.ParticleBudgetPercent = Math.Max(25, Math.Min(100, j.Int("particles", 100)));
            s.TargetFps30 = j.BoolOf("fps30");
            s.HintsEnabled = j.BoolOf("hints", true);
            s.SkipForgeAnimations = j.BoolOf("skipForge");
            s.MasterVolume = Math.Max(0, Math.Min(100, j.Int("master", 80)));
            s.MusicVolume = Math.Max(0, Math.Min(100, j.Int("music", 70)));
            s.SfxVolume = Math.Max(0, Math.Min(100, j.Int("sfx", 80)));
            return s;
        }
    }

    /// <summary>
    /// Persistent player profile. Holds knowledge (discoveries, gallery, history, achievements, cosmetics, settings) —
    /// never run power. The only things an Adventure run reads from it are: which Forgemasters are unlocked and the
    /// optional favourite recipe hint. Workshop designs and trial items are stored here and are never injected into runs.
    /// </summary>
    public sealed class Profile
    {
        public const int Version = 1;
        public const int MaxHistory = 60;
        public const int MaxDesigns = 40;

        public readonly HashSet<string> UnlockedForgemasters = new HashSet<string>();
        public readonly HashSet<string> Discovered = new HashSet<string>();
        public readonly HashSet<string> Achievements = new HashSet<string>();
        public readonly HashSet<string> Cosmetics = new HashSet<string>();
        public readonly HashSet<string> TrialsCompleted = new HashSet<string>();
        public readonly List<RunSummary> History = new List<RunSummary>();
        public readonly List<SavedDesign> Designs = new List<SavedDesign>();
        public string FavoriteRecipe;
        public int Runs, Wins, ElitesKilled, BossesKilled, LegendariesCrafted, HighestChapter;
        public Settings Settings = new Settings();

        public static Profile CreateNew(ContentDatabase db)
        {
            var p = new Profile();
            foreach (var f in db.Forgemasters) if (f.Unlock == null) p.UnlockedForgemasters.Add(f.Id);
            return p;
        }

        public bool IsUnlocked(string forgemasterId) { return UnlockedForgemasters.Contains(forgemasterId); }

        public bool CheckUnlock(string condition)
        {
            switch (condition)
            {
                case null: return true;
                case "win_any": return Wins > 0;
                case "reach_chapter_3": return HighestChapter >= 3;
                case "elites_10": return ElitesKilled >= 10;
                case "craft_legendary": return LegendariesCrafted > 0;
                default: return false;
            }
        }

        /// <summary>Record a finished run: discoveries, stats, history, unlocks, achievements. Returns newly unlocked ids.</summary>
        public List<string> RecordRun(ContentDatabase db, RunState run, string date)
        {
            var unlocked = new List<string>();
            Runs++;
            if (run.Outcome == RunOutcome.Victory) Wins++;
            ElitesKilled += run.ElitesKilled;
            BossesKilled += run.BossesKilled;
            LegendariesCrafted += run.LegendariesForged;
            HighestChapter = Math.Max(HighestChapter, run.Chapter + 1);
            foreach (var d in run.Discovered) Discovered.Add(d);
            foreach (var w in run.Weapons) Discovered.Add("blueprint:" + w.FrameId + "|" + (w.CoreId ?? "-"));

            var s = new RunSummary
            {
                Seed = run.Seed.ToString(), Forgemaster = run.ForgemasterId, Difficulty = run.Difficulty, Outcome = run.Outcome.ToString(),
                Chapter = run.Chapter + 1, Step = run.Step, Battles = run.BattlesFought, Damage = run.TotalDamage, DeathReason = run.DeathReason, Date = date
            };
            foreach (var w in run.Weapons) s.FinalWeapons.Add(w.Signature());
            s.Blessings.AddRange(run.Blessings);
            History.Insert(0, s);
            if (History.Count > MaxHistory) History.RemoveRange(MaxHistory, History.Count - MaxHistory);

            foreach (var f in db.Forgemasters)
                if (!UnlockedForgemasters.Contains(f.Id) && CheckUnlock(f.Unlock)) { UnlockedForgemasters.Add(f.Id); unlocked.Add("forgemaster:" + f.Id); }

            Award("first_run", true, unlocked);
            Award("first_victory", run.Outcome == RunOutcome.Victory, unlocked);
            Award("first_legendary", run.LegendariesForged > 0, unlocked);
            Award("hard_victory", run.Outcome == RunOutcome.Victory && run.Difficulty >= 2, unlocked);
            Award("forgemaster_victory", run.Outcome == RunOutcome.Victory && run.Difficulty >= 3, unlocked);
            int frames = 0;
            foreach (var f in db.Frames) { foreach (var d in Discovered) if (d.StartsWith("blueprint:" + f.Id + "|")) { frames++; break; } }
            Award("all_frames", frames == db.Frames.Count, unlocked);
            Award("triple_threat", run.Weapons.Count >= 3 && run.Outcome == RunOutcome.Victory, unlocked);
            return unlocked;
        }

        private void Award(string id, bool condition, List<string> unlocked)
        {
            if (condition && Achievements.Add(id)) unlocked.Add("achievement:" + id);
        }

        /// <summary>Items the Workshop sandbox may use: only what the player has discovered (or starter kits).</summary>
        public bool CanUseInWorkshop(ItemCategory cat, string id)
        {
            switch (cat)
            {
                case ItemCategory.Frame:
                    foreach (var d in Discovered) if (d.StartsWith("blueprint:" + id + "|")) return true;
                    return Discovered.Contains("item:Frame:" + id);
                case ItemCategory.Core:
                    foreach (var d in Discovered) if (d.EndsWith("|" + id) && d.StartsWith("blueprint:")) return true;
                    return Discovered.Contains("item:Core:" + id);
                default:
                    return Discovered.Contains("item:" + cat + ":" + id);
            }
        }

        public JsonValue ToJson()
        {
            Func<IEnumerable<string>, JsonValue> arr = items =>
            {
                var list = new List<string>(items);
                list.Sort(StringComparer.Ordinal);
                var a = JsonValue.Arr();
                foreach (var s in list) a.Add(JsonValue.Str(s));
                return a;
            };
            var o = JsonValue.Obj().Set("version", Version)
                .Set("unlocked", arr(UnlockedForgemasters)).Set("discovered", arr(Discovered)).Set("achievements", arr(Achievements))
                .Set("cosmetics", arr(Cosmetics)).Set("trials", arr(TrialsCompleted)).Set("favorite", JsonValue.Str(FavoriteRecipe))
                .Set("runs", Runs).Set("wins", Wins).Set("elites", ElitesKilled).Set("bosses", BossesKilled).Set("legendaries", LegendariesCrafted).Set("highestChapter", HighestChapter)
                .Set("settings", Settings.ToJson());
            var hist = JsonValue.Arr(); foreach (var h in History) hist.Add(h.ToJson()); o.Set("history", hist);
            var designs = JsonValue.Arr();
            foreach (var d in Designs) designs.Add(JsonValue.Obj().Set("name", d.Name).Set("favorite", d.Favorite).Set("weapon", RunState.WeaponToJson(d.Weapon)));
            o.Set("designs", designs);
            return o;
        }

        public static Profile FromJson(ContentDatabase db, JsonValue j)
        {
            var p = new Profile();
            foreach (var s in j.StrList("unlocked")) if (db.Forgemaster(s) != null) p.UnlockedForgemasters.Add(s);
            foreach (var f in db.Forgemasters) if (f.Unlock == null) p.UnlockedForgemasters.Add(f.Id);
            foreach (var s in j.StrList("discovered")) p.Discovered.Add(s);
            foreach (var s in j.StrList("achievements")) p.Achievements.Add(s);
            foreach (var s in j.StrList("cosmetics")) p.Cosmetics.Add(s);
            foreach (var s in j.StrList("trials")) p.TrialsCompleted.Add(s);
            p.FavoriteRecipe = j.Str("favorite");
            if (p.FavoriteRecipe != null && db.Legendary(p.FavoriteRecipe) == null) p.FavoriteRecipe = null;
            p.Runs = j.Int("runs"); p.Wins = j.Int("wins"); p.ElitesKilled = j.Int("elites"); p.BossesKilled = j.Int("bosses");
            p.LegendariesCrafted = j.Int("legendaries"); p.HighestChapter = j.Int("highestChapter");
            p.Settings = Settings.FromJson(j["settings"]);
            foreach (var h in j["history"].Elements()) if (p.History.Count < MaxHistory) p.History.Add(RunSummary.FromJson(h));
            foreach (var d in j["designs"].Elements())
            {
                if (p.Designs.Count >= MaxDesigns) break;
                var w = RunState.WeaponFromJson(d["weapon"]);
                if (BlueprintCodec.ValidateWeapon(db, w).Count > 0) continue;   // drop invalid designs instead of crashing
                p.Designs.Add(new SavedDesign { Name = (d.Str("name", "Design") ?? "Design").Length > 40 ? d.Str("name").Substring(0, 40) : d.Str("name", "Design"), Favorite = d.BoolOf("favorite"), Weapon = w });
            }
            return p;
        }
    }

    /// <summary>Sharing-safe blueprint export/import. Imports are never trusted: ids, sizes, counts and assembly legality are validated.</summary>
    public static class BlueprintCodec
    {
        public const int MaxImportLength = 4096;
        public const string Prefix = "FBP1:";

        public static string Export(WeaponInstance w)
        {
            var j = RunState.WeaponToJson(w);
            j.Set("uid", 0);
            string body = JsonWriter.Write(j, false);
            return Prefix + Save.SaveSystem.Checksum(body) + ":" + body;
        }

        public static List<string> ValidateWeapon(ContentDatabase db, WeaponInstance w)
        {
            var errors = new List<string>();
            if (w.FrameId == null || db.Frame(w.FrameId) == null) errors.Add("unknown_frame");
            if (w.Modifiers.Count > 6) errors.Add("too_many_modifiers");
            if (w.Runes.Count > 6) errors.Add("too_many_runes");
            if (errors.Count > 0) return errors;
            foreach (var issue in AssemblyRules.Validate(db, w, null)) errors.Add(issue.ToString());
            return errors;
        }

        public static WeaponInstance Import(ContentDatabase db, string text, out List<string> errors)
        {
            errors = new List<string>();
            if (string.IsNullOrEmpty(text) || text.Length > MaxImportLength || !text.StartsWith(Prefix)) { errors.Add("format"); return null; }
            var rest = text.Substring(Prefix.Length);
            int colon = rest.IndexOf(':');
            if (colon <= 0) { errors.Add("format"); return null; }
            string sum = rest.Substring(0, colon), body = rest.Substring(colon + 1);
            if (Save.SaveSystem.Checksum(body) != sum) { errors.Add("checksum"); return null; }
            JsonValue j;
            string err;
            if (!JsonParser.TryParse(body, out j, out err) || j.Kind != JsonKind.Object) { errors.Add("json"); return null; }
            var w = RunState.WeaponFromJson(j);
            w.Uid = 0;
            if (w.Tier < 1 || w.Tier > AssemblyRules.MaxTier) { errors.Add("tier"); return null; }
            errors.AddRange(ValidateWeapon(db, w));
            return errors.Count == 0 ? w : null;
        }
    }
}
