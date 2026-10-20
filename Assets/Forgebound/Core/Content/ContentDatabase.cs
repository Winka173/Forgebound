using System;
using System.Collections.Generic;

namespace Forgebound.Core.Content
{
    /// <summary>Abstracts where content JSON comes from (Unity Resources/TextAsset or files on disk for headless tools).</summary>
    public interface IContentSource
    {
        /// <summary>Returns raw JSON text for the named content file (e.g. "frames") or null if missing.</summary>
        string Read(string name);
    }

    public sealed class DictionaryContentSource : IContentSource
    {
        private readonly Dictionary<string, string> _files;
        public DictionaryContentSource(Dictionary<string, string> files) { _files = files; }
        public string Read(string name) { string s; return _files.TryGetValue(name, out s) ? s : null; }
    }

    /// <summary>
    /// Immutable (after load) registry of all authored content. Lookups are by stable string id.
    /// Lists preserve file order so iteration is deterministic.
    /// </summary>
    public sealed class ContentDatabase
    {
        public const int SchemaVersion = 1;

        public static readonly string[] FileNames =
        {
            "frames", "cores", "modifiers", "runes", "pieces", "legendaries", "forgemasters",
            "enemies", "encounters", "events", "blessings", "biomes", "challenges"
        };

        public readonly List<FrameDef> Frames = new List<FrameDef>();
        public readonly List<CoreDef> Cores = new List<CoreDef>();
        public readonly List<ModifierDef> Modifiers = new List<ModifierDef>();
        public readonly List<RuneDef> Runes = new List<RuneDef>();
        public readonly List<BoardPieceDef> Pieces = new List<BoardPieceDef>();
        public readonly List<LegendaryDef> Legendaries = new List<LegendaryDef>();
        public readonly List<ForgemasterDef> Forgemasters = new List<ForgemasterDef>();
        public readonly List<EnemyDef> Enemies = new List<EnemyDef>();
        public readonly List<EncounterDef> Encounters = new List<EncounterDef>();
        public readonly List<EventDef> Events = new List<EventDef>();
        public readonly List<BlessingDef> Blessings = new List<BlessingDef>();
        public readonly List<BiomeDef> Biomes = new List<BiomeDef>();
        public readonly List<ChallengeDef> Challenges = new List<ChallengeDef>();

        private readonly Dictionary<string, FrameDef> _frames = new Dictionary<string, FrameDef>();
        private readonly Dictionary<string, CoreDef> _cores = new Dictionary<string, CoreDef>();
        private readonly Dictionary<string, ModifierDef> _mods = new Dictionary<string, ModifierDef>();
        private readonly Dictionary<string, RuneDef> _runes = new Dictionary<string, RuneDef>();
        private readonly Dictionary<string, BoardPieceDef> _pieces = new Dictionary<string, BoardPieceDef>();
        private readonly Dictionary<string, LegendaryDef> _legendaries = new Dictionary<string, LegendaryDef>();
        private readonly Dictionary<string, ForgemasterDef> _forgemasters = new Dictionary<string, ForgemasterDef>();
        private readonly Dictionary<string, EnemyDef> _enemies = new Dictionary<string, EnemyDef>();
        private readonly Dictionary<string, EncounterDef> _encounters = new Dictionary<string, EncounterDef>();
        private readonly Dictionary<string, EventDef> _events = new Dictionary<string, EventDef>();
        private readonly Dictionary<string, BlessingDef> _blessings = new Dictionary<string, BlessingDef>();
        private readonly Dictionary<string, BiomeDef> _biomes = new Dictionary<string, BiomeDef>();
        private readonly Dictionary<string, ChallengeDef> _challenges = new Dictionary<string, ChallengeDef>();

        public readonly List<string> LoadErrors = new List<string>();

        public FrameDef Frame(string id) { return Get(_frames, id); }
        public CoreDef Core(string id) { return Get(_cores, id); }
        public ModifierDef Modifier(string id) { return Get(_mods, id); }
        public RuneDef Rune(string id) { return Get(_runes, id); }
        public BoardPieceDef Piece(string id) { return Get(_pieces, id); }
        public LegendaryDef Legendary(string id) { return Get(_legendaries, id); }
        public ForgemasterDef Forgemaster(string id) { return Get(_forgemasters, id); }
        public EnemyDef Enemy(string id) { return Get(_enemies, id); }
        public EncounterDef Encounter(string id) { return Get(_encounters, id); }
        public EventDef Event(string id) { return Get(_events, id); }
        public BlessingDef Blessing(string id) { return Get(_blessings, id); }
        public BiomeDef Biome(string id) { return Get(_biomes, id); }
        public ChallengeDef Challenge(string id) { return Get(_challenges, id); }

        private static T Get<T>(Dictionary<string, T> d, string id) where T : class
        {
            if (id == null) return null;
            T v;
            return d.TryGetValue(id, out v) ? v : null;
        }

