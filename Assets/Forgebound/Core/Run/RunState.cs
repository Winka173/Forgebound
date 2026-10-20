using System;
using System.Collections.Generic;
using Forgebound.Core.Combat;
using Forgebound.Core.Content;
using Forgebound.Core.ForgeBoard;
using Forgebound.Core.Weapons;

namespace Forgebound.Core.Run
{
    public enum RunPhase { ChoosingNode, PreBattle, InShop, InForge, InRest, InEvent, InReward, Finished }

    public enum RunOutcome { None, Victory, Defeat, Abandoned }

    public sealed class InventoryItem
    {
        public int Uid;
        public ItemCategory Category;
        public string Id;
    }

    public sealed class MapNode
    {
        public string Id;
        public NodeType Type;
        public int Chapter;
        public int Row;
        public int Column;
        public readonly List<string> Next = new List<string>();
        public string EncounterId;
        public string EventId;
        public bool Visited;
    }

    public sealed class ChapterMap
    {
        public int Chapter;
        public string Biome;
        public readonly List<MapNode> Nodes = new List<MapNode>();
    }

    public sealed class ShopOffer
    {
        public ItemCategory Category;
        public string Id;
        public int Price;
        public bool Locked;
        public bool Sold;
    }

    public sealed class RewardChoice
    {
        public ItemCategory Category;
        public string Id;             // item id, or blessing id when IsBlessing
        public bool IsBlessing;
    }

    public sealed class NodeRecord
    {
        public string NodeId;
        public NodeType Type;
        public string EncounterId;
        public bool Won;
        public int Ticks;
        public int HpBefore, HpAfter;
        public int GoldAfter;
        public long DamageDealt;
        public string KilledBy;
        public readonly List<string> WeaponSignatures = new List<string>();
    }

    /// <summary>
    /// Complete, serialisable Adventure run. Everything needed to resume (or replay deterministically) lives here.
    /// Per-system RNG streams are stored so save/load mid-run continues the exact same sequence.
    /// </summary>
    public sealed class RunState
    {
        public const int Version = 1;
        public const int ChapterCount = 3;
        public const int RowsPerChapter = 5;
        public const int InventoryCap = 16;

        public ulong Seed;
        public string ForgemasterId;
        public int Difficulty = 1;
        public string Mode = "adventure";
        public int Chapter;                 // 0-based
        public string CurrentNodeId;
        public int Hp, MaxHpBase;
        public int Gold, Essence;
        public readonly List<InventoryItem> Inventory = new List<InventoryItem>();
        public readonly List<WeaponInstance> Weapons = new List<WeaponInstance>();
        public BoardState Board = new BoardState();
        public int PrimaryWeaponUid = -1;
        public TargetPriority Targeting = TargetPriority.Nearest;
        public readonly List<string> Blessings = new List<string>();
        public readonly List<ChapterMap> Maps = new List<ChapterMap>();
        public RunPhase Phase = RunPhase.ChoosingNode;
        public RunOutcome Outcome = RunOutcome.None;
        public int NextUid = 1;
        public readonly Dictionary<string, uint[]> RngStates = new Dictionary<string, uint[]>();
        // Shop
        public readonly List<ShopOffer> ShopOffers = new List<ShopOffer>();
        public int ShopRerolls;
        public readonly List<ShopOffer> LockedCarry = new List<ShopOffer>();
        // Rewards / events / forge
        public readonly List<RewardChoice> PendingRewards = new List<RewardChoice>();
        public string PendingEventId;
        public bool ForgeActionUsed;
        public bool RestUsed;
        public int BattlesFought;
        public int ElitesKilled;
        public int BossesKilled;
        public long TotalDamage;
        public int LegendariesForged;
        public int ShopDiscountPermille;     // temporary from events, consumed at next shop
        public int RevealMapChapters;
        public readonly List<NodeRecord> History = new List<NodeRecord>();
        public readonly List<string> Discovered = new List<string>();   // blueprint signatures + legendary ids discovered in run
        public readonly List<JsonValue> ActionLog = new List<JsonValue>();
        public string DeathReason;
        public bool NextRewardBlessing;

        public WeaponInstance FindWeapon(int uid)
        {
            for (int i = 0; i < Weapons.Count; i++) if (Weapons[i].Uid == uid) return Weapons[i];
            return null;
        }

        public InventoryItem FindItem(int uid)
        {
            for (int i = 0; i < Inventory.Count; i++) if (Inventory[i].Uid == uid) return Inventory[i];
            return null;
        }

        public ChapterMap CurrentMap { get { return Chapter >= 0 && Chapter < Maps.Count ? Maps[Chapter] : null; } }

        public MapNode FindNode(string id)
        {
            foreach (var m in Maps) foreach (var n in m.Nodes) if (n.Id == id) return n;
            return null;
        }

