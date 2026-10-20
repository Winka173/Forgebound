using System;
using System.Collections.Generic;
using Forgebound.Core.Content;

namespace Forgebound.Core.Save
{
    /// <summary>Platform file access (Unity: Application.persistentDataPath; tools/tests: temp dir or in-memory).</summary>
    public interface IFileStore
    {
        bool Exists(string path);
        string ReadAllText(string path);
        void WriteAllText(string path, string text);
        void Delete(string path);
        /// <summary>Atomic replace where the platform supports it (File.Replace / rename).</summary>
        void Move(string from, string to);
    }

    public sealed class MemoryFileStore : IFileStore
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public bool FailNextWrite;
        public bool Exists(string path) { return Files.ContainsKey(path); }
        public string ReadAllText(string path) { return Files[path]; }
        public void WriteAllText(string path, string text)
        {
            if (FailNextWrite) { FailNextWrite = false; Files[path] = text.Substring(0, text.Length / 2); throw new System.IO.IOException("simulated crash during write"); }
            Files[path] = text;
        }
        public void Delete(string path) { Files.Remove(path); }
        public void Move(string from, string to) { Files[to] = Files[from]; Files.Remove(from); }
    }

    public enum LoadStatus { Ok, NotFound, RecoveredFromBackup, Corrupt, Migrated }

    public sealed class LoadResult
    {
        public LoadStatus Status;
        public JsonValue Data;
        public string Error;
        public int FromVersion;
    }

    /// <summary>
    /// Versioned, checksummed, atomic saves. Write order: payload → `file.tmp` → current becomes `file.bak` → tmp becomes current.
    /// A crash at any point leaves either the old or the new complete file. Load verifies the checksum and falls back to
    /// the backup when the main file is corrupt, then runs migrations up to <see cref="CurrentVersion"/>.
    /// </summary>
    public sealed class SaveSystem
    {
        public const int CurrentVersion = 1;
        public const int MaxSaveBytes = 4 * 1024 * 1024;
        private readonly IFileStore _fs;
        private readonly Dictionary<int, Func<JsonValue, JsonValue>> _migrations = new Dictionary<int, Func<JsonValue, JsonValue>>();

        public SaveSystem(IFileStore fs) { _fs = fs; }

        /// <summary>Register a migration from `fromVersion` to `fromVersion + 1`.</summary>
        public void AddMigration(int fromVersion, Func<JsonValue, JsonValue> migrate) { _migrations[fromVersion] = migrate; }

        public static string Checksum(string s)
        {
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        public void Save(string path, string kind, JsonValue payload)
        {
            string body = JsonWriter.Write(payload, false);
            var envelope = JsonValue.Obj()
                .Set("kind", kind)
                .Set("saveVersion", CurrentVersion)
                .Set("checksum", Checksum(body))
                .Set("payload", payload);
            string text = JsonWriter.Write(envelope, false);
            string tmp = path + ".tmp";
            string bak = path + ".bak";
            _fs.WriteAllText(tmp, text);
            // verify the tmp file round-trips before promoting it
            var verify = TryRead(tmp, kind);
            if (verify.Status != LoadStatus.Ok && verify.Status != LoadStatus.Migrated) throw new InvalidOperationException("save verification failed: " + verify.Error);
            if (_fs.Exists(path))
            {
                if (_fs.Exists(bak)) _fs.Delete(bak);
                _fs.Move(path, bak);
            }
            _fs.Move(tmp, path);
        }

        public LoadResult Load(string path, string kind)
        {
            if (!_fs.Exists(path) && !_fs.Exists(path + ".bak")) return new LoadResult { Status = LoadStatus.NotFound };
            var main = _fs.Exists(path) ? TryRead(path, kind) : new LoadResult { Status = LoadStatus.Corrupt, Error = "missing main file" };
            if (main.Status == LoadStatus.Ok || main.Status == LoadStatus.Migrated) return main;
            if (_fs.Exists(path + ".bak"))
            {
                var bak = TryRead(path + ".bak", kind);
                if (bak.Status == LoadStatus.Ok || bak.Status == LoadStatus.Migrated)
                {
                    bak.Status = LoadStatus.RecoveredFromBackup;
                    bak.Error = main.Error;
                    return bak;
                }
            }
            return main;
        }

        private LoadResult TryRead(string path, string kind)
        {
            string text;
            try { text = _fs.ReadAllText(path); }
            catch (Exception e) { return new LoadResult { Status = LoadStatus.Corrupt, Error = e.Message }; }
            if (text == null || text.Length > MaxSaveBytes) return new LoadResult { Status = LoadStatus.Corrupt, Error = "size" };
            JsonValue env;
            string err;
            if (!JsonParser.TryParse(text, out env, out err)) return new LoadResult { Status = LoadStatus.Corrupt, Error = "json: " + err };
            if (env.Str("kind") != kind) return new LoadResult { Status = LoadStatus.Corrupt, Error = "wrong kind" };
            var payload = env["payload"];
            if (payload.Kind != JsonKind.Object) return new LoadResult { Status = LoadStatus.Corrupt, Error = "no payload" };
            if (Checksum(JsonWriter.Write(payload, false)) != env.Str("checksum")) return new LoadResult { Status = LoadStatus.Corrupt, Error = "checksum mismatch" };
            int version = env.Int("saveVersion", 0);
            if (version > CurrentVersion) return new LoadResult { Status = LoadStatus.Corrupt, Error = "save from newer version " + version };
            var status = LoadStatus.Ok;
            int from = version;
            while (version < CurrentVersion)
            {
                Func<JsonValue, JsonValue> m;
                if (!_migrations.TryGetValue(version, out m)) return new LoadResult { Status = LoadStatus.Corrupt, Error = "no migration from v" + version };
                payload = m(payload);
                version++;
                status = LoadStatus.Migrated;
            }
            return new LoadResult { Status = status, Data = payload, FromVersion = from };
        }
    }
}
