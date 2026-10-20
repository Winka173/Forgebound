// Forgebound art importer (Unity 2022.3 LTS, Editor only).
//
// Applies import settings to everything under Assets/Forgebound/Resources/Art/Generated/
// from the pipeline's manifest.json (single sprites) and atlases/*.json
// (multi-sprite atlas pages):
//   * textureType Sprite, PPU from manifest (100), custom pivot from manifest
//   * alphaIsTransparency, no mipmaps, bilinear, clamp, no NPOT rescale
//   * compressed HQ (BC7/ASTC per platform defaults), sRGB
//   * atlas pages: SpriteImportMode.Multiple with rects + pivots from JSON
// Menu: Forgebound/Art/Run Art Validator, Reimport Generated Art, Open Report.
//
// NOTE: written against the 2022.3 API but not compiled in the authoring
// environment (no Unity there). Review on first import.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Forgebound.Editor.Art
{
    [Serializable]
    internal class ManifestEntry
    {
        public string file;
        public string id;
        public string category;
        public string variant;
        public string state;
        public int frame;
        public int frame_count;
        public int fps;
        public int[] size;
        public float[] pivot;
        public int ppu;
        public string blend;
        public string sha256;
    }

    [Serializable]
    internal class Manifest
    {
        public string schema;
        public int ppu;
        public ManifestEntry[] files;
    }

    [Serializable]
    internal class AtlasPage
    {
        public int index;
        public string image;
        public int[] size;
    }

    [Serializable]
    internal class AtlasSprite
    {
        public string name;
        public int page;
        public int[] rect_unity;
        public float[] pivot;
    }

    [Serializable]
    internal class AtlasMeta
    {
        public string schema;
        public string group;
        public int ppu;
        public int padding;
        public AtlasPage[] pages;
        public AtlasSprite[] sprites;
    }

    public class ForgeboundArtImporter : AssetPostprocessor
    {
        public const string GeneratedRoot = "Assets/Forgebound/Resources/Art/Generated/";
        public const string AtlasFolder = GeneratedRoot + "atlases/";
        const string PythonPrefKey = "Forgebound.Art.PythonExe";

        static Dictionary<string, ManifestEntry> s_Entries;
        static DateTime s_ManifestStamp;

        // Bump to force Unity to re-run the importer when rules change.
        public override uint GetVersion() => 3;

        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        static Dictionary<string, ManifestEntry> Entries
        {
            get
            {
                string path = Path.Combine(ProjectRoot, GeneratedRoot, "manifest.json");
                if (!File.Exists(path))
                    return s_Entries = new Dictionary<string, ManifestEntry>();
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (s_Entries == null || stamp != s_ManifestStamp)
                {
                    s_Entries = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
                    try
                    {
                        Manifest m = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
                        if (m != null && m.files != null)
                            foreach (ManifestEntry e in m.files)
                                if (!string.IsNullOrEmpty(e.file))
                                    s_Entries[GeneratedRoot + e.file] = e;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Forgebound Art] Could not parse manifest.json: {ex.Message}");
                    }
                    s_ManifestStamp = stamp;
                }
                return s_Entries;
            }
        }

        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(GeneratedRoot, StringComparison.Ordinal) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return;
            var importer = (TextureImporter)assetImporter;
            ApplyCommon(importer);

            if (path.StartsWith(AtlasFolder, StringComparison.Ordinal))
            {
                ApplyAtlas(importer, path);
                return;
            }

            if (!Entries.TryGetValue(path, out ManifestEntry entry))
            {
                Debug.LogWarning($"[Forgebound Art] {path} is not listed in manifest.json (no provenance). Run the art pipeline.");
                return;
            }

            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = entry.ppu > 0 ? entry.ppu : 100;
            if (entry.size != null && entry.size.Length == 2)
                importer.maxTextureSize = NextPow2(Mathf.Max(entry.size[0], entry.size[1]));

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMode = (int)SpriteImportMode.Single;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = entry.pivot != null && entry.pivot.Length == 2
                ? new Vector2(entry.pivot[0], entry.pivot[1])
                : new Vector2(0.5f, 0.5f);
            // Animation frames share one pivot and canvas; FullRect keeps them aligned.
            settings.spriteMeshType = entry.frame_count > 1 ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
            settings.spriteExtrude = 1;
            importer.SetTextureSettings(settings);
            importer.spritePixelsPerUnit = entry.ppu > 0 ? entry.ppu : 100;

            if (entry.blend == "additive")
                importer.userData = "forgebound:blend=additive"; // render with an additive sprite material
        }

        static void ApplyCommon(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true; // pipeline already colour-bleeds; this is belt and braces
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.crunchedCompression = false;
        }

        static void ApplyAtlas(TextureImporter importer, string path)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            // fb_atlas_<group>.png or fb_atlas_<group>_<page>.png -> fb_atlas_<group>.json
            string dir = Path.Combine(ProjectRoot, Path.GetDirectoryName(path) ?? "");
            AtlasMeta meta = null;
            int pageIndex = 0;
            foreach (string jsonPath in Directory.GetFiles(dir, "fb_atlas_*.json"))
            {
                AtlasMeta m;
                try { m = JsonUtility.FromJson<AtlasMeta>(File.ReadAllText(jsonPath)); }
                catch (Exception ex)
                {
                    Debug.LogError($"[Forgebound Art] Bad atlas json {jsonPath}: {ex.Message}");
                    continue;
                }
                if (m?.pages == null) continue;
                foreach (AtlasPage p in m.pages)
                {
                    if (Path.GetFileNameWithoutExtension(p.image) == file)
                    {
                        meta = m;
                        pageIndex = p.index;
                    }
                }
                if (meta != null) break;
            }
            if (meta == null)
            {
                Debug.LogWarning($"[Forgebound Art] No atlas metadata for {path}");
                return;
            }

            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = meta.ppu > 0 ? meta.ppu : 100;
            importer.maxTextureSize = 4096;
            var sprites = new List<SpriteMetaData>();
            foreach (AtlasSprite s in meta.sprites)
            {
                if (s.page != pageIndex || s.rect_unity == null || s.rect_unity.Length != 4) continue;
                sprites.Add(new SpriteMetaData
                {
                    name = s.name,
                    rect = new Rect(s.rect_unity[0], s.rect_unity[1], s.rect_unity[2], s.rect_unity[3]),
                    alignment = (int)SpriteAlignment.Custom,
                    pivot = s.pivot != null && s.pivot.Length == 2 ? new Vector2(s.pivot[0], s.pivot[1]) : new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                });
            }