        public int Step
        {
            get
            {
                var n = CurrentNodeId != null ? FindNode(CurrentNodeId) : null;
                return n == null ? 0 : n.Chapter * RowsPerChapter + n.Row + 1;
            }
        }

        // ------------------------------------------------------------------ serialisation

        public JsonValue ToJson()
        {
            var o = JsonValue.Obj();
            o.Set("version", Version);
            o.Set("seed", Seed.ToString());
            o.Set("forgemaster", ForgemasterId);
            o.Set("difficulty", Difficulty);
            o.Set("mode", Mode);
            o.Set("chapter", Chapter);
            o.Set("node", JsonValue.Str(CurrentNodeId));
            o.Set("hp", Hp);
            o.Set("maxHpBase", MaxHpBase);
            o.Set("gold", Gold);
            o.Set("essence", Essence);
            o.Set("primary", PrimaryWeaponUid);
            o.Set("targeting", Targeting.ToString());
            o.Set("phase", Phase.ToString());
            o.Set("outcome", Outcome.ToString());
            o.Set("nextUid", NextUid);
            o.Set("shopRerolls", ShopRerolls);
            o.Set("pendingEvent", JsonValue.Str(PendingEventId));
            o.Set("forgeUsed", ForgeActionUsed);
            o.Set("restUsed", RestUsed);
            o.Set("battles", BattlesFought);
            o.Set("elites", ElitesKilled);
            o.Set("bosses", BossesKilled);
            o.Set("totalDamage", TotalDamage);
            o.Set("legendaries", LegendariesForged);
            o.Set("shopDiscount", ShopDiscountPermille);
            o.Set("deathReason", JsonValue.Str(DeathReason));
            o.Set("nextRewardBlessing", NextRewardBlessing);

            var inv = JsonValue.Arr();
            foreach (var it in Inventory) inv.Add(JsonValue.Obj().Set("uid", it.Uid).Set("cat", it.Category.ToString()).Set("id", it.Id));
            o.Set("inventory", inv);

            var weapons = JsonValue.Arr();
            foreach (var w in Weapons) weapons.Add(WeaponToJson(w));
            o.Set("weapons", weapons);
            o.Set("board", BoardToJson(Board));

            var bl = JsonValue.Arr();
            foreach (var b in Blessings) bl.Add(JsonValue.Str(b));
            o.Set("blessings", bl);

            var maps = JsonValue.Arr();
            foreach (var m in Maps)
            {
                var mj = JsonValue.Obj().Set("chapter", m.Chapter).Set("biome", m.Biome);
                var nodes = JsonValue.Arr();
                foreach (var n in m.Nodes)
                {
                    var nj = JsonValue.Obj().Set("id", n.Id).Set("type", n.Type.ToString()).Set("row", n.Row).Set("col", n.Column)
                        .Set("enc", JsonValue.Str(n.EncounterId)).Set("evt", JsonValue.Str(n.EventId)).Set("visited", n.Visited);
                    var next = JsonValue.Arr();
                    foreach (var x in n.Next) next.Add(JsonValue.Str(x));
                    nj.Set("next", next);
                    nodes.Add(nj);
                }
                mj.Set("nodes", nodes);
                maps.Add(mj);
            }
            o.Set("maps", maps);

            var rng = JsonValue.Obj();
            var keys = new List<string>(RngStates.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                var arr = JsonValue.Arr();
                foreach (var w in RngStates[k]) arr.Add(JsonValue.Num(w));
                rng.Set(k, arr);
            }
            o.Set("rng", rng);

            o.Set("shop", OffersToJson(ShopOffers));
            o.Set("lockedCarry", OffersToJson(LockedCarry));
            var rewards = JsonValue.Arr();
            foreach (var r in PendingRewards) rewards.Add(JsonValue.Obj().Set("cat", r.Category.ToString()).Set("id", r.Id).Set("blessing", r.IsBlessing));
            o.Set("rewards", rewards);

            var hist = JsonValue.Arr();
            foreach (var h in History)
            {
                var hj = JsonValue.Obj().Set("node", h.NodeId).Set("type", h.Type.ToString()).Set("enc", JsonValue.Str(h.EncounterId)).Set("won", h.Won)
                    .Set("ticks", h.Ticks).Set("hpBefore", h.HpBefore).Set("hpAfter", h.HpAfter).Set("gold", h.GoldAfter).Set("dmg", h.DamageDealt)
                    .Set("killedBy", JsonValue.Str(h.KilledBy));
                var sigs = JsonValue.Arr();
                foreach (var s in h.WeaponSignatures) sigs.Add(JsonValue.Str(s));
                hj.Set("weapons", sigs);
                hist.Add(hj);
            }
            o.Set("history", hist);
            var disc = JsonValue.Arr();
            foreach (var d in Discovered) disc.Add(JsonValue.Str(d));
            o.Set("discovered", disc);
            var log = JsonValue.Arr();
            foreach (var a in ActionLog) log.Add(a);
            o.Set("actions", log);
            return o;
        }

