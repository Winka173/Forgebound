using System.Collections.Generic;
using UnityEngine;

namespace Forgebound.Presentation
{
    /// <summary>
    /// World-space side-view stage (orthographic camera, sprites). Hidden while menus are shown.
    /// 1 world unit = 100 px at PPU 100 = 1000 simulation milli-units.
    /// </summary>
    public sealed class BattleWorld : MonoBehaviour
    {
        public static BattleWorld Instance { get; private set; }
        public Camera Camera { get; private set; }
        public Transform Stage { get; private set; }
        private SpriteRenderer _sky, _ground, _horizon;
        private readonly List<SpriteRenderer> _props = new List<SpriteRenderer>();
        private Vector3 _camBase;
        private float _shake;
        public float ShakeScale = 0.6f;

        public static void Create()
        {
            var go = new GameObject("BattleWorld");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<BattleWorld>();
            Instance.Build();
            Instance.SetVisible(false);
        }

        private void Build()
        {
            var camGo = new GameObject("BattleCamera");
            camGo.transform.SetParent(transform, false);
            Camera = camGo.AddComponent<Camera>();
            Camera.orthographic = true;
            Camera.orthographicSize = 5.4f;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Theme.Bg;
            Camera.depth = -1;
            _camBase = new Vector3(0, 0.6f, -10);
            camGo.transform.position = _camBase;

            Stage = new GameObject("Stage").transform;
            Stage.SetParent(transform, false);
            _sky = Quad("Sky", new Vector3(0, 1.5f, 5), new Vector2(40, 14), Theme.Hex("1b1520"), -100);
            _horizon = Quad("Horizon", new Vector3(0, -1.2f, 4), new Vector2(40, 2.5f), Theme.Hex("2a1f1a"), -90);
            _ground = Quad("Ground", new Vector3(0, -4.2f, 3), new Vector2(40, 4f), Theme.Hex("3a2a20"), -80);
        }

        private SpriteRenderer Quad(string name, Vector3 pos, Vector2 size, Color c, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteLibrary.Primitive(SpriteLibrary.Shape.Square);
            sr.color = c;
            sr.sortingOrder = order;
            go.transform.localScale = new Vector3(size.x / 0.64f, size.y / 0.64f, 1);
            return sr;
        }

        /// <summary>Tint the stage with the biome palette (palette[0]=sky, [1]=accent, [2]=highlight, [3]=ground).</summary>
        public void ApplyBiome(Forgebound.Core.Content.BiomeDef biome)
        {
            foreach (var p in _props) if (p != null) Destroy(p.gameObject);
            _props.Clear();
            if (biome == null || biome.Palette.Count < 4) return;
            _sky.color = Theme.Hex(biome.Palette[0]);
            _horizon.color = Color.Lerp(Theme.Hex(biome.Palette[0]), Theme.Hex(biome.Palette[3]), 0.6f);
            _ground.color = Theme.Hex(biome.Palette[3]);
            var sprite = GameRoot.Services.Sprites.Find("fb_env_" + biome.Id + "_bg", "fb_env_" + biome.Id);
            if (sprite != null)
            {
                var bg = new GameObject("BiomeBackdrop").AddComponent<SpriteRenderer>();
                bg.transform.SetParent(transform, false);
                bg.transform.localPosition = new Vector3(0, 1.0f, 4.5f);
                bg.sprite = sprite;
                bg.sortingOrder = -95;
                _props.Add(bg);
            }
            // Accent silhouettes (simple parallax pillars) so biomes read differently even before env art lands.
            var rng = new System.Random(biome.Id.GetHashCode());
            for (int i = 0; i < 7; i++)
            {
                var go = new GameObject("Prop");
                go.transform.SetParent(transform, false);
                float x = -9f + i * 3f + (float)rng.NextDouble();
                float h = 1.5f + (float)rng.NextDouble() * 2.5f;
                go.transform.localPosition = new Vector3(x, -1.6f + h * 0.5f, 4.2f);
                go.transform.localScale = new Vector3(0.6f / 0.64f, h / 0.64f, 1);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = SpriteLibrary.Primitive(SpriteLibrary.Shape.Rounded);
                sr.color = Color.Lerp(Theme.Hex(biome.Palette[0]), Theme.Hex(biome.Palette[1]), 0.18f);
                sr.sortingOrder = -92;
                _props.Add(sr);
            }
        }

        public void SetVisible(bool visible)
        {
            Camera.enabled = visible;
            Stage.gameObject.SetActive(visible);
            foreach (Transform t in transform) if (t != Camera.transform) t.gameObject.SetActive(visible);
        }

        public void Shake(float amount)
        {
            _shake = Mathf.Max(_shake, amount * ShakeScale);
        }

        private void LateUpdate()
        {
            if (_shake > 0.001f)
            {
                Camera.transform.position = _camBase + (Vector3)(Random.insideUnitCircle * _shake * 0.25f);
                _shake = Mathf.MoveTowards(_shake, 0, Time.unscaledDeltaTime * 3f);
            }
            else Camera.transform.position = _camBase;
        }

        /// <summary>Simulation milli-units to world X (arena 0..18000 centred).</summary>
        public static float WorldX(int milli) { return milli / 1000f - 9f; }
        public const float GroundY = -2.4f;
        public const float AirY = -0.2f;
    }
}
