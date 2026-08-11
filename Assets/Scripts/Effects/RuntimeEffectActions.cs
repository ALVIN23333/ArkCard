using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 运行时效果共享原语：效果定义类通过这里操作真实对战对象。
/// </summary>
public static class RuntimeEffectActions
{
    public static void Draw(PlayerController player, int count, System.Action onComplete = null)
    {
        if (player == null || GM.Ins == null || GM.Ins.BM == null)
        {
            onComplete?.Invoke();
            return;
        }

        GM.Ins.BM.DrawCard(player, count, onComplete);
    }

    public static void AddStats(CardController card, int attackValue, int healthValue)
    {
        if (card != null)
        {
            card.AddStats(attackValue, healthValue);
        }
    }

    public static void BuffAllies(CardController source, int attackValue, int healthValue)
    {
        if (source == null || source.player == null || source.player.fieldController == null)
        {
            return;
        }

        foreach (CardController ally in source.player.fieldController.fieldCards)
        {
            if (ally != null)
            {
                ally.AddStats(attackValue, healthValue);
            }
        }
    }

    public static void BuffEnemies(CardController source, int attackValue, int healthValue)
    {
        if (source == null || GM.Ins == null || GM.Ins.BM == null)
        {
            return;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null || player == source.player || player.fieldController == null)
            {
                continue;
            }

            foreach (CardController enemy in player.fieldController.fieldCards)
            {
                if (enemy != null)
                {
                    enemy.AddStats(attackValue, healthValue);
                }
            }
        }
    }

    public static void HealAllies(CardController source, int healValue)
    {
        if (source == null || source.player == null || healValue <= 0)
        {
            return;
        }

        source.player.Heal(healValue);
        if (source.player.fieldController == null)
        {
            return;
        }

        foreach (CardController ally in source.player.fieldController.fieldCards)
        {
            if (ally != null)
            {
                ally.Heal(healValue);
            }
        }
    }

    public static void AddCostAndMaxCost(PlayerController player, int costValue)
    {
        if (player == null || costValue <= 0)
        {
            return;
        }

        player.AddMaxCost(costValue);
        player.AddCost(costValue);
    }

    public static void DiscardRandomCards(PlayerController player, int discardCount)
    {
        if (player == null || player.handController == null || discardCount <= 0)
        {
            return;
        }

        List<CardController> handCards = new(player.handController.handCards);
        for (int i = 0; i < discardCount && handCards.Count > 0; i++)
        {
            int randomIndex = Random.Range(0, handCards.Count);
            CardController card = handCards[randomIndex];
            handCards.RemoveAt(randomIndex);

            if (card != null)
            {
                player.SendCardToGraveyard(card);
            }
        }
    }

    public static void DamageCharacters(CardController source, int damageValue, bool enemyOnly)
    {
        if (damageValue <= 0 || source == null || GM.Ins == null || GM.Ins.BM == null)
        {
            return;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null)
            {
                continue;
            }

            bool isEnemy = player != source.player;
            if (enemyOnly && !isEnemy)
            {
                continue;
            }

            CardController.ApplyPlayerDamage(source, player, damageValue);
        }

        List<CardController> targets = new();
        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null || player.fieldController == null)
            {
                continue;
            }

            foreach (CardController card in player.fieldController.fieldCards)
            {
                if (card == null)
                {
                    continue;
                }

                if (!enemyOnly && card == source)
                {
                    continue;
                }

                if (enemyOnly && card.player == source.player)
                {
                    continue;
                }

                targets.Add(card);
            }
        }

        foreach (CardController target in targets)
        {
            CardController.ApplyDamage(source, target, damageValue);
        }
    }

    public static void DamageTargets(CardController source, List<UnityEngine.Object> targets, int damageValue)
    {
        if (damageValue <= 0 || targets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in targets)
        {
            if (target is CardController targetCard)
            {
                CardController.ApplyDamage(source, targetCard, damageValue);
                continue;
            }

            if (target is PlayerController targetPlayer)
            {
                CardController.ApplyPlayerDamage(source, targetPlayer, damageValue);
            }
        }
    }

    public static void BuffTargets(List<UnityEngine.Object> targets, int attackValue, int healthValue)
    {
        if (targets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in targets)
        {
            if (target is CardController targetCard)
            {
                targetCard.AddStats(attackValue, healthValue);
            }
        }
    }

    public static void HealTargets(List<UnityEngine.Object> targets, int healValue)
    {
        if (healValue <= 0 || targets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in targets)
        {
            if (target is CardController targetCard)
            {
                targetCard.Heal(healValue);
                continue;
            }

            if (target is PlayerController targetPlayer)
            {
                targetPlayer.Heal(healValue);
            }
        }
    }

    public static void SilenceTargets(List<UnityEngine.Object> targets)
    {
        if (targets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in targets)
        {
            if (target is not CardController targetCard)
            {
                continue;
            }

            targetCard.isSilence = true;
            targetCard.isStealth = false;
            targetCard.holyShieldCount = 0;
            if (targetCard.cardDisplay != null)
            {
                targetCard.cardDisplay.UpdateCard();
            }
        }
    }

    public static void DestroyTargets(List<UnityEngine.Object> targets)
    {
        if (targets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in targets)
        {
            if (target is not CardController targetCard
                || targetCard.player == null
                || targetCard.state == CardState.Graveyard)
            {
                continue;
            }

            targetCard.Kill();
        }
    }

    public static void ReturnTargetsToOwnerHand(List<UnityEngine.Object> targets)
    {
        if (targets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in targets)
        {
            if (target is not CardController targetCard
                || targetCard.player == null
                || targetCard.player.handController == null)
            {
                continue;
            }

            if (targetCard.player.graveCards.Contains(targetCard))
            {
                targetCard.player.graveCards.Remove(targetCard);
                targetCard.player.RefreshGraveyardSorting();
            }

            PlayerController owner = targetCard.player;
            if (owner.handController.handCards.Count >= GameConst.handMax)
            {
                // Hand is full: the returning minion is destroyed instead, triggering its deathrattle.
                targetCard.Kill();
                continue;
            }

            if (owner.fieldController != null && owner.fieldController.fieldCards.Contains(targetCard))
            {
                owner.fieldController.RemoveCard(targetCard);
            }

            CardData data = targetCard.cardData;
            targetCard.transform.localScale = Vector3.one;
            targetCard.Init(data, owner);
            if (targetCard.cardDisplay != null)
            {
                targetCard.cardDisplay.ShowBack(!owner.isMainPlayer);
            }

            owner.handController.AddCard(targetCard);
        }
    }

    public static void ReviveAllies(PlayerController owner, List<UnityEngine.Object> targets)
    {
        if (owner == null || owner.fieldController == null || targets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in targets)
        {
            if (owner.fieldController.fieldCards.Count >= GameConst.fieldMax)
            {
                return;
            }

            if (target is not CardController targetCard
                || targetCard.player != owner
                || targetCard.cardData == null
                || targetCard.cardData.cardType != CardType.Minion
                || !owner.graveCards.Contains(targetCard))
            {
                continue;
            }

            owner.graveCards.Remove(targetCard);
            owner.RefreshGraveyardSorting();

            CardData data = targetCard.cardData;
            targetCard.transform.localScale = Vector3.one;
            targetCard.Init(data, owner);
            owner.fieldController.AddCard(targetCard);
        }
    }

    public static void ReviveForController(PlayerController destination, List<UnityEngine.Object> targets)
    {
        if (destination == null || destination.fieldController == null || targets == null)
        {
            return;
        }

        foreach (UnityEngine.Object target in targets)
        {
            if (destination.fieldController.fieldCards.Count >= GameConst.fieldMax)
            {
                return;
            }

            if (target is not CardController card || card.player == null || card.cardData == null
                || card.cardData.cardType != CardType.Minion || !card.player.graveCards.Contains(card))
            {
                continue;
            }

            PlayerController previousOwner = card.player;
            previousOwner.graveCards.Remove(card);
            previousOwner.RefreshGraveyardSorting();
            card.transform.localScale = Vector3.one;
            card.Init(card.cardData, destination);
            destination.fieldController.AddCard(card);
        }
    }

    public static void Summon(PlayerController owner, CardData data, int count)
    {
        if (owner == null || owner.fieldController == null || data == null || data.cardType != CardType.Minion
            || count <= 0 || GM.Ins == null || GM.Ins.BM == null || GM.Ins.BM.cardPrefab == null)
        {
            return;
        }

        int summonCount = Mathf.Min(count, Mathf.Max(0, GameConst.fieldMax - owner.fieldController.fieldCards.Count));
        for (int i = 0; i < summonCount; i++)
        {
            CardController card = Object.Instantiate(GM.Ins.BM.cardPrefab, owner.fieldController.transform)
                .GetComponent<CardController>();
            if (card == null)
            {
                continue;
            }

            card.Init(data, owner);
            owner.fieldController.AddCard(card);
        }
    }
}
