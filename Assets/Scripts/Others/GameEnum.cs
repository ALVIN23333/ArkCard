using UnityEngine;

public enum TriggerType
{
    None,
    Start,
    End,
    Enter,
    Died,
    Hurt,
    Cast,
}

public enum EffectType
{
    None=0,
    Draw=1,
    BuffSelf=2,
    BuffAlliesAll=3,
    BuffAllEnemies=4,
    healAlliesAll=5,
    DamageAll=6,
    DamageAllEnemy=7,
    AddCostMax=8,
    AddCost=9,
    AddBothCost=10,
    DisCard=11,

    // Target-selection effects. Selection count is effectValues[1]; BuffEnemy/BuffAlly use effectValues[2].
    DealDamageToEnemy=100,
    BuffEnemy=101,
    SlienceEnemy=102,
    DestoryEnemy=103,
    BuffAlly=104,
    HealAlly=105,
    OtherBackHand=106,
    AllyBackHand=107,
    EnemyBackHand=108,
    ReviveAlly=109,

    // Unified configurable effects. Existing serialized values 0-109 must never change.
    Damage=110,
    Heal=111,
    Destroy=112,
    Buff=113,
    BackHand=114,
    Discard=115,
    Revive=116,
    SummonMinion=117,
    SummonRandomCostMinion=118,
    DrawCards=119,
    Cost=120,
    Silence=121,
}

public enum EffectTargetSide
{
    Friendly=0,
    Enemy=1,
    Both=2,
}

public enum EffectTargetMode
{
    Self=0,
    All=1,
    Selected=2,
    Random=3,
}

public enum EffectCharacterScope
{
    Minions=0,
    Heroes=1,
    Characters=2,
}

public enum PassiveType
{
    None,
    Charge,//冲锋：进场时就可以攻击所有敌方随从和英雄
    Rush,//突袭：进场时就可以攻击所有敌方随从
    Guard,//嘲讽
    Stealth,//潜行：具有潜行时无法被敌方选做目标和攻击，自己攻击后失去潜行，在cardcontroller中配置一个bool值
    Swingle,//横扫：同时攻击目标和相邻的随从，在fieldcontroller中实现获取邻居卡牌的方法
    Windfury,//风怒：一回合可以攻击2次，在cardcontroller中配置新的可攻击次数变量，回合开始时刷新
    HolyShield,//圣盾：抵消受到的第一次伤害，在cardcontroller中配置一个圣盾计数器
    Lifesteal,//吸血：造成伤害时为己方英雄回复等量生命值，对于效果造成的伤害同样适用
    Poisonous,//剧毒：对随从造成>0的伤害时直接消灭随从，对于效果造成的伤害同样适用
    MagicImmunity,//魔免：无法被法术卡选择，可以被随从的效果选择
}

public enum CardType
{
    None,
    Minion,
    SPELL,
}

public enum ConditionType
{
    None=0,
    ThreeMoreHand=1,//手牌大于三张
    HasOther=2, //场上有其他随从
    HasEnemy=3,//场上有敌方随从
    HasAlly=4,//场上有友方随从
    HasDiedAlly=5,//友方墓地有随从
    HasDiedEnemy=6,//敌方墓地有随从
    HasEmptyField=7,//场地上的卡牌小于最大场地卡牌
    HasNonMagicalImmunityAlly=8,//具有非魔免友方随从
    HasNonMagicalImmunityEnemy=9,//具有非魔免敌方随从
    HasNonMagicalImmunityOther=10,//具有非魔免其他随从
}

public enum CardState
{
    Deck,
    Hand,
    Field,
    Graveyard,
    Hanging,
}

public enum CardAIRole
{
    None,
    Tempo,
    Removal,
    Finisher,
    Support,
    Value,
}

public enum AIPlayStyle
{
    Default,
    Aggressive,
    Defensive,
    ComboReserve,
}

public enum AITargetPriority
{
    Default,
    EnemyHero,
    HighAttackEnemy,
    LowHealthEnemy,
    GuardFirst,
    WeakAlly,
    StrongAlly,
}

public enum AudioType
{
    None,
    DrawCard,
    Shuffle,
    Destroy,
    Damage,
    Heal,
    NextTurn,
    Effect,
    Summon,
}
