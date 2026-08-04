using System.Collections.Generic;
using UnityEngine;

public class AIController : PlayerController
{
    [SerializeField]
    private float actionInterval = 1.5f;

    private float nextActionTime;

    private void Update()
    {
        if (!ShouldProcessTurnActions() || Time.time < nextActionTime)
        {
            return;
        }

        if (TryPlayHandCard() || TryUseFieldEffect() || TryAttackWithFieldCard())
        {
            ScheduleNextAction();
            return;
        }

        if (GM.Ins != null && GM.Ins.BM != null && GM.Ins.BM.CurrentPlayer == this)
        {
            GM.Ins.BM.EndCurrentTurn();
            ScheduleNextAction();
        }
    }

    public override void StartTurn()
    {
        base.StartTurn();
        ScheduleNextAction();
    }

    public override void EndTurn()
    {
        base.EndTurn();
        nextActionTime = 0f;
    }

    private bool ShouldProcessTurnActions()
    {
        return !isMainPlayer
            && isInTurn
            && GM.Ins != null
            && GM.Ins.BM != null
            && GM.Ins.BM.CurrentPlayer == this
            && !GM.Ins.BM.IsGameOver
            && !GM.Ins.BM.IsTurnTransitioning
            && (GM.Ins.BM.TM == null || !GM.Ins.BM.TM.HasActiveSelection);
    }

    private void ScheduleNextAction()
    {
        nextActionTime = Time.time + actionInterval;
    }

    private bool TryPlayHandCard()
    {
        if (handController == null || GM.Ins == null || GM.Ins.BM == null)
        {
            return false;
        }

        List<CardController> handCards = new(handController.handCards);
        foreach (CardController card in handCards)
        {
            if (card == null || card.cardData == null || card.player != this || card.state != CardState.Hand)
            {
                continue;
            }

            if (card.cardData.cardType == CardType.SPELL)
            {
                if (!GM.Ins.BM.CanUseSpell(card) || !SpendCost(card.cost))
                {
                    continue;
                }

                TargetManager targetManager = GM.Ins.BM.TM;
                void TriggerSpellAfterHangingReady()
                {
                    GM.Ins.BM.EM.TriggerSpellEffect(card, null, () =>
                    {
                        if (targetManager != null)
                        {
                            targetManager.ReleaseHangingState(card, () => SendCardToGraveyard(card));
                        }
                        else
                        {
                            SendCardToGraveyard(card);
                        }
                    });
                }

                if (targetManager == null || !targetManager.EnterHangingState(card, false, TriggerSpellAfterHangingReady))
                {
                    TriggerSpellAfterHangingReady();
                }
                return true;
            }

            if (card.cardData.cardType == CardType.Minion)
            {
                if (!GM.Ins.BM.CanUseMinion(card, fieldController) || !SpendCost(card.cost))
                {
                    continue;
                }

                card.transform.localScale = Vector3.one;
                handController.RemoveCard(card);
                fieldController.AddCard(card);
                AnimeManager.Delay(AnimeManager.FieldRefreshDuration, () =>
                {
                    if (card != null && card.state == CardState.Field)
                    {
                        GM.Ins.BM.EM.TriggerCardEffect(card, TriggerType.Enter);
                    }
                });
                return true;
            }
        }

        return false;
    }

    private bool TryUseFieldEffect()
    {
        if (fieldController == null || GM.Ins == null || GM.Ins.BM == null || GM.Ins.BM.EM == null)
        {
            return false;
        }

        List<CardController> fieldCards = new(fieldController.fieldCards);
        foreach (CardController card in fieldCards)
        {
            if (!CanUseFieldEffect(card))
            {
                continue;
            }

            return GM.Ins.BM.TryUseFieldCast(card, true);
        }

        return false;
    }

    private bool TryAttackWithFieldCard()
    {
        if (fieldController == null || GM.Ins == null || GM.Ins.BM == null)
        {
            return false;
        }

        List<CardController> attackers = new();
        foreach (CardController card in fieldController.fieldCards)
        {
            if (card != null && card.state == CardState.Field && card.canAttack && card.player == this)
            {
                attackers.Add(card);
            }
        }

        if (attackers.Count == 0)
        {
            return false;
        }

        CardController attacker = attackers[Random.Range(0, attackers.Count)];
        List<CardController> enemyCards = new();
        List<PlayerController> enemyPlayers = new();

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null || player == this)
            {
                continue;
            }

            enemyPlayers.Add(player);
            if (player.fieldController == null)
            {
                continue;
            }

            foreach (CardController fieldCard in player.fieldController.fieldCards)
            {
                if (fieldCard != null && fieldCard.state == CardState.Field)
                {
                    enemyCards.Add(fieldCard);
                }
            }
        }

        List<CardController> validEnemyCards = new();
        foreach (CardController enemyCard in enemyCards)
        {
            if (GM.Ins.BM.CanResolveMinionAttack(attacker, enemyCard))
            {
                validEnemyCards.Add(enemyCard);
            }
        }

        List<PlayerController> validEnemyPlayers = new();
        foreach (PlayerController enemyPlayer in enemyPlayers)
        {
            if (GM.Ins.BM.CanResolvePlayerAttack(attacker, enemyPlayer))
            {
                validEnemyPlayers.Add(enemyPlayer);
            }
        }

        bool attackMinion = validEnemyCards.Count > 0 && (validEnemyPlayers.Count == 0 || Random.Range(0, 2) == 0);
        if (attackMinion)
        {
            CardController target = validEnemyCards[Random.Range(0, validEnemyCards.Count)];
            GM.Ins.BM.ResolveMinionAttack(attacker, target);
            return true;
        }

        if (validEnemyPlayers.Count == 0)
        {
            return false;
        }

        PlayerController targetPlayer = validEnemyPlayers[Random.Range(0, validEnemyPlayers.Count)];
        GM.Ins.BM.ResolvePlayerAttack(attacker, targetPlayer);
        return true;
    }

    private bool CanUseFieldEffect(CardController card)
    {
        return GM.Ins != null
            && GM.Ins.BM != null
            && card != null
            && card.player == this
            && GM.Ins.BM.CanUseFieldCast(card);
    }

}
