using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// 全流程冒烟测试：加载 BattleScene，双方 AI 托管（主玩家走 AutoPlayDriver，敌方走 AIController），
/// 在跳过动画 + 3 倍速下跑完整局并断言到达胜负判定且无异常日志。
/// </summary>
public class BattleSmokeTests
{
    private const float InitTimeoutSeconds = 30f;
    private const float GameOverTimeoutSeconds = 180f;

    private AutoPlayDriver driver;

    [UnityTest]
    public IEnumerator FullGame_AIAutoBattle_ReachesGameOver()
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
        Debug.Log("[SMOKE] battle initialized");

        Time.timeScale = 3f;
        AnimeManager.Instant = true;

        driver = AutoPlayDriver.GetOrCreate();
        driver.enabled = true;

        float elapsed = 0f;
        while (elapsed < GameOverTimeoutSeconds && !GM.Ins.BM.IsGameOver)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Assert.IsTrue(GM.Ins.BM.IsGameOver, $"Game did not finish within {GameOverTimeoutSeconds}s (unscaled).");
        Debug.Log("[SMOKE] game over reached");
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

    [UnityTest]
    public IEnumerator Snapshot_HidesOpponentHandAndUsesBeliefPool()
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

        BattleManager battleManager = GM.Ins.BM;
        int enemyIndex = -1;
        int mainIndex = -1;
        for (int i = 0; i < battleManager.players.Count; i++)
        {
            if (battleManager.players[i] == null)
            {
                continue;
            }
            if (battleManager.players[i].isMainPlayer)
            {
                mainIndex = i;
            }
            else
            {
                enemyIndex = i;
            }
        }
        Assert.GreaterOrEqual(enemyIndex, 0, "Enemy player must exist.");
        Assert.GreaterOrEqual(mainIndex, 0, "Main player must exist.");

        Assert.IsTrue(
            SnapshotFactory.TryCreate(battleManager, enemyIndex, out BattleStateSnapshot snapshot, out string error, null),
            $"Snapshot creation failed: {error}");

        PlayerStateSnapshot ai = snapshot.GetPlayer(enemyIndex);
        PlayerStateSnapshot opponent = snapshot.GetPlayer(mainIndex);
        PlayerController opponentRuntime = battleManager.players[mainIndex];
        PlayerController aiRuntime = battleManager.players[enemyIndex];

        Assert.IsFalse(ai.HandIsHidden, "AI must see its own hand.");
        Assert.AreEqual(aiRuntime.handController != null ? aiRuntime.handController.handCards.Count : 0, ai.Hand.Count);
        Assert.AreEqual(aiRuntime.deckCards != null ? aiRuntime.deckCards.Count : 0, ai.HiddenDeckCount);
        Assert.AreEqual(aiRuntime.deckCards != null ? aiRuntime.deckCards.Count : 0, ai.DeckRemaining.Count, "AI deck must be carried as real runtime cards.");

        Assert.IsTrue(opponent.HandIsHidden, "Opponent hand must be hidden from the AI.");
        Assert.AreEqual(0, opponent.Hand.Count, "Opponent hand must not contain card entities.");
        Assert.AreEqual(opponentRuntime.handController != null ? opponentRuntime.handController.handCards.Count : 0, opponent.HiddenHandCount);
        Assert.AreEqual(opponentRuntime.deckCards != null ? opponentRuntime.deckCards.Count : 0, opponent.HiddenDeckCount);
        Assert.AreEqual(0, opponent.DeckRemaining.Count, "Opponent deck must not leak real card entities.");
        Assert.IsTrue(opponent.HiddenCardPool.Count > 0, "Belief pool must be populated.");
        if (GM.Ins != null && GM.Ins.DM != null && GM.Ins.DM.so != null && GM.Ins.DM.so.cards != null)
        {
            Assert.AreEqual(GM.Ins.DM.so.cards.Count * 2, opponent.HiddenCardPool.Count, "Default belief pool must contain two copies of every database card.");
        }

