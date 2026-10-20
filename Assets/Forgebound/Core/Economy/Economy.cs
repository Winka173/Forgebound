using System;
using System.Collections.Generic;
using Forgebound.Core.Content;
using Forgebound.Core.Run;
using Forgebound.Core.Simulation;
using Forgebound.Core.Weapons;

namespace Forgebound.Core.Economy
{
    /// <summary>
    /// Economy rules (single per-run spend currency Gold + rare Forge Essence; items are never currencies).
    /// All numbers are documented in docs/ECONOMY.md and covered by EconomyTests.
    /// </summary>
    public static class EconomyRules
    {
        public const int BaseShopOffers = 6;
        public const int MaxShopOffers = 8;
        public const int RerollBase = 8;
        public const int RerollStep = 4;
        public const int RerollCap = 40;
        public const int SellPermille = 500;
        public const int TierUpgradeCost2 = 55;
        public const int TierUpgradeCost3 = 110;
        public const int BoardExpandCost = 70;
        public const int RestHealPermille = 300;
        public const int SkipRewardGold = 12;

        public static int RarityPricePermille(Rarity r)
        {
            switch (r)
            {
                case Rarity.Uncommon: return 1150;
                case Rarity.Rare: return 1400;
                case Rarity.Epic: return 1800;
                case Rarity.Legendary: return 2400;
                default: return 1000;
            }
        }

        public static int Price(ContentDatabase db, ItemCategory cat, string id, int discountPermille)
        {
            int baseCost = db.ItemCost(cat, id);
            int p = FMath.Permille(baseCost, RarityPricePermille(db.ItemRarity(cat, id)));
            p = FMath.Permille(p, Math.Max(300, 1000 - discountPermille));
            return Math.Max(5, p);
        }

        public static int SellValue(ContentDatabase db, ItemCategory cat, string id)
        {
            return Math.Max(2, FMath.Permille(db.ItemCost(cat, id), SellPermille));
        }

        public static int RerollCost(int rerollsThisVisit, int discountPermille)
        {
            int c = Math.Min(RerollCap, RerollBase + RerollStep * rerollsThisVisit);
            return Math.Max(1, FMath.Permille(c, Math.Max(200, 1000 - discountPermille)));
        }

        public static int TierUpgradeCost(int currentTier)
        {
            return currentTier <= 1 ? TierUpgradeCost2 : TierUpgradeCost3;
        }

        /// <summary>Rarity roll table per chapter (permille weights Common/Uncommon/Rare/Epic).</summary>
        public static int[] RarityWeights(int chapter)
        {
            switch (chapter)
            {
                case 0: return new[] { 620, 290, 85, 5 };
                case 1: return new[] { 450, 350, 170, 30 };
                default: return new[] { 300, 360, 270, 70 };
            }
        }
    }

    public static class ShopGenerator
    {
        private static readonly ItemCategory[] Categories = { ItemCategory.Frame, ItemCategory.Core, ItemCategory.Modifier, ItemCategory.Rune, ItemCategory.Piece };

