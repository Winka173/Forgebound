using System.Collections.Generic;
using Forgebound.Core.Content;
using UnityEngine;

namespace Forgebound.Presentation
{
    /// <summary>
    /// Resolves sprites produced by the Blender pipeline (Resources/Art/**, naming per docs/ART_BIBLE.md).
    /// When an asset is missing, a procedural fallback shape is generated and the miss is recorded in
    /// <see cref="Missing"/> (shown in the debug overlay / art report) — fallbacks are never treated as final art.
    /// </summary>
    public sealed class SpriteLibrary
    {
        public const float PixelsPerUnit = 100f;
        private readonly Dictionary<string, Sprite> _byName = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, Sprite> _fallbacks = new Dictionary<string, Sprite>();
        public readonly HashSet<string> Missing = new HashSet<string>();
        public int Loaded { get { return _byName.Count; } }

        public SpriteLibrary()
        {
            foreach (var s in Resources.LoadAll<Sprite>("Art"))
                if (s != null && !_byName.ContainsKey(s.name)) _byName[s.name] = s;
        }

        public Sprite Find(params string[] candidates)
        {
            foreach (var c in candidates)
            {
                Sprite s;
                if (c != null && _byName.TryGetValue(c, out s)) return s;
            }
            return null;
        }

        /// <summary>Frames of an animation clip: name_000, name_001 ... (stops at first gap).</summary>
        public List<Sprite> Sequence(string baseName)
        {
            var list = new List<Sprite>();
            for (int i = 0; i < 64; i++)
            {
                Sprite s;
                if (_byName.TryGetValue(baseName + "_" + i.ToString("000"), out s) || _byName.TryGetValue(baseName + "_" + i.ToString("00"), out s)) list.Add(s);
                else if (i > 0) break;
            }
            return list;
        }

        public Sprite Weapon(string frame, string core, int tier, string legendary, bool ingame = false)
        {
            // Pipeline naming: fb_weapon_<frame>_<core>-t<tier>_<icon|ingame>_00, legendary: -leg (docs/ART_BIBLE.md)
            string c = core ?? "steel";
            string kind = ingame ? "ingame" : "icon";
            var s = Find(
                legendary != null ? "fb_weapon_" + frame + "_" + c + "-leg_" + kind + "_00" : null,
                "fb_weapon_" + frame + "_" + c + "-t" + tier + "_" + kind + "_00",
                "fb_weapon_" + frame + "_" + c + "-t3_" + kind + "_00",
                "fb_weapon_" + frame + "_" + c + "-t1_" + kind + "_00",
                ingame ? "fb_weapon_" + frame + "_" + c + "-t1_icon_00" : null);
            if (s != null) return s;
            Missing.Add("weapon:" + frame + "|" + c + "|" + kind);
            return Fallback("weapon_" + frame, Shape.Diamond, CoreColor(core));
        }

        /// <summary>Additive legendary glow layer rendered behind the weapon (null if not produced yet).</summary>
        public Sprite LegendaryGlow(string frame, string core)
        {
            return Find("fb_weapon_" + frame + "_" + (core ?? "steel") + "-leg_glow_00");
        }

        public Sprite Piece(string id)
        {
            var s = Find("fb_board_" + id + "_icon_00", "fb_board_" + id + "_icon", "fb_piece_" + id + "_icon");
            if (s != null) return s;
            Missing.Add("piece:" + id);
            return Fallback("piece_" + id, Shape.Rounded, Theme.Link);
        }

        public Sprite Icon(string category, string id)
        {
            var s = Find("fb_" + category + "_" + id + "_icon", "fb_icon_" + category + "_" + id,
                // Frames have no dedicated icon: show the plain steel tier-1 weapon of that frame.
                category == "frame" ? "fb_weapon_" + id + "_steel-t1_icon_00" : null);
            if (s != null) return s;
            Missing.Add(category + ":" + id);
            return Fallback(category + "_" + id, Shape.Circle, category == "core" ? CoreColor(id) : Theme.Gold);
        }

        public List<Sprite> CharacterClip(string id, string state)
        {
            var seq = Sequence("fb_char_" + id + "_" + state);
            if (seq.Count == 0) seq = Sequence("fb_character_" + id + "_" + state);
            if (seq.Count == 0) seq = Sequence("fb_forgemaster_" + id + "_" + state);
            if (seq.Count == 0)
            {
                // Only the benchmark Forgemaster is rendered so far; others reuse its rig (tinted by the view) until produced.
                Missing.Add("character:" + id + ":" + state);
                seq = Sequence("fb_char_blacksmith_" + state);
                if (seq.Count == 0) seq.Add(Fallback("char_" + id, Shape.Capsule, Theme.Gold));
            }
            return seq;
        }

