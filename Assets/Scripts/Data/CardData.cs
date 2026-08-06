using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class CardEffectData
{
    public TriggerType triggerType;
    public List<ConditionType> conditionTypes = new();
    public EffectType effectType;
    public int[] effectValues;
    public List<CardEffectData> thenEffects = new();
    public List<CardEffectData> elseEffects = new();
}

[System.Serializable]
public class CardData
{
    public int index;
    public CardType cardType;
    public string name;
    public int cost;
    public int attack;
    public int health;
    public Sprite image;
    [TextArea]
    public string effectDescription;
    public List<CardEffectData> effects = new();
    public List<PassiveType> passiveTypes = new();

    [Header("AI Configuration")]
    public CardAIRole aiRole;
    public AIPlayStyle aiPlayStyle;
    public AITargetPriority aiTargetPriority;
    public int aiBasePriority;
    public int aiComboReserveThreshold;
    public int aiLethalBonus;

    public AudioClip attackAudio;
    public AudioClip enterAudio;
}