        /// <summary>
        /// Weighted, compatibility-aware offers. Not auto-winning: at most one "build memory" slot is biased toward
        /// components that make the player's current weapons legal/closer to a recipe, and pity rules recover bad RNG:
        ///  - fewer than 2 weapons owned → at least one Frame;
        ///  - a weapon without core → at least one Core;
        ///  - no relay owned on board/inventory after the first shop → at least one board piece.
        /// </summary>
        public static List<ShopOffer> Generate(ContentDatabase db, RunState run, DetRandom rng, int offerCount, int discountPermille, string favoriteRecipe)
        {
            var offers = new List<ShopOffer>();
            foreach (var l in run.LockedCarry) offers.Add(new ShopOffer { Category = l.Category, Id = l.Id, Price = l.Price, Locked = true });
            var taken = new HashSet<string>();
            foreach (var o in offers) taken.Add(o.Category + ":" + o.Id);

            var catWeights = new[] { 13, 18, 27, 27, 15 };
            bool needFrame = run.Weapons.Count < 2;
            bool needCore = false;
            foreach (var w in run.Weapons) if (w.CoreId == null) needCore = true;
            bool hasRelay = false;
            foreach (var it in run.Inventory) if (it.Category == ItemCategory.Piece) hasRelay = true;
            foreach (var p in run.Board.Pieces) if (p.Kind != PieceKind.Weapon) hasRelay = true;

            var forced = new List<ItemCategory>();
            if (needFrame) forced.Add(ItemCategory.Frame);
            if (needCore) forced.Add(ItemCategory.Core);
            if (!hasRelay && run.BattlesFought >= 1) forced.Add(ItemCategory.Piece);

            int guard = 0;
            while (offers.Count < offerCount && guard++ < 200)
            {
                ItemCategory cat;
                if (forced.Count > 0) { cat = forced[0]; forced.RemoveAt(0); }
                else cat = Categories[Math.Max(0, rng.WeightedIndex(catWeights))];
                var rarity = (Rarity)Math.Max(0, rng.WeightedIndex(EconomyRules.RarityWeights(run.Chapter)));
                string id = null;
                // Build memory slot: bias toward missing components of the favourite / closest recipe.
                if (offers.Count == offerCount - 1 && rng.Chance(300)) id = RecipeHint(db, run, favoriteRecipe, taken);
                if (id != null)
                {
                    var hint = id.Split(':');
                    ItemCategory hc;
                    if (Enum.TryParse(hint[0], out hc)) { cat = hc; id = hint[1]; }
                    else id = null;
                }
                if (id == null) id = PickItem(db, run, cat, rarity, rng, taken);
                if (id == null) continue;
                taken.Add(cat + ":" + id);
                offers.Add(new ShopOffer { Category = cat, Id = id, Price = EconomyRules.Price(db, cat, id, discountPermille) });
            }
            return offers;
        }

        private static string RecipeHint(ContentDatabase db, RunState run, string favorite, HashSet<string> taken)
        {
            RecipeStatus best = null;
            foreach (var w in run.Weapons)
            {
                foreach (var st in LegendaryRules.Candidates(db, w, 99))
                {
                    if (favorite != null && st.Recipe.Id != favorite && best != null) continue;
                    if (best == null || st.Missing.Count < best.Missing.Count || (favorite != null && st.Recipe.Id == favorite)) best = st;
                }
            }
            if (best == null) return null;
            foreach (var m in best.Missing)
            {
                var parts = m.Split(':');
                if (parts.Length < 2) continue;
                string cat = parts[0] == "modifier" ? "Modifier" : parts[0] == "core" ? "Core" : parts[0] == "rune" ? "Rune" : null;
                if (cat == null) continue;
                string id = parts[1].Split('|')[0];
                if (!taken.Contains(cat + ":" + id)) return cat + ":" + id;
            }
            return null;
        }

        /// <summary>Pick an item of a category near the rolled rarity, preferring items usable with the current loadout.</summary>
        public static string PickItem(ContentDatabase db, RunState run, ItemCategory cat, Rarity rarity, DetRandom rng, HashSet<string> taken)
        {
            var ids = new List<string>();
            var weights = new List<int>();
            void Consider(string id, Rarity r, List<string> requires)
            {
                if (taken.Contains(cat + ":" + id)) return;
                int dist = Math.Abs((int)r - (int)rarity);
                if (r == Rarity.Legendary) return;
                int w = dist == 0 ? 10 : (dist == 1 ? 3 : 1);
                // Compatibility awareness: items no current weapon can use are less likely (not impossible: future frames).
                if (requires != null && requires.Count > 0 && run.Weapons.Count > 0)
                {
                    bool usable = false;
                    foreach (var wpn in run.Weapons) if (AssemblyRules.TagsMatchAny(requires, AssemblyRules.WeaponTags(db, wpn))) usable = true;
                    if (!usable) w = Math.Max(1, w / 3);
                }
                ids.Add(id);
                weights.Add(w);
            }
            switch (cat)
            {
                case ItemCategory.Frame: foreach (var d in db.Frames) Consider(d.Id, d.Rarity, null); break;
                case ItemCategory.Core: foreach (var d in db.Cores) Consider(d.Id, d.Rarity, null); break;
                case ItemCategory.Modifier: foreach (var d in db.Modifiers) Consider(d.Id, d.Rarity, d.RequiresTags); break;
                case ItemCategory.Rune: foreach (var d in db.Runes) Consider(d.Id, d.Rarity, d.RequiresTags); break;
                case ItemCategory.Piece: foreach (var d in db.Pieces) Consider(d.Id, d.Rarity, null); break;
            }
            if (ids.Count == 0) return null;
            return ids[Math.Max(0, rng.WeightedIndex(weights.ToArray()))];
        }
    }
}
