using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// 运行时冒烟：验证法术效果经 EffectManager 走注册表分发，抽牌效果真正生效。
/// </summary>
public class EffectRegistryPlayModeSmokeTests
{
    private const float InitTimeoutSeconds = 30f;

    [UnityTest]
    public IEnumerator SpellDraw_DispatchesThroughRegistry()
    {
        SceneManager.LoadScene("BattleScene");
        yield return null;

        float initElapsed = 0f;
        while (initElapsed < InitTimeoutSeconds && !IsBattleInitialized())
        {
            initElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Assert.IsTrue(IsBattleInitialized(), "BattleScene failed to initialize within timeout.");

        bool oldInstant = AnimeManager.Instant;
        AnimeManager.Instant = true;
        GameObject cardObject = null;
        try
        {
            PlayerController player = GM.Ins.BM.players[0];
            Assert.IsNotNull(player.handController, "Main player must have a hand controller.");
            int handBefore = player.handController.handCards.Count;
            int deckBefore = player.deckCards.Count;
            Assert.Greater(deckBefore, 0, "Main player must have at least one deck card for draw smoke test.");

            cardObject = Object.Instantiate(Resources.Load<GameObject>("Prefabs/Card"));
            CardController card = cardObject.GetComponent<CardController>();
            Assert.IsNotNull(card, "Card prefab must have a CardController.");

            CardData spellData = new()
            {
                index = 9999,
                name = "RegistrySmokeDraw",
                cardType = CardType.SPELL,
                cost = 0,
                effects = new List<CardEffectData>
                {
                    new() { effectType = EffectType.Draw, effectValues = new[] { 1 } },
                },
            };
            card.Init(spellData, player);

            bool completed = false;
            GM.Ins.BM.EM.TriggerSpellEffect(card, null, () => completed = true);

            Assert.IsTrue(completed, "Spell effect callback must complete synchronously with Instant animations.");
            Assert.AreEqual(
                handBefore + 1,
                player.handController.handCards.Count,
                "Draw effect must move exactly one deck card to hand.");
            Assert.AreEqual(
                deckBefore - 1,
                player.deckCards.Count,
                "Draw effect must remove exactly one deck card.");
        }
        finally
        {
            AnimeManager.Instant = oldInstant;
            if (cardObject != null)
            {
                Object.Destroy(cardObject);
            }
        }
    }

    [UnityTest]
    public IEnumerator QueuedDrawSpell_FromFullHand_DrawsIntoHandAndSpellEntersGraveyard()
    {
        SceneManager.LoadScene("BattleScene");
        yield return null;

        float initElapsed = 0f;
        while (initElapsed < InitTimeoutSeconds && !IsBattleInitialized())
        {
            initElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Assert.IsTrue(IsBattleInitialized(), "BattleScene failed to initialize within timeout.");

        bool oldInstant = AnimeManager.Instant;
        AnimeManager.Instant = true;
        GameObject spellObject = null;
        try
        {
            PlayerController player = GM.Ins.BM.players[0];
            Assert.IsNotNull(player.handController, "Main player must have a hand controller.");
            Assert.Greater(player.deckCards.Count, 0, "Main player must have at least one deck card.");

            while (player.handController.handCards.Count < GameConst.handMax - 1)
            {
                GameObject fillerObject = Object.Instantiate(Resources.Load<GameObject>("Prefabs/Card"));
                CardController fillerCard = fillerObject.GetComponent<CardController>();
                CardData fillerData = new()
                {
                    index = 9900 + player.handController.handCards.Count,
                    name = "QueueSmokeFiller",
                    cardType = CardType.Minion,
                    cost = 99,
                    effects = new List<CardEffectData>(),
                };
                fillerCard.Init(fillerData, player);
                player.handController.AddCard(fillerCard);
            }

            spellObject = Object.Instantiate(Resources.Load<GameObject>("Prefabs/Card"));
            CardController card = spellObject.GetComponent<CardController>();
            Assert.IsNotNull(card, "Card prefab must have a CardController.");
            CardData spellData = new()
            {
                index = 9998,
                name = "QueueSmokeDraw",
                cardType = CardType.SPELL,
                cost = 0,
                effects = new List<CardEffectData>
                {
                    new() { effectType = EffectType.Draw, effectValues = new[] { 1 } },
                },
            };
            card.Init(spellData, player);
            player.handController.AddCard(card);

            int deckBefore = player.deckCards.Count;
            Assert.AreEqual(
                GameConst.handMax,
                player.handController.handCards.Count,
                "Hand must be at the cap before queueing.");

            bool queued = false;
            float queueElapsed = 0f;
            while (queueElapsed < InitTimeoutSeconds && !queued)
            {
                queued = GM.Ins.BM.TryQueueHandCardPlay(card, null);
                if (!queued)
                {
                    queueElapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            Assert.IsTrue(queued, "Draw spell must be queueable from a full hand once the turn starts.");

            float resolveElapsed = 0f;
            while (resolveElapsed < InitTimeoutSeconds && card.state != CardState.Graveyard)
            {
                resolveElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(CardState.Graveyard, card.state, "Queued spell must enter the graveyard after its effect executes.");
            Assert.AreEqual(
                deckBefore - 1,
                player.deckCards.Count,
                "Draw effect must remove one deck card.");
            Assert.AreEqual(
                GameConst.handMax,
                player.handController.handCards.Count,
                "Drawn card must enter the hand since the resolving spell no longer occupies a hand slot.");
            Assert.IsTrue(player.graveCards.Contains(card), "Queued spell must be in the player's graveyard.");
        }
        finally
        {
            AnimeManager.Instant = oldInstant;
            if (spellObject != null)
            {
                Object.Destroy(spellObject);
            }
        }
    }

    private static bool IsBattleInitialized()
    {
        if (GM.Ins == null || GM.Ins.BM == null || GM.Ins.BM.players == null)
        {
            return false;
        }

        if (GM.Ins.BM.players.Count < 2)
        {
            return false;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null || player.handController == null || player.handController.handCards.Count < GameConst.initalHands)
            {
                return false;
            }
        }

        return true;
    }
}