#pragma warning disable 0618 // spritesheet is the simplest stable API in 2022.3 (ISpriteEditorDataProvider is the newer route)
            importer.spritesheet = sprites.ToArray();
#pragma warning restore 0618
        }

        static int NextPow2(int v)
        {
            int p = 32;
            while (p < v && p < 8192) p <<= 1;
            return p;
        }

        // ------------------------------------------------------------ menu items
        [MenuItem("Forgebound/Art/Run Art Validator")]
        public static void RunValidator()
        {
            string python = EditorPrefs.GetString(PythonPrefKey,
                Application.platform == RuntimePlatform.WindowsEditor ? "python" : "python3");
            string script = Path.Combine(ProjectRoot, "tools", "art", "art_validator.py");
            if (!File.Exists(script))
            {
                EditorUtility.DisplayDialog("Forgebound Art", $"Validator not found:\n{script}", "OK");
                return;
            }
            var psi = new ProcessStartInfo
            {
                FileName = python,
                Arguments = $"\"{script}\"",
                WorkingDirectory = ProjectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            try
            {
                EditorUtility.DisplayProgressBar("Forgebound Art", "Running art validator...", 0.5f);
                using (Process proc = Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    EditorUtility.ClearProgressBar();
                    string msg = stdout + (string.IsNullOrEmpty(stderr) ? "" : "\n" + stderr);
                    if (proc.ExitCode == 0) Debug.Log("[Forgebound Art] Validation PASSED\n" + msg);
                    else Debug.LogError($"[Forgebound Art] Validation exit code {proc.ExitCode}\n" + msg);
                    bool open = EditorUtility.DisplayDialog("Forgebound Art Validator",
                        (proc.ExitCode == 0 ? "PASS" : proc.ExitCode == 1 ? "FAIL" : "ERROR") +
                        "\n\nAutomated checks do not certify aesthetic quality.\n\n" + Truncate(stdout, 1200),
                        "Open HTML report", "Close");
                    if (open) OpenReport();
                }
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[Forgebound Art] Could not run '{python}'. Set EditorPrefs '{PythonPrefKey}' to your Python 3 (needs Pillow + numpy). {ex.Message}");
            }
        }

        [MenuItem("Forgebound/Art/Open Validation Report")]
        public static void OpenReport()
        {
            string report = Path.Combine(ProjectRoot, "reports", "art", "art_validation.html");
            if (File.Exists(report)) EditorUtility.OpenWithDefaultApp(report);
            else Debug.LogWarning("[Forgebound Art] No report yet: run Forgebound/Art/Run Art Validator.");
        }

        [MenuItem("Forgebound/Art/Reimport Generated Art")]
        public static void ReimportGenerated()
        {
            s_Entries = null;
            AssetDatabase.ImportAsset(GeneratedRoot.TrimEnd('/'), ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        }

        static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "\n...";
    }
}
