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
    None,
    Draw,
    BuffSelf,
    BuffAlliesAll,
    BuffAllEnemies,
    healAlliesAll,
    DamageAll,
    DamageAllEnemy,
    AddCostMax,
    AddCost,
    AddBothCost, // Increase one player's cost and maxCost.
    DisCard,

    // Target-selection effects. Selection count is effectValues[1]; BuffEnemy/BuffAlly use effectValues[2].
    DealDamageToEnemy,
    BuffEnemy,
    SlienceEnemy,
    DestoryEnemy,
    BuffAlly,
    HealAlly,
    OtherBackHand,
    AllyBackHand,
    EnemyBackHand,
    ReviveAlly,// Revive an allied minion from the graveyard to the field.
}

public enum PassiveType
{
    None,
    Rush,
    Guard,
    Swingle,
}

public enum CardType
{
    None,
    Minion,
    SPELL,
}

public enum ConditionType
{
    None,
    ThreeMoreHand,
    HasOther, 
    HasEnemy,
    HasAlly,
    HasDiedMumber,
    HasEmptyField,//场地上的卡牌小于最大场地卡牌
}

public enum CardState
{
    Deck,
    Hand,
    Field,
    Graveyard,
    Hanging,
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