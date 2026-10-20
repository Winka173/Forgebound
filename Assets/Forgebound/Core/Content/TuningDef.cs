using System;
using System.Collections.Generic;

namespace Forgebound.Core.Content
{
    /// <summary>
    /// Global balance knobs (Resources/Content/tuning.json). Changed by designers / balance passes instead of editing
    /// dozens of enemies; every knob is applied in exactly one place in the simulation and documented in docs/BALANCE.md.
    /// </summary>
    public sealed class TuningDef
    {
        public int[] EnemyHpByChapter = { 1000, 1000, 1000, 1000, 1000 };
        public int[] EnemyDamageByChapter = { 1000, 1000, 1000, 1000, 1000 };
        public int BossHpPermille = 1000;
        public int BossDamagePermille = 1000;
        public int EliteHpPermille = 1000;
        public int EliteDamagePermille = 1000;
        public int HeroDamagePermille = 1000;
        public int ProcGlobalPermille = 1000;
        public int MultiProjectileFalloffPermille = 1000;   // applied on top of split damage when more than 3 projectiles
        public int CritDamageCapPermille = 4000;
        public int LeechCapPerSecondPermille = 40;           // of max HP
        public int PostBattleHealPermille = 0;
        public int ShieldCapPermille = 1000;                 // hero shield cap (of max HP)
        public int ShieldDecayPermille = 0;                  // hero shield lost per second (of current shield)               // field repair after a won battle (of max HP)

        private static int[] Arr(JsonValue v, int[] def)
        {
            if (v.Kind != JsonKind.Array || v.Count == 0) return def;
            var a = new int[Math.Max(5, v.Count)];
            for (int i = 0; i < a.Length; i++) a[i] = (int)Math.Round((i < v.Count ? v[i].AsDouble(1) : v[v.Count - 1].AsDouble(1)) * 1000);
            return a;
        }

        public static TuningDef Parse(JsonValue v, List<string> errors)
        {
            var t = new TuningDef();
            t.EnemyHpByChapter = Arr(v["enemyHpByChapter"], t.EnemyHpByChapter);
            t.EnemyDamageByChapter = Arr(v["enemyDamageByChapter"], t.EnemyDamageByChapter);
            t.BossHpPermille = v.Permille("bossHp", 1000);
            t.BossDamagePermille = v.Permille("bossDamage", 1000);
            t.EliteHpPermille = v.Permille("eliteHp", 1000);
            t.EliteDamagePermille = v.Permille("eliteDamage", 1000);
            t.HeroDamagePermille = v.Permille("heroDamage", 1000);
            t.ProcGlobalPermille = v.Permille("procGlobal", 1000);
            t.MultiProjectileFalloffPermille = v.Permille("multiProjectileFalloff", 1000);
            t.CritDamageCapPermille = v.Permille("critDamageCap", 4000);
            t.LeechCapPerSecondPermille = v.Permille("leechCapPerSecond", 40);
            t.PostBattleHealPermille = v.Permille("postBattleHeal", 0);
            t.ShieldCapPermille = v.Permille("shieldCap", 1000);
            t.ShieldDecayPermille = v.Permille("shieldDecayPerSecond", 0);
            foreach (var arr in new[] { t.EnemyHpByChapter, t.EnemyDamageByChapter })
                foreach (var x in arr) if (x < 100 || x > 10000) errors.Add("tuning: chapter multiplier out of range 0.1..10");
            return t;
        }

        public int Chapter(int[] arr, int chapter) { return arr[Math.Max(0, Math.Min(arr.Length - 1, chapter - 1))]; }
    }
}
