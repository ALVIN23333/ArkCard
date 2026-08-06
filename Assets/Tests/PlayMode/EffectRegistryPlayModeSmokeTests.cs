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