        snapshot.Determinize(new System.Random(123));
        Assert.AreEqual(opponent.HiddenHandCount, opponent.Hand.Count, "Determinize must materialize the hidden hand.");
        Assert.AreEqual(opponent.HiddenDeckCount, opponent.DeckRemaining.Count, "Determinize must materialize the hidden deck.");
        foreach (CardStateSnapshot card in opponent.Hand)
        {
            Assert.Less(card.RuntimeId, 0, "Hidden hand cards must use synthetic negative ids.");
        }

        List<SimulatedAction> actions = new BattleStateSimulator().GenerateLegalActions(snapshot);
        Assert.IsNotEmpty(actions, "Determinized snapshot must produce legal actions.");
    }

    [UnityTest]
    public IEnumerator Deathrattle_TargetedEffect_AutoResolves_WhenMainPlayerIsAiControlled()
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

        PlayerController mainPlayer = null;
        PlayerController enemy = null;
        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null)
            {
                continue;
            }
            if (player.isMainPlayer)
            {
                mainPlayer = player;
            }
            else
            {
                enemy = player;
            }
        }
        Assert.IsNotNull(mainPlayer, "Main player must exist.");
        Assert.IsNotNull(enemy, "Enemy player must exist.");

        AutoPlayDriver driver = AutoPlayDriver.GetOrCreate();
        driver.enabled = true;
        yield return null; // Let AutoPlayDriver.Update apply the AI-control flag.

        Assert.IsTrue(mainPlayer.IsAIControlled, "Main player must be marked AI-controlled after driver enable.");

        CardListSO database = GM.Ins.DM != null && GM.Ins.DM.so != null
            ? GM.Ins.DM.so
            : Resources.Load<CardListSO>("ArkCardsDatabase");
        Assert.IsNotNull(database, "Card database must exist.");
        CardData deathrattleData = database.GetData(1003); // 华法琳：亡语选择敌方随从 -2/-2
        CardData enemyMinionData = database.GetData(1009); // 拉普兰德 5/4
        Assert.IsNotNull(deathrattleData, "Deathrattle card data (1003) must exist.");
        Assert.IsNotNull(enemyMinionData, "Enemy minion card data (1009) must exist.");

        CardController deathrattleCard = Object.Instantiate(
                GM.Ins.BM.cardPrefab,
                mainPlayer.fieldController != null ? mainPlayer.fieldController.transform : null)
            .GetComponent<CardController>();
        deathrattleCard.Init(deathrattleData, mainPlayer);
        deathrattleCard.state = CardState.Field;
        mainPlayer.fieldController.AddCard(deathrattleCard);

        CardController enemyMinion = Object.Instantiate(
                GM.Ins.BM.cardPrefab,
                enemy.fieldController != null ? enemy.fieldController.transform : null)
            .GetComponent<CardController>();
        enemyMinion.Init(enemyMinionData, enemy);
        enemyMinion.state = CardState.Field;
        enemy.fieldController.AddCard(enemyMinion);

        AnimeManager.Instant = true;
        deathrattleCard.Kill();

        for (int frame = 0; frame < 10; frame++)
        {
            yield return null;
        }

        Assert.AreEqual(3, enemyMinion.atk, "Deathrattle -2/-2 must auto-resolve on the enemy minion.");
        Assert.AreEqual(2, enemyMinion.health, "Deathrattle -2/-2 must auto-resolve on the enemy minion.");
        Assert.IsFalse(GM.Ins.BM.TM.HasActiveSelection, "No manual target selection may remain pending.");
        Assert.IsFalse(GM.Ins.BM.EM.IsProcessingEffects, "Effect chain must complete.");
    }

    [UnityTest]
    public IEnumerator Deathrattle_AutoResolves_WhenDriverEnabledBeforeBattleInit()
    {
        SceneManager.LoadScene("BattleScene");
        yield return null;

        // Enable takeover BEFORE battle init: the per-frame flag must land once players exist.
        AutoPlayDriver driver = AutoPlayDriver.GetOrCreate();
        driver.enabled = true;

        float initElapsed = 0f;
        while (initElapsed < InitTimeoutSeconds && !IsBattleInitialized())
        {
            initElapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        Assert.IsTrue(IsBattleInitialized(), "BattleScene failed to initialize within timeout.");
        yield return null;

        PlayerController mainPlayer = FindPlayer(true);
        PlayerController enemy = FindPlayer(false);
        Assert.IsNotNull(mainPlayer, "Main player must exist.");
        Assert.IsNotNull(enemy, "Enemy player must exist.");
        Assert.IsTrue(mainPlayer.IsAIControlled, "Main player must be AI-controlled when the driver was enabled before init.");

        CardController deathrattleCard = SpawnCard(mainPlayer, 1003); // 华法琳：亡语选择敌方随从 -2/-2
        CardController enemyMinion = SpawnCard(enemy, 1009); // 拉普兰德 5/4
        AnimeManager.Instant = true;
        deathrattleCard.Kill();

        for (int frame = 0; frame < 10; frame++)
        {
            yield return null;
        }

        Assert.AreEqual(3, enemyMinion.atk, "Deathrattle -2/-2 must auto-resolve on the enemy minion.");
        Assert.AreEqual(2, enemyMinion.health, "Deathrattle -2/-2 must auto-resolve on the enemy minion.");
        Assert.IsFalse(GM.Ins.BM.TM.HasActiveSelection, "No manual target selection may remain pending.");
    }

    [UnityTest]
    public IEnumerator Deathrattle_Enemy_AutoResolves_EvenWithAIControllerDisabled()
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

        PlayerController enemy = FindPlayer(false);
        PlayerController mainPlayer = FindPlayer(true);
        Assert.IsNotNull(enemy, "Enemy player must exist.");
        Assert.IsNotNull(mainPlayer, "Main player must exist.");

        AIController enemyAi = enemy.GetComponent<AIController>();
        if (enemyAi != null)
        {
            enemyAi.enabled = false;
        }
        enemy.IsAIControlled = false; // The non-main-player fallback must handle this case.

        CardController deathrattleCard = SpawnCard(enemy, 1003); // 敌方华法琳：亡语选择敌方随从 -2/-2
        CardController mainMinion = SpawnCard(mainPlayer, 1009); // 我方拉普兰德 5/4
        AnimeManager.Instant = true;
        deathrattleCard.Kill();

        for (int frame = 0; frame < 10; frame++)
        {
            yield return null;
        }

        Assert.AreEqual(3, mainMinion.atk, "Enemy deathrattle must auto-resolve even with AIController disabled.");
        Assert.AreEqual(2, mainMinion.health, "Enemy deathrattle must auto-resolve even with AIController disabled.");
        Assert.IsFalse(GM.Ins.BM.TM.HasActiveSelection, "No manual target selection may remain pending.");
    }

    private static PlayerController FindPlayer(bool isMain)
    {
        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player != null && player.isMainPlayer == isMain)
            {
                return player;
            }
        }
        return null;
    }

    private static CardController SpawnCard(PlayerController player, int cardIndex)
    {
        CardListSO database = GM.Ins.DM != null && GM.Ins.DM.so != null
            ? GM.Ins.DM.so
            : Resources.Load<CardListSO>("ArkCardsDatabase");
        CardData data = database != null ? database.GetData(cardIndex) : null;
        Assert.IsNotNull(data, $"Card data {cardIndex} must exist.");
        CardController card = Object.Instantiate(
                GM.Ins.BM.cardPrefab,
                player.fieldController != null ? player.fieldController.transform : null)
            .GetComponent<CardController>();
        card.Init(data, player);
        card.state = CardState.Field;
        player.fieldController.AddCard(card);
        return card;
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        AnimeManager.Instant = false;
        if (driver != null)
        {
            Object.Destroy(driver.gameObject);
            driver = null;
        }
    }
}
