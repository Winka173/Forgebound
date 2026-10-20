using System.Collections.Generic;
using Forgebound.Core.Content;
using Forgebound.Core.ForgeBoard;
using Forgebound.Core.Weapons;

namespace Forgebound.Core.Combat
{
    /// <summary>
    /// Turns active board links into runtime signal rules. The first "charging" relay on a path (Echo, Capacitor, Governor)
    /// decides which source event charges the link and the threshold; later relays shape the proc.
    /// A direct weapon→weapon port connection is a "Resonance" link: OnHit, threshold 4, 50% power.
    /// Inactive links (capacity exceeded / no destination) are never compiled, so disconnected circuits can't apply effects.
    /// </summary>
    public static class LinkCompiler
    {
        public const int ResonanceThreshold = 4;
        public const int ResonancePower = 500;

        public static List<CompiledLink> Compile(BoardAnalysis analysis, BoardState board, IPieceShapeResolver shapes, List<WeaponSpec> weapons, StatBlock heroStats)
        {
            var result = new List<CompiledLink>();
            var indexByUid = new Dictionary<int, int>();
            for (int i = 0; i < weapons.Count; i++) indexByUid[weapons[i].Uid] = i;
            int thresholdBonus = heroStats != null ? heroStats.SumUntagged(StatType.RelayThreshold) : 0;

            foreach (var link in analysis.Links)
            {
                if (!link.Active) continue;
                int src;
                if (!indexByUid.TryGetValue(link.SourceWeaponUid, out src)) continue;
                int dst = -1;
                if (link.DestWeaponUid >= 0 && !indexByUid.TryGetValue(link.DestWeaponUid, out dst)) continue;

                var c = new CompiledLink
                {
                    Index = result.Count,
                    SourceWeapon = src,
                    DestWeapon = dst,
                    Listen = TriggerType.OnHit,
                    Threshold = ResonanceThreshold,
                    PowerPermille = ResonancePower
                };
                c.RelayUids.AddRange(link.RelayUids);
                bool charged = false;
                foreach (var ruid in link.RelayUids)
                {
                    var piece = board.Find(ruid);
                    var r = piece != null ? shapes.GetRelay(piece) : null;
                    if (r == null) continue;
                    switch (r.Type)
                    {
                        case RelayType.Echo:
                        case RelayType.Capacitor:
                        case RelayType.Governor:
                            if (!charged)
                            {
                                c.Listen = r.Listen;
                                c.Threshold = r.Threshold;
                                c.PowerPermille = r.Permille;
                                charged = true;
                            }
                            else c.PowerPermille = c.PowerPermille * 1100 / 1000;
                            if (r.Type == RelayType.Governor)
                            {
                                c.Threshold = System.Math.Max(1, c.Threshold - 1);
                                c.MaxPerBattle = r.MaxPerBattle;
                            }
                            if (r.Type == RelayType.Capacitor) { c.CapacitorNova = true; c.RadiusMilli = r.RadiusMilli; }
                            break;
                        case RelayType.Amplifier:
                        case RelayType.Splitter:
                            c.PowerPermille = c.PowerPermille * r.Permille / 1000;
                            break;
                        case RelayType.Delay:
                            c.DelayTicks += r.DelayTicks;
                            c.PowerPermille = c.PowerPermille * r.Permille / 1000;
                            break;
                        case RelayType.Converter:
                            c.ConvertDamageType = r.ConvertDamageType;
                            if (r.Status != null) c.AddStatus = r.Status;
                            break;
                        case RelayType.Prism:
                            c.AddStatus = r.Status ?? weapons[src].Status;
                            c.PowerPermille = c.PowerPermille * r.Permille / 1000;
                            break;
                        case RelayType.Siphon:
                            c.Siphon = true;
                            break;
                        case RelayType.Beacon:
                            c.Beacon = true;
                            break;
                    }
                }
                c.Threshold = System.Math.Max(1, c.Threshold - weapons[src].LinkThresholdReduce - thresholdBonus);
                c.Description = link.Describe();
                result.Add(c);
            }
            return result;
        }
    }
}
