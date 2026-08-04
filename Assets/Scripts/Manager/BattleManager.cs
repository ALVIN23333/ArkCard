using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BattleManager : MonoBehaviour
{
    public GameObject cardPrefab;
    public List<PlayerController> players = new List<PlayerController>();

    private PlayerController curplayer;
    private int currentPlayerIndex;
    private bool isGameOver;
    private bool isTurnTransitioning;
    private readonly Dictionary<PlayerController, Transform> playerIconTargets = new();

    public PlayerController CurrentPlayer => curplayer;
    public bool IsGameOver => isGameOver;
    public bool IsTurnTransitioning => isTurnTransitioning;
    public TargetManager TM;
    public EffectManager EM;

    public void Init()
    {
        EM.Init();
        TM.Init();
        currentPlayerIndex = 0;
        isGameOver = false;
        isTurnTransitioning = true;
        playerIconTargets.Clear();

        foreach (PlayerController player in players)
        {
            player.Init();
            CachePlayerIconTarget(player);
            if (player.isMainPlayer)
            {
                curplayer = player;
                currentPlayerIndex = players.IndexOf(player);
            }
        }

        if (curplayer == null && players.Count > 0)
        {
            curplayer = players[0];
            currentPlayerIndex = 0;
        }

        AnimeManager.Delay(0.1f, () =>
        {
            foreach (PlayerController player in players)
            {
                DrawCard(player, 5);
            }
        });

        AnimeManager.Delay(1f, StartCurrentTurn);
    }


    private void CachePlayerIconTarget(PlayerController player)
    {
        if (player == null)
        {
            return;
        }

        Transform playerIcon = FindChildByName(player.transform, "PlayerIcon");
        if (playerIcon != null)
        {
            playerIconTargets[player] = playerIcon;
        }
    }

    public bool TryGetPlayerIconTarget(PlayerController player, out Transform iconTarget)
    {
        iconTarget = null;
        if (player == null)
        {
            return false;
        }

        if (playerIconTargets.TryGetValue(player, out Transform cachedTarget) && cachedTarget != null)
        {
            iconTarget = cachedTarget;
            return true;
        }

        CachePlayerIconTarget(player);
        if (playerIconTargets.TryGetValue(player, out cachedTarget) && cachedTarget != null)
        {
            iconTarget = cachedTarget;
            return true;
        }

        return false;
    }

    private Vector3 GetPlayerTargetPosition(PlayerController player)
    {
        if (TryGetPlayerIconTarget(player, out Transform playerIcon))
        {
            return playerIcon.position;
        }

        return player != null ? player.transform.position : Vector3.zero;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
        {
            return null;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
            {
                return child;
            }
        }

        return null;
    }
    public void DrawCard(PlayerController player, int count)
    {
        if (player == null || count <= 0)
        {
            return;
        }

        if (count > 1)
        {
            StartCoroutine(DrawCardsCoroutine(player, count));
            return;
        }

        DrawSingleCard(player);
    }

    public void EndCurrentTurn()
    {
        if (players.Count == 0
            || curplayer == null
            || !curplayer.isInTurn
            || isGameOver
            || isTurnTransitioning
            || IsEffectProcessing())
        {
            return;
        }

        StartCoroutine(EndCurrentTurnCoroutine());
    }

    public void EndMainPlayerTurn()
    {
        if (!IsMainPlayerInTurn())
        {
            return;
        }

        EndCurrentTurn();
    }

    public bool IsMainPlayerInTurn()
    {
        return curplayer != null && curplayer.isMainPlayer && curplayer.isInTurn && !isGameOver && !isTurnTransitioning;
    }

    public bool CanResolveMinionAttack(CardController attacker, CardController target)
    {
        return CanAttackTarget(attacker, target);
    }

    public bool CanResolvePlayerAttack(CardController attacker, PlayerController targetPlayer)
    {
        return CanAttackTarget(attacker, targetPlayer);
    }

    public void ResolveMinionAttack(CardController attacker, CardController target)
    {
        if (!CanResolveMinionAttack(attacker, target))
        {
            return;
        }

        int attackerDamage = attacker.atk;
        int targetDamage = target.atk;
        attacker.SetCanAttack(false);

        AnimeManager.PlayAttackAnimation(attacker, target.transform.position, () =>
        {
            if (target != null)
            {
                target.Damage(attackerDamage);
            }

            if (attacker != null)
            {
                attacker.Damage(targetDamage);
            }
        });
    }

    public void ResolvePlayerAttack(CardController attacker, PlayerController targetPlayer)
    {
        if (!CanResolvePlayerAttack(attacker, targetPlayer))
        {
            return;
        }

        attacker.SetCanAttack(false);
        AnimeManager.PlayAttackAnimation(attacker, GetPlayerTargetPosition(targetPlayer), () =>
        {
            if (targetPlayer != null)
            {
                targetPlayer.Damage(attacker.atk);
                CheckGameOver();
            }
        });
    }

    public bool CanUseMinion(CardController card, FieldController targetField)
    {
        if (!CanAct())
        {
            return false;
        }

        if (!CanUseCard(card) || card.cardData.cardType != CardType.Minion || targetField == null || targetField.player != card.player)
        {
            return false;
        }

        if (targetField.fieldCards.Count >= GameConst.fieldMax)
        {
            return false;
        }

        return HasRequiredConditions(card, TriggerType.Enter, false);
    }

    public bool CanUseSpell(CardController card)
    {
        if (!CanAct())
        {
            return false;
        }

        if (!CanUseCard(card)
            || card.cardData.cardType != CardType.SPELL
            || card.cardData.effects == null
            || card.cardData.effects.Count == 0)
        {
            return false;
        }

        foreach (CardEffectData effect in card.cardData.effects)
        {
            if (effect == null)
            {
                continue;
            }

            if (!EM.HasRequiredCondition(card, effect))
            {
                return false;
            }
        }

        return true;
    }

    public bool CanUseFieldCast(CardController card)
    {
        if (!CanAct())
        {
            return false;
        }

        if (!CanUseFieldCard(card)
            || card.cardData.cardType != CardType.Minion
            || card.isSilence
            || card.castUsed
            || card.cardData.effects == null
            || card.cardData.effects.Count == 0)
        {
            return false;
        }

        bool hasCastEffect = false;
        foreach (CardEffectData effect in card.cardData.effects)
        {
            if (effect == null || effect.triggerType != TriggerType.Cast)
            {
                continue;
            }

            hasCastEffect = true;
            if (!EM.HasRequiredCondition(card, effect))
            {
                return false;
            }
        }

        if (!hasCastEffect)
        {
            return false;
        }

        return true;
    }

    public bool TryUseFieldCast(CardController card, bool autoSelect = false)
    {
        if (!CanUseFieldCast(card))
        {
            return false;
        }

        ResolveFieldCast(card, null);
        return true;
    }

    public void ResetFieldAttackState(PlayerController player)
    {
        if (player == null || player.fieldController == null)
        {
            return;
        }

        foreach (CardController card in player.fieldController.fieldCards)
        {
            if (card == null || card.state != CardState.Field)
            {
                continue;
            }

            card.SetCanAttack(true);
        }
    }


    private bool HasRequiredConditions(CardController card, TriggerType triggerType, bool requireMatchingEffect)
    {
        if (card == null || card.cardData == null || card.cardData.effects == null)
        {
            return !requireMatchingEffect;
        }

        bool hasMatchingEffect = false;
        foreach (CardEffectData effect in card.cardData.effects)
        {
            if (effect == null || effect.triggerType != triggerType)
            {
                continue;
            }

            hasMatchingEffect = true;
            if (!EM.HasRequiredCondition(card, effect))
            {
                return false;
            }
        }

        return !requireMatchingEffect || hasMatchingEffect;
    }
    private IEnumerator DrawCardsCoroutine(PlayerController player, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new WaitForSeconds(0.1f);
            DrawSingleCard(player);
        }
    }

    private void DrawSingleCard(PlayerController player)
    {
        if (player.deckCards.Count <= 0)
        {
            return;
        }

        CardController card = player.deckCards[0];
        player.deckCards.RemoveAt(0);
        player.handController.AddCard(card);
    }

    private void StartCurrentTurn()
    {
        if (curplayer == null || isGameOver)
        {
            isTurnTransitioning = false;
            return;
        }

        StartCoroutine(StartCurrentTurnCoroutine());
    }

    private IEnumerator EndCurrentTurnCoroutine()
    {
        isTurnTransitioning = true;
        PlayerController endingPlayer = curplayer;
        endingPlayer.EndTurn();

        yield return new WaitForSeconds(0.2f);

        if (CheckGameOver())
        {
            yield break;
        }

        EM.TriggerFieldEffects(endingPlayer, TriggerType.End);
        while (IsEffectProcessing() && !isGameOver)
        {
            yield return null;
        }

        if (CheckGameOver())
        {
            yield break;
        }

        currentPlayerIndex = (currentPlayerIndex + 1) % players.Count;
        curplayer = players[currentPlayerIndex];
        StartCurrentTurn();
    }

    private IEnumerator StartCurrentTurnCoroutine()
    {
        isTurnTransitioning = true;
        ResetFieldAttackState(curplayer);
        curplayer.StartTurn();

        bool turnPanelComplete = false;
        if (GM.Ins != null && GM.Ins.UM != null)
        {
            GM.Ins.UM.ShowTurnPanel(curplayer, () => turnPanelComplete = true);
        }
        else
        {
            AnimeManager.Delay(0.2f, () => turnPanelComplete = true);
        }

        while (!turnPanelComplete && !isGameOver)
        {
            yield return null;
        }

        if (CheckGameOver())
        {
            yield break;
        }

        EM.TriggerFieldEffects(curplayer, TriggerType.Start);
        while (IsEffectProcessing() && !isGameOver)
        {
            yield return null;
        }

        if (CheckGameOver())
        {
            yield break;
        }

        DrawCard(curplayer, GameConst.turnDraw);
        isTurnTransitioning = false;
    }

    public bool CheckGameOver()
    {
        if (isGameOver)
        {
            return true;
        }

        if (IsEffectProcessing())
        {
            return false;
        }

        bool mainDead = false;
        bool enemyDead = false;
        foreach (PlayerController player in players)
        {
            if (player == null || player.health > 0)
            {
                continue;
            }

            if (player.isMainPlayer)
            {
                mainDead = true;
            }
            else
            {
                enemyDead = true;
            }
        }

        if (!mainDead && !enemyDead)
        {
            return false;
        }

        if (mainDead && enemyDead)
        {
            EndGame(null, true);
            return true;
        }

        PlayerController winner = FindAlivePlayer(!mainDead);
        EndGame(winner, winner == null);
        return true;
    }

    private void EndGame(PlayerController winner, bool isDraw)
    {
        isGameOver = true;
        isTurnTransitioning = false;

        foreach (PlayerController player in players)
        {
            if (player != null && player.isInTurn)
            {
                player.EndTurn();
            }
        }

        if (GM.Ins != null && GM.Ins.UM != null)
        {
            GM.Ins.UM.ShowWinPanel(winner, isDraw);
        }
    }

    private PlayerController FindAlivePlayer(bool isMainPlayer)
    {
        foreach (PlayerController player in players)
        {
            if (player != null && player.isMainPlayer == isMainPlayer && player.health > 0)
            {
                return player;
            }
        }

        return null;
    }

    private void ResolveFieldCast(CardController card, List<UnityEngine.Object> selectedTargets)
    {
        if (!CanUseFieldCast(card))
        {
            return;
        }

        TM?.RegisterFieldCastRollback(card);
        card.SetCastUsed(true);
        EM.TriggerCardEffect(card, TriggerType.Cast, selectedTargets, () =>
        {
            TM?.ClearFieldCastRollback(card);
        });
    }
    private static List<UnityEngine.Object> SelectRandomTargets(TargetSelectionRequest request)
    {
        List<UnityEngine.Object> results = new();
        if (request == null || request.candidates == null || request.candidates.Count == 0)
        {
            return results;
        }

        List<UnityEngine.Object> candidates = new(request.candidates);
        int targetCount = Mathf.Min(Mathf.Max(1, request.requiredCount), candidates.Count);
        for (int i = 0; i < targetCount; i++)
        {
            int randomIndex = UnityEngine.Random.Range(0, candidates.Count);
            results.Add(candidates[randomIndex]);
            candidates.RemoveAt(randomIndex);
        }

        return results;
    }

    private bool CanAttack(CardController attacker)
    {
        return CanAct()
            && attacker != null
            && attacker.state == CardState.Field
            && attacker.canAttack
            && attacker.player != null
            && attacker.player.isInTurn;
    }

    private bool CanAttackTarget(CardController attacker, CardController target)
    {
        if (!CanAttack(attacker)
            || target == null
            || target.state != CardState.Field
            || target.player == null
            || target.player == attacker.player)
        {
            return false;
        }

        return !HasGuardMinion(target.player) || IsGuardMinion(target);
    }

    private bool CanAttackTarget(CardController attacker, PlayerController targetPlayer)
    {
        if (!CanAttack(attacker) || targetPlayer == null || targetPlayer == attacker.player)
        {
            return false;
        }

        return !HasGuardMinion(targetPlayer);
    }

    private static bool HasGuardMinion(PlayerController player)
    {
        if (player == null || player.fieldController == null)
        {
            return false;
        }

        foreach (CardController card in player.fieldController.fieldCards)
        {
            if (IsGuardMinion(card))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGuardMinion(CardController card)
    {
        return card != null
            && card.state == CardState.Field
            && !card.isSilence
            && card.cardData != null
            && card.cardData.passiveType == PassiveType.Guard;
    }

    private bool CanUseCard(CardController card)
    {
        return CanAct()
            && !IsEffectProcessing()
            && card != null
            && card.player != null
            && card.cardData != null
            && card.state == CardState.Hand
            && card.player.isInTurn
            && card.player.cost >= card.cost;
    }

    private bool CanUseFieldCard(CardController card)
    {
        return CanAct()
            && !IsEffectProcessing()
            && card != null
            && card.player != null
            && card.cardData != null
            && card.state == CardState.Field
            && card.player.isInTurn;
    }

    private bool CanAct()
    {
        return !isGameOver && !isTurnTransitioning;
    }

    private bool IsEffectProcessing()
    {
        return EM != null && EM.IsProcessingEffects;
    }
}
