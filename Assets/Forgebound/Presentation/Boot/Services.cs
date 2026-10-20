using System;
using System.Collections.Generic;
using System.IO;
using Forgebound.Core.Content;
using Forgebound.Core.Localization;
using Forgebound.Core.Progression;
using Forgebound.Core.Run;
using Forgebound.Core.Save;
using UnityEngine;

namespace Forgebound.Presentation
{
    /// <summary>Loads content JSON shipped in Resources/Content as TextAssets.</summary>
    public sealed class ResourcesContentSource : IContentSource
    {
        public string Read(string name)
        {
            var ta = Resources.Load<TextAsset>("Content/" + name);
            return ta != null ? ta.text : null;
        }
    }

    /// <summary>Save files under Application.persistentDataPath with atomic replace.</summary>
    public sealed class UnityFileStore : IFileStore
    {
        private readonly string _root;
        public UnityFileStore(string root) { _root = root; Directory.CreateDirectory(root); }
        private string P(string path) { return Path.Combine(_root, path); }
        public bool Exists(string path) { return File.Exists(P(path)); }
        public string ReadAllText(string path) { return File.ReadAllText(P(path)); }
        public void WriteAllText(string path, string text) { File.WriteAllText(P(path), text); }
        public void Delete(string path) { if (File.Exists(P(path))) File.Delete(P(path)); }
        public void Move(string from, string to)
        {
            if (File.Exists(P(to))) File.Delete(P(to));
            File.Move(P(from), P(to));
        }
    }

    /// <summary>
    /// Long-lived services. Plain C# object owned by <see cref="GameRoot"/>; screens get it injected.
    /// </summary>
    public sealed class Services
    {
        public const string ProfileFile = "profile.sav";
        public const string RunFile = "run.sav";

        public ContentDatabase Db;
        public Loc Loc;
        public TooltipBuilder Tips;
        public Profile Profile;
        public SaveSystem Saves;
        public RunController Run;   // active Adventure run (null when none)
        public AudioManager Audio;
        public SpriteLibrary Sprites;
        public UiRoot Ui;
        public readonly List<string> StartupWarnings = new List<string>();

        public static Services Create(GameObject host)
        {
            var s = new Services();
            s.Db = ContentDatabase.Load(new ResourcesContentSource());
            foreach (var e in s.Db.LoadErrors) { s.StartupWarnings.Add(e); Debug.LogError("[Forgebound] content: " + e); }
            s.Loc = new Loc();
            foreach (var lang in Loc.Languages)
            {
                var table = new Dictionary<string, string>();
                foreach (var file in new[] { "Localization/" + lang, "Localization/ui_" + lang })
                {
                    var ta = Resources.Load<TextAsset>(file);
                    if (ta == null) continue;
                    foreach (var kv in Loc.ParseTable(ta.text)) table[kv.Key] = kv.Value;
                }
                s.Loc.AddTable(lang, table);
            }
            s.Tips = new TooltipBuilder(s.Loc, s.Db);
            s.Saves = new SaveSystem(new UnityFileStore(Path.Combine(Application.persistentDataPath, "saves")));
            s.LoadProfile();
            s.Loc.Language = s.Profile.Settings.Language;
            s.Sprites = new SpriteLibrary();
            s.Audio = host.AddComponent<AudioManager>();
            s.Audio.Init(s);
            return s;
        }

        public void LoadProfile()
        {
            Profile = null;
            try
            {
                var r = Saves.Load(ProfileFile, "profile");
                if (r.Status == LoadStatus.RecoveredFromBackup) StartupWarnings.Add("profile_recovered_from_backup");
                if (r.Data != null) Profile = Profile.FromJson(Db, r.Data);
                if (r.Status == LoadStatus.Corrupt) StartupWarnings.Add("profile_corrupt:" + r.Error);
            }
            catch (Exception e)
            {
                StartupWarnings.Add("profile_load_failed:" + e.Message);
            }
            if (Profile == null) Profile = Profile.CreateNew(Db);
        }

        public void SaveProfile()
        {
            try { Saves.Save(ProfileFile, "profile", Profile.ToJson()); }
            catch (Exception e) { Debug.LogError("[Forgebound] profile save failed: " + e); }
        }

        /// <summary>Autosave at safe boundaries (never mid-battle; battles are re-simulated deterministically from the seed).</summary>
        public void SaveRun()
        {
            if (Run == null) return;
            try
            {
                if (Run.Run.Phase == RunPhase.Finished) Saves.Save(RunFile, "run", JsonValue.Obj().Set("empty", true));
                else Saves.Save(RunFile, "run", Run.Run.ToJson());
            }
            catch (Exception e) { Debug.LogError("[Forgebound] run save failed: " + e); }
        }

        public bool HasSavedRun()
        {
            var r = Saves.Load(RunFile, "run");
            return r.Data != null && !r.Data.BoolOf("empty") && r.Data.Has("seed");
        }

        public bool ResumeRun()
        {
            var r = Saves.Load(RunFile, "run");
            if (r.Data == null || r.Data.BoolOf("empty")) return false;
            try
            {
                var state = RunState.FromJson(r.Data);
                if (Db.Forgemaster(state.ForgemasterId) == null) return false;
                Run = new RunController(Db, state) { FavoriteRecipe = Profile.FavoriteRecipe };
                return true;
            }
            catch (Exception e)
            {
                StartupWarnings.Add("run_resume_failed:" + e.Message);
                return false;
            }
        }

        public string L(string key) { return Loc.Get(key); }
        public string LF(string key, params object[] args) { return Loc.Format(key, args); }
    }
}