        private static JsonValue OffersToJson(List<ShopOffer> offers)
        {
            var arr = JsonValue.Arr();
            foreach (var s in offers) arr.Add(JsonValue.Obj().Set("cat", s.Category.ToString()).Set("id", s.Id).Set("price", s.Price).Set("locked", s.Locked).Set("sold", s.Sold));
            return arr;
        }

        public static JsonValue WeaponToJson(WeaponInstance w)
        {
            var o = JsonValue.Obj().Set("uid", w.Uid).Set("frame", w.FrameId).Set("core", JsonValue.Str(w.CoreId)).Set("tier", w.Tier)
                .Set("legendary", JsonValue.Str(w.LegendaryId)).Set("locked", w.Locked);
            var mods = JsonValue.Arr();
            foreach (var m in w.Modifiers) mods.Add(JsonValue.Str(m));
            o.Set("mods", mods);
            var runes = JsonValue.Arr();
            foreach (var r in w.Runes) runes.Add(JsonValue.Str(r));
            o.Set("runes", runes);
            return o;
        }

        public static WeaponInstance WeaponFromJson(JsonValue j)
        {
            var w = new WeaponInstance
            {
                Uid = j.Int("uid"),
                FrameId = j.Str("frame"),
                CoreId = j.Str("core"),
                Tier = j.Int("tier", 1),
                LegendaryId = j.Str("legendary"),
                Locked = j.BoolOf("locked")
            };
            w.Modifiers.AddRange(j.StrList("mods"));
            w.Runes.AddRange(j.StrList("runes"));
            return w;
        }

        public static JsonValue BoardToJson(BoardState b)
        {
            var o = JsonValue.Obj().Set("w", b.Width).Set("h", b.Height).Set("nextUid", b.NextUid);
            var arr = JsonValue.Arr();
            foreach (var p in b.Pieces)
                arr.Add(JsonValue.Obj().Set("uid", p.Uid).Set("kind", p.Kind.ToString()).Set("piece", JsonValue.Str(p.PieceId)).Set("weapon", p.WeaponUid)
                    .Set("x", p.Origin.X).Set("y", p.Origin.Y).Set("rot", p.Rotation));
            o.Set("pieces", arr);
            return o;
        }

        public static BoardState BoardFromJson(JsonValue j)
        {
            var b = new BoardState
            {
                Width = Math.Max(BoardState.AbsoluteMin, Math.Min(BoardState.MaxWidth, j.Int("w", BoardState.MinWidth))),
                Height = Math.Max(BoardState.AbsoluteMin, Math.Min(BoardState.MaxHeight, j.Int("h", BoardState.MinHeight))),
                NextUid = Math.Max(1, j.Int("nextUid", 1))
            };
            foreach (var p in j["pieces"].Elements())
            {
                PieceKind kind;
                if (!Enum.TryParse(p.Str("kind", "Relay"), out kind)) continue;
                b.Pieces.Add(new PlacedPiece
                {
                    Uid = p.Int("uid"),
                    Kind = kind,
                    PieceId = p.Str("piece"),
                    WeaponUid = p.Int("weapon", -1),
                    Origin = new Cell(p.Int("x"), p.Int("y")),
                    Rotation = ((p.Int("rot") % 4) + 4) % 4
                });
                if (b.Pieces.Count > 64) break;   // hard cap for untrusted data
            }
            return b;
        }

        private static List<ShopOffer> OffersFromJson(JsonValue arr)
        {
            var list = new List<ShopOffer>();
            foreach (var s in arr.Elements())
            {
                ItemCategory cat;
                if (!Enum.TryParse(s.Str("cat", ""), out cat)) continue;
                list.Add(new ShopOffer { Category = cat, Id = s.Str("id"), Price = s.Int("price"), Locked = s.BoolOf("locked"), Sold = s.BoolOf("sold") });
            }
            return list;
        }

