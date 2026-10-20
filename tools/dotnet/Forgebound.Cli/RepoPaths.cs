using System;
using System.Collections.Generic;
using System.IO;
using Forgebound.Core.Content;

namespace Forgebound.Cli
{
    public sealed class FileContentSource : IContentSource
    {
        private readonly string _dir;
        public FileContentSource(string dir) { _dir = dir; }
        public string Read(string name)
        {
            var p = Path.Combine(_dir, name + ".json");
            return File.Exists(p) ? File.ReadAllText(p) : null;
        }
    }

    public static class RepoPaths
    {
        public static string Root()
        {
            var env = Environment.GetEnvironmentVariable("FORGEBOUND_ROOT");
            if (!string.IsNullOrEmpty(env)) return env;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Forgebound"))) return dir.FullName;
                dir = dir.Parent;
            }
            dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "Forgebound"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Cannot locate repository root (set FORGEBOUND_ROOT)");
        }

        public static string ContentDir() { return Path.Combine(Root(), "Assets", "Forgebound", "Resources", "Content"); }
        public static string LocDir() { return Path.Combine(Root(), "Assets", "Forgebound", "Resources", "Localization"); }

        public static ContentDatabase LoadDb() { return ContentDatabase.Load(new FileContentSource(ContentDir())); }

        public static Dictionary<string, string> LoadLoc(string lang)
        {
            var p = Path.Combine(LocDir(), lang + ".json");
            if (!File.Exists(p)) return null;
            var j = JsonParser.Parse(File.ReadAllText(p));
            var d = new Dictionary<string, string>();
            foreach (var kv in j["strings"].Fields) d[kv.Key] = kv.Value.AsString("");
            return d;
        }
    }
}
