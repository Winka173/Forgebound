using System.Collections.Generic;
using System.IO;
using Forgebound.Core.Content;
using Forgebound.Core.ForgeBoard;
using Forgebound.Core.Run;
using Forgebound.Core.Weapons;

namespace Forgebound.Tests
{
    /// <summary>Loads the real shipped content in both Unity (Resources) and headless dotnet test runs.</summary>
    public static class TestContent
    {
        private static ContentDatabase _db;

        public static ContentDatabase Db
        {
            get
            {
                if (_db != null) return _db;
#if UNITY_5_3_OR_NEWER
                _db = ContentDatabase.Load(new Forgebound.Presentation.ResourcesContentSource());
#else
                var files = new Dictionary<string, string>();
                var names = new List<string>(ContentDatabase.FileNames) { "tuning" };
                foreach (var name in names)
                {
                    var p = Path.Combine(FindRoot(), "Assets", "Forgebound", "Resources", "Content", name + ".json");
                    if (File.Exists(p)) files[name] = File.ReadAllText(p);
                }
                _db = ContentDatabase.Load(new DictionaryContentSource(files));
#endif
                return _db;
            }
        }

        public static string FindRoot()
        {
            var env = System.Environment.GetEnvironmentVariable("FORGEBOUND_ROOT");
            if (!string.IsNullOrEmpty(env)) return env;
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Forgebound"))) return dir.FullName;
                dir = dir.Parent;
            }
            return Directory.GetCurrentDirectory();
        }

        /// <summary>A shape resolver over an explicit weapon list (for board tests without a run).</summary>
        public static ContentShapeResolver Shapes(List<WeaponInstance> weapons)
        {
            return new ContentShapeResolver(Db, uid => { foreach (var w in weapons) if (w.Uid == uid) return w; return null; });
        }
    }
}