        public List<Sprite> EnemyClip(EnemyDef def, string state)
        {
            var seq = Sequence("fb_enemy_" + def.Id + "_" + state);
            if (seq.Count == 0)
            {
                // Archetype stand-in until per-enemy art is produced (tracked as missing, never reported as final).
                Missing.Add("enemy:" + def.Id + ":" + state);
                seq = Sequence("fb_enemy_" + Archetype(def) + "_" + state);
                if (seq.Count == 0) seq = Sequence("fb_enemy_" + Archetype(def) + "_idle");
            }
            if (seq.Count == 0) seq.Add(Fallback("enemy_" + def.Id, Shape.Capsule, Theme.Danger));
            return seq;
        }

        public static string Archetype(EnemyDef def)
        {
            if (def.Flying) return "bat";
            if (def.Tags.Contains("hollow_dead")) return "skeleton";
            if (def.Role == "tank" || def.Role == "bruiser" || def.Rank != EnemyRank.Minion) return "golem";
            return def.Tags.Contains("overgrowth") || def.Role == "swarm" ? "slime" : "skeleton";
        }

        public static Color CoreColor(string core)
        {
            switch (core)
            {
                case "fire": return Theme.Hex("ff6a2a");
                case "frost": return Theme.Hex("7fd8ff");
                case "lightning": return Theme.Hex("ffe24a");
                case "earth": return Theme.Hex("b98a4e");
                case "life": return Theme.Hex("7dff8a");
                case "shadow": return Theme.Hex("7a4bd6");
                case "venom": return Theme.Hex("9be22d");
                case "arcane": return Theme.Hex("4fa3ff");
                case "wind": return Theme.Hex("bff5ea");
                case "steel": return Theme.Hex("a9b8c9");
                case "solar": return Theme.Hex("ffd76a");
                case "void": return Theme.Hex("9b2cff");
                default: return Theme.Hex("c9bcae");
            }
        }

        public enum Shape { Circle, Rounded, Diamond, Capsule, Square, Ring, Spark }

        /// <summary>Procedurally generated fallback / VFX primitive (also used intentionally for VFX particles).</summary>
        public Sprite Fallback(string key, Shape shape, Color tint)
        {
            string k = key + ":" + shape;
            Sprite s;
            if (_fallbacks.TryGetValue(k, out s)) return s;
            s = Primitive(shape, 64);
            s.name = "fallback_" + key;
            _fallbacks[k] = s;
            return s;
        }

        private static readonly Dictionary<Shape, Sprite> _primitives = new Dictionary<Shape, Sprite>();

        /// <summary>White anti-aliased primitive (tinted by SpriteRenderer/Image colour). Cached per shape.</summary>
        public static Sprite Primitive(Shape shape, int size = 64)
        {
            Sprite cached;
            if (_primitives.TryGetValue(shape, out cached)) return cached;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = (x + 0.5f - r) / r, fy = (y + 0.5f - r) / r;
                    float d;
                    switch (shape)
                    {
                        case Shape.Circle: d = Mathf.Sqrt(fx * fx + fy * fy); break;
                        case Shape.Ring: d = Mathf.Abs(Mathf.Sqrt(fx * fx + fy * fy) - 0.8f) * 5f; break;
                        case Shape.Diamond: d = Mathf.Abs(fx) + Mathf.Abs(fy); break;
                        case Shape.Capsule: d = Mathf.Max(Mathf.Abs(fx) * 1.6f, Mathf.Sqrt(fx * fx * 0.3f + fy * fy) * 1.02f); break;
                        case Shape.Spark: d = Mathf.Min(Mathf.Abs(fx) * 4f + Mathf.Abs(fy) * 0.9f, Mathf.Abs(fy) * 4f + Mathf.Abs(fx) * 0.9f); break;
                        case Shape.Rounded: d = Mathf.Pow(Mathf.Pow(Mathf.Abs(fx), 6) + Mathf.Pow(Mathf.Abs(fy), 6), 1f / 6f); break;
                        default: d = Mathf.Max(Mathf.Abs(fx), Mathf.Abs(fy)); break;
                    }
                    float a = Mathf.Clamp01((1f - d) * r * 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            cached = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            cached.name = "prim_" + shape;
            _primitives[shape] = cached;
            return cached;
        }
    }
}