        /// <summary>Resolve an item reference "category:id" used by inventories, shops and saves.</summary>
        public bool ItemExists(ItemCategory cat, string id)
        {
            switch (cat)
            {
                case ItemCategory.Frame: return _frames.ContainsKey(id);
                case ItemCategory.Core: return _cores.ContainsKey(id);
                case ItemCategory.Modifier: return _mods.ContainsKey(id);
                case ItemCategory.Rune: return _runes.ContainsKey(id);
                case ItemCategory.Piece: return _pieces.ContainsKey(id);
                default: return false;
            }
        }

        public int ItemCost(ItemCategory cat, string id)
        {
            switch (cat)
            {
                case ItemCategory.Frame: { var d = Frame(id); return d == null ? 0 : d.Cost; }
                case ItemCategory.Core: { var d = Core(id); return d == null ? 0 : d.Cost; }
                case ItemCategory.Modifier: { var d = Modifier(id); return d == null ? 0 : d.Cost; }
                case ItemCategory.Rune: { var d = Rune(id); return d == null ? 0 : d.Cost; }
                case ItemCategory.Piece: { var d = Piece(id); return d == null ? 0 : d.Cost; }
                default: return 0;
            }
        }

        public Rarity ItemRarity(ItemCategory cat, string id)
        {
            switch (cat)
            {
                case ItemCategory.Frame: { var d = Frame(id); return d == null ? Rarity.Common : d.Rarity; }
                case ItemCategory.Core: { var d = Core(id); return d == null ? Rarity.Common : d.Rarity; }
                case ItemCategory.Modifier: { var d = Modifier(id); return d == null ? Rarity.Common : d.Rarity; }
                case ItemCategory.Rune: { var d = Rune(id); return d == null ? Rarity.Common : d.Rarity; }
                case ItemCategory.Piece: { var d = Piece(id); return d == null ? Rarity.Common : d.Rarity; }
                default: return Rarity.Common;
            }
        }

        public static ContentDatabase Load(IContentSource source)
        {
            var db = new ContentDatabase();
            var errors = db.LoadErrors;
            foreach (var name in FileNames)
            {
                var text = source.Read(name);
                if (text == null) { errors.Add("missing content file: " + name); continue; }
                JsonValue root;
                string err;
                if (!JsonParser.TryParse(text, out root, out err)) { errors.Add(name + ": JSON parse error: " + err); continue; }
                int version = root.Int("version", 0);
                if (version != SchemaVersion) errors.Add(name + ": schema version " + version + " != " + SchemaVersion);
                var items = root["items"];
                if (items.Kind != JsonKind.Array) { errors.Add(name + ": missing 'items' array"); continue; }
                foreach (var item in items.Elements())
                {
                    try { db.AddItem(name, item, errors); }
                    catch (Exception ex) { errors.Add(name + ": exception parsing item: " + ex.Message); }
                }
            }
            return db;
        }

        private void AddItem(string file, JsonValue v, List<string> errors)
        {
            switch (file)
            {
                case "frames": Register(Frames, _frames, FrameDef.Parse(v, errors), x => x.Id, file, errors); break;
                case "cores": Register(Cores, _cores, CoreDef.Parse(v, errors), x => x.Id, file, errors); break;
                case "modifiers": Register(Modifiers, _mods, ModifierDef.Parse(v, errors), x => x.Id, file, errors); break;
                case "runes": Register(Runes, _runes, RuneDef.Parse(v, errors, "rune"), x => x.Id, file, errors); break;
                case "pieces": Register(Pieces, _pieces, BoardPieceDef.Parse(v, errors), x => x.Id, file, errors); break;
                case "legendaries": Register(Legendaries, _legendaries, LegendaryDef.Parse(v, errors), x => x.Id, file, errors); break;
                case "forgemasters": Register(Forgemasters, _forgemasters, ForgemasterDef.Parse(v, errors), x => x.Id, file, errors); break;
                case "enemies": Register(Enemies, _enemies, EnemyDef.Parse(v, errors), x => x.Id, file, errors); break;
                case "encounters": Register(Encounters, _encounters, EncounterDef.Parse(v, errors), x => x.Id, file, errors); break;
                case "events": Register(Events, _events, EventDef.Parse(v, errors), x => x.Id, file, errors); break;
                case "blessings": Register(Blessings, _blessings, BlessingDef.Parse(v, errors), x => x.Id, file, errors); break;
                case "biomes": Register(Biomes, _biomes, BiomeDef.Parse(v, errors), x => x.Id, file, errors); break;
                case "challenges": Register(Challenges, _challenges, ChallengeDef.Parse(v, errors), x => x.Id, file, errors); break;
            }
        }

        private static void Register<T>(List<T> list, Dictionary<string, T> map, T item, Func<T, string> id, string file, List<string> errors)
        {
            string key = id(item);
            if (string.IsNullOrEmpty(key)) return;
            if (map.ContainsKey(key)) { errors.Add(file + ": duplicate id '" + key + "'"); return; }
            map[key] = item;
            list.Add(item);
        }
    }
}