        public static RunState FromJson(JsonValue o)
        {
            var r = new RunState();
            ulong seed;
            ulong.TryParse(o.Str("seed", "0"), out seed);
            r.Seed = seed;
            r.ForgemasterId = o.Str("forgemaster");
            r.Difficulty = o.Int("difficulty", 1);
            r.Mode = o.Str("mode", "adventure");
            r.Chapter = o.Int("chapter");
            r.CurrentNodeId = o.Str("node");
            r.Hp = o.Int("hp");
            r.MaxHpBase = o.Int("maxHpBase", 100);
            r.Gold = o.Int("gold");
            r.Essence = o.Int("essence");
            r.PrimaryWeaponUid = o.Int("primary", -1);
            TargetPriority tp; if (Enum.TryParse(o.Str("targeting", "Nearest"), out tp)) r.Targeting = tp;
            RunPhase ph; if (Enum.TryParse(o.Str("phase", "ChoosingNode"), out ph)) r.Phase = ph;
            RunOutcome oc; if (Enum.TryParse(o.Str("outcome", "None"), out oc)) r.Outcome = oc;
            r.NextUid = o.Int("nextUid", 1);
            r.ShopRerolls = o.Int("shopRerolls");
            r.PendingEventId = o.Str("pendingEvent");
            r.ForgeActionUsed = o.BoolOf("forgeUsed");
            r.RestUsed = o.BoolOf("restUsed");
            r.BattlesFought = o.Int("battles");
            r.ElitesKilled = o.Int("elites");
            r.BossesKilled = o.Int("bosses");
            r.TotalDamage = o["totalDamage"].AsLong();
            r.LegendariesForged = o.Int("legendaries");
            r.ShopDiscountPermille = o.Int("shopDiscount");
            r.DeathReason = o.Str("deathReason");
            r.NextRewardBlessing = o.BoolOf("nextRewardBlessing");
            foreach (var it in o["inventory"].Elements())
            {
                ItemCategory cat;
                if (!Enum.TryParse(it.Str("cat", ""), out cat)) continue;
                r.Inventory.Add(new InventoryItem { Uid = it.Int("uid"), Category = cat, Id = it.Str("id") });
            }
            foreach (var w in o["weapons"].Elements()) r.Weapons.Add(WeaponFromJson(w));
            r.Board = BoardFromJson(o["board"]);
            r.Blessings.AddRange(o.StrList("blessings"));
            foreach (var mj in o["maps"].Elements())
            {
                var m = new ChapterMap { Chapter = mj.Int("chapter"), Biome = mj.Str("biome") };
                foreach (var nj in mj["nodes"].Elements())
                {
                    NodeType nt;
                    Enum.TryParse(nj.Str("type", "Battle"), out nt);
                    var n = new MapNode { Id = nj.Str("id"), Type = nt, Chapter = m.Chapter, Row = nj.Int("row"), Column = nj.Int("col"), EncounterId = nj.Str("enc"), EventId = nj.Str("evt"), Visited = nj.BoolOf("visited") };
                    n.Next.AddRange(nj.StrList("next"));
                    m.Nodes.Add(n);
                }
                r.Maps.Add(m);
            }
            var rng = o["rng"];
            if (rng.Kind == JsonKind.Object)
                foreach (var kv in rng.Fields)
                {
                    var st = new uint[4];
                    for (int i = 0; i < 4; i++) st[i] = (uint)kv.Value[i].AsDouble();
                    r.RngStates[kv.Key] = st;
                }
            r.ShopOffers.AddRange(OffersFromJson(o["shop"]));
            r.LockedCarry.AddRange(OffersFromJson(o["lockedCarry"]));
            foreach (var rw in o["rewards"].Elements())
            {
                ItemCategory cat;
                Enum.TryParse(rw.Str("cat", "Rune"), out cat);
                r.PendingRewards.Add(new RewardChoice { Category = cat, Id = rw.Str("id"), IsBlessing = rw.BoolOf("blessing") });
            }
            foreach (var hj in o["history"].Elements())
            {
                NodeType nt;
                Enum.TryParse(hj.Str("type", "Battle"), out nt);
                var h = new NodeRecord
                {
                    NodeId = hj.Str("node"), Type = nt, EncounterId = hj.Str("enc"), Won = hj.BoolOf("won"), Ticks = hj.Int("ticks"),
                    HpBefore = hj.Int("hpBefore"), HpAfter = hj.Int("hpAfter"), GoldAfter = hj.Int("gold"), DamageDealt = hj["dmg"].AsLong(), KilledBy = hj.Str("killedBy")
                };
                h.WeaponSignatures.AddRange(hj.StrList("weapons"));
                r.History.Add(h);
            }
            r.Discovered.AddRange(o.StrList("discovered"));
            foreach (var a in o["actions"].Elements()) r.ActionLog.Add(a);
            return r;
        }

        /// <summary>Stable hash of the gameplay-relevant state (used by replay/determinism tests).</summary>
        public string StateHash()
        {
            var j = ToJson();
            // The action log itself is excluded so the hash describes resulting state only.
            var copy = JsonValue.Obj();
            foreach (var kv in j.Fields) if (kv.Key != "actions") copy.Set(kv.Key, kv.Value);
            string s = JsonWriter.Write(copy, false);
            ulong h = 1469598103934665603UL;
            foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16");
        }
    }
}
