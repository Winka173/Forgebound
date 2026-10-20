namespace Forgebound.Core.Content
{
    public enum DamageType { Physical, Fire, Frost, Lightning, Shadow, Poison, Arcane, Radiant, True }

    public enum StatusType
    {
        None, Burn, Chill, Freeze, Shock, Stun, ArmorBreak, Poison, Curse, Mark, Bleed, Weaken, Slow,
        // Hero-side buffs
        Haste, Fortify, Regen,
        Count
    }

    public enum Stance { Melee, Mid, Ranged }

    public enum StepKind { Melee, Projectile, Nova, Summon, Beam }

    public enum HitShape { Single, Arc, Line, Around }

    public enum TargetPriority { Nearest, LowestHp, HighestThreat, Cluster, Marked, Farthest }

    public enum TriggerType
    {
        None, OnAttack, OnHit, OnCrit, OnKill, OnStatusApplied, OnShieldBreak, OnTimer, OnDamageTaken,
        OnBattleStart, OnLowHp, OnBlock, OnWaveStart, OnProjectileFired, OnSummon
    }

    public enum EffectType
    {
        None,
        Damage,          // direct hit on the trigger target (or selector) using weapon base * permille
        Nova,            // area damage around a point
        Chain,           // bouncing damage to N additional enemies
        ApplyStatus,
        Shield,          // hero shield (flat + permille of weapon base)
        Heal,
        ExtraAttack,     // the owning weapon performs a proc attack
        Projectile,      // spawn an extra projectile from the owner weapon
        Haste,           // hero haste buff (attack speed)
        CooldownReduce,  // reduce current cooldowns of weapons
        Summon,          // spawn a spectral familiar
        Execute,         // kill non-boss target under threshold, bosses take bonus damage
        Pull,
        Knockback,
        Cleanse,         // remove one debuff from hero
        Gold,            // capped per battle, run economy
        SpreadStatus,    // copy the target's statuses to nearby enemies
        Mark,
        Fortify,         // damage reduction buff
        Detonate         // consume a status on target for burst damage
    }

    public enum EffectTarget { TriggerTarget, Self, Nearest, LowestHp, Random, AllEnemies, Cluster, Marked }

    public enum PieceKind { Weapon, Relay, Support }

    public enum PortDir { North = 0, East = 1, South = 2, West = 3 }

    public enum PortKind { In, Out, Both }

    public enum RelayType
    {
        None,
        Conduit,     // pass-through, no charge
        Echo,        // accumulate charge, destination weapon fires an echo attack
        Splitter,    // forwards to every connected output (each output counts separately)
        Amplifier,   // boosts procs passing through it
        Capacitor,   // stores charges from kills, discharges a nova at threshold
        Converter,   // converts the passing proc to a damage type / status
        Delay,       // delays the proc for a set time but increases it
        Prism,       // proc also applies source weapon's core status
        Governor,    // lowers threshold but caps per-battle activations
        Siphon,      // passing procs heal/shield the hero
        Beacon       // procs passing mark the target
    }

    public enum EnemyRank { Minion, Elite, Boss }

    public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

    public enum NodeType { Battle, Elite, Shop, Forge, Rest, Event, Boss, Treasure }

    public enum StatType
    {
        None,
        MaxHp, Armor, AttackSpeed, Damage, CritChance, CritDamage, StatusChance, StatusDuration,
        DamageVsElite, DamageVsBoss, DamageVsStatus, ShieldOnStart, Regen, Thorns, Evasion, Leech,
        MoveSpeed, ProjectileSpeed, Range, ProcDamage, RelayCapacity, RelayThreshold, BoardWidth, BoardHeight,
        GoldGain, EssenceGain, ShopDiscount, RerollDiscount, RestHeal, MaxWeapons, HealingPower,
        DamageTypeBonus, IncomingDamage, EnemyHp, EnemyDamage, EnemySpeed, ShopOffers, StartGold, SummonPower,
        MeleeDamage, RangedDamage, AdjacencyPower, ModifierSlots, RuneSlots, FirstStrike, LowHpDamage,
        ResistAll, ExecuteThreshold, BurnDamage, PoisonDamage, ChainCount, ProjectileCount, RewardChoices
    }

    public enum ItemCategory { Frame, Core, Modifier, Rune, Piece, Weapon }
}
