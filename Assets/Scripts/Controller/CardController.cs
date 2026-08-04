using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CardController : MonoBehaviour
{
    public PlayerController player;
    public CardState state;
    public bool canAttack;
    public bool castUsed;
    public bool isSilence;
    public bool isDying;
    public int cost;
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
        canAttack = cardData.passiveType == PassiveType.Rush;

        if (cardDisplay != null)
        {
            cardDisplay.UpdateCard();
        }
    }
    public void Damage(int value)
    {
        if (value <= 0 || state == CardState.Graveyard || isDying)
        {
            return;
        }

        health -= value;
        if (health < 0)
        {
            health = 0;
        }
        cardDisplay.UpdateCard();

        if (health > 0)
        {
            GM.Ins.BM.EM.TriggerCardEffect(this, TriggerType.Hurt);
            return;
        }

        Die();
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
        if (health < 0)
        {
            health = 0;
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
