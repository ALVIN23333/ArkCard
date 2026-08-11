using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CardController : MonoBehaviour
{
    public PlayerController player;
    public CardState state;
    public bool canAttack;
    public bool canAttackPlayer;
    public int attackCount;
    public bool isStealth;
    public int holyShieldCount;
    public bool castUsed;
    public bool isSilence;
    public bool isDying;
    public int cost;
    // 真实攻击力（可为负值），UI 显示时由 CardDisplay 钳制到 0
    public int atk;
    public int health;
    public int maxHealth;

    public CardData cardData;
    public CardDisplay cardDisplay;


    public void Init(CardData data,PlayerController playerController)
    {
        player = playerController;
        cardData = data;
        cardDisplay = GetComponent<CardDisplay>();
        if (cardDisplay != null)
        {
            cardDisplay.SetCard(this);
        }

        ResetCard();
    }

    public void ResetCard()
    {
        if (cardData == null)
        {
            return;
        }

        cost = cardData.cost;
        atk = cardData.attack;
        health = cardData.health;
        maxHealth = cardData.health;
        isSilence = false;
        isDying = false;
        castUsed = false;
        canAttack = HasAnyPassive(PassiveType.Rush, PassiveType.Charge);
        canAttackPlayer = HasPassive(PassiveType.Charge);
        attackCount = HasPassive(PassiveType.Windfury) ? 2 : 1;
        isStealth = HasPassive(PassiveType.Stealth);
        holyShieldCount = HasPassive(PassiveType.HolyShield) ? 1 : 0;

        if (cardDisplay != null)
        {
            cardDisplay.UpdateCard();
        }
    }

    public bool HasPassive(PassiveType passive)
    {
        if (isSilence || cardData == null || cardData.passiveTypes == null)
        {
            return false;
        }

        return cardData.passiveTypes.Contains(passive);
    }

    public bool HasAnyPassive(params PassiveType[] passives)
    {
        foreach (PassiveType passive in passives)
        {
            if (HasPassive(passive))
            {
                return true;
            }
        }

        return false;
    }

    public void UseAttack()
    {
        attackCount = Mathf.Max(0, attackCount - 1);
        if (attackCount <= 0)
        {
            canAttack = false;
        }

        if (isStealth)
        {
            isStealth = false;
        }

        if (cardDisplay != null)
        {
            cardDisplay.UpdateCard();
        }
    }

    public void RefreshTurnAttackState()
    {
        canAttack = true;
        canAttackPlayer = true;
        attackCount = HasPassive(PassiveType.Windfury) ? 2 : 1;

        if (cardDisplay != null)
        {
            cardDisplay.UpdateCard();
        }
    }

    public bool Damage(int value)
    {
        if (value <= 0 || state == CardState.Graveyard || isDying)
        {
            return false;
        }

        if (holyShieldCount > 0)
        {
            holyShieldCount--;
            if (cardDisplay != null)
            {
                cardDisplay.UpdateCard();
            }

            return false;
        }

        health -= value;
        if (health < 0)
        {
            health = 0;
        }

        if (cardDisplay != null)
        {
            cardDisplay.UpdateCard();
        }

        // Hurt fires even when this damage is lethal; death resolves after the trigger is queued.
        GM.Ins.BM.EM.TriggerCardEffect(this, TriggerType.Hurt);
        if (health <= 0)
        {
            Die();
        }

        return true;
    }

    public static void ApplyDamage(CardController source, CardController target, int damage)
    {
        if (source == null || target == null || damage <= 0 || target.state == CardState.Graveyard || target.isDying)
        {
            return;
        }

        bool damageDealt = target.Damage(damage);
        if (!damageDealt || source.player == null)
        {
            return;
        }

        if (source.HasPassive(PassiveType.Lifesteal))
        {
            source.player.Heal(damage);
        }

        if (source.HasPassive(PassiveType.Poisonous)
            && target.cardData != null
            && target.cardData.cardType == CardType.Minion
            && target.state == CardState.Field
            && !target.isDying
            && target.health > 0)
        {
            target.Kill();
        }
    }

    public static void ApplyPlayerDamage(CardController source, PlayerController target, int damage)
    {
        if (source == null || target == null || damage <= 0)
        {
            return;
        }

        target.Damage(damage);
        if (source.HasPassive(PassiveType.Lifesteal))
        {
            source.player?.Heal(damage);
        }
    }
    public void Heal(int value)
    {
        if (value <= 0 || state == CardState.Graveyard || isDying)
        {
            return;
        }

        health += value;
        if (health > maxHealth)
        {
            health = maxHealth;
        }
        cardDisplay.UpdateCard();
    }

    public void SetCanAttack(bool value)
    {
        canAttack = value;
        if (cardDisplay != null)
        {
            cardDisplay.UpdateCard();
        }
    }

    public void SetCastUsed(bool value)
    {
        castUsed = value;
        if (cardDisplay != null)
        {
            cardDisplay.UpdateCard();
        }
    }

    public void SetSelected(bool value)
    {
        if (cardDisplay != null)
        {
            cardDisplay.SetSelected(value);
        }
    }

    public void AddStats(int attackValue, int healthValue)
    {
        atk += attackValue;
        maxHealth += healthValue;
        health += healthValue;
        if (health > maxHealth)
        {
            health = maxHealth;
        }

        if (health <= 0)
        {
            health = 0;
            Die();
        }

        cardDisplay.UpdateCard();
    }

    public void Kill(Action onComplete = null)
    {
        if (state == CardState.Graveyard || isDying)
        {
            onComplete?.Invoke();
            return;
        }

        health = 0;
        if (cardDisplay != null)
        {
            cardDisplay.UpdateCard();
        }

        Die(onComplete);
    }

    private void Die(Action onComplete = null)
    {
        if (state == CardState.Graveyard || player == null || isDying)
        {
            onComplete?.Invoke();
            return;
        }

        isDying = true;
        PlayerController owner = player;
        GM.Ins.BM.EM.TriggerDeathEffect(this, () =>
        {
            if (this != null && owner != null)
            {
                owner.SendCardToGraveyard(this);
            }

            onComplete?.Invoke();
        });
    }
}
