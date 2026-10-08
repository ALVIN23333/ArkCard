using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class LethalEffectGameOverTests
{
    private const float InitTimeoutSeconds = 30f;
    private const float ResolveTimeoutSeconds = 5f;

    [UnityTest]
    public IEnumerator BattlecryDamage_EndsGameWithoutEndingTurn()
    {
        yield return LoadBattle();

        bool oldInstant = AnimeManager.Instant;
        AnimeManager.Instant = true;
        try
        {
            DisableAutoActors();
            PlayerController mainPlayer = FindPlayer(true);
            PlayerController enemy = FindPlayer(false);
            Assert.IsTrue(mainPlayer.isInTurn, "Main player must be in turn to play a battlecry.");
            enemy.SetHealth(2);

            CardData data = new()
            {
                index = 91008,
                name = "TestEyja",
                cardType = CardType.Minion,
                cost = 0,
                attack = 1,
                health = 1,
                effects = new List<CardEffectData>
                {
                    new()
                    {
                        triggerType = TriggerType.Enter,
                        effectType = EffectType.Damage,
                        targetSide = EffectTargetSide.Enemy,
                        targetMode = EffectTargetMode.All,
                        characterScope = EffectCharacterScope.Characters,
                        includeSource = false,
                        effectValues = new[] { 3, 1 },
                    },
                },
            };
            CardController card = CreateHandCard(mainPlayer, data);
            Assert.IsTrue(
                GM.Ins.BM.TryQueueHandCardPlay(card, mainPlayer.fieldController),
                "Battlecry minion must enter the play queue.");

            yield return WaitUntilGameOver();

            Assert.AreEqual(-1, enemy.health, "Lethal battlecry overkill must remain visible as negative hero health.");
            Assert.IsTrue(GM.Ins.BM.IsGameOver, "Game must end after lethal battlecry without ending the turn.");
            AssertWinPanelVisible();
        }
        finally
        {
            AnimeManager.Instant = oldInstant;
        }
    }

    [UnityTest]
    public IEnumerator FieldCastDamage_EndsGameWithoutEndingTurn()
    {
        yield return LoadBattle();

        bool oldInstant = AnimeManager.Instant;
        AnimeManager.Instant = true;
        try
        {
            DisableAutoActors();
            PlayerController mainPlayer = FindPlayer(true);
            PlayerController enemy = FindPlayer(false);
            Assert.IsTrue(mainPlayer.isInTurn, "Main player must be in turn to use a field cast.");
            enemy.SetHealth(2);

            CardController caster = SpawnFieldCard(mainPlayer, 1004);
            Assert.IsTrue(
                GM.Ins.BM.TryUseFieldCast(caster, true, new List<UnityEngine.Object> { enemy }),
                "Card 42 cast must resolve against the enemy hero.");

            yield return WaitUntilGameOver();

            Assert.AreEqual(-4, enemy.health, "Lethal field cast overkill must remain visible as negative hero health.");
            Assert.IsTrue(GM.Ins.BM.IsGameOver, "Game must end after lethal field cast without ending the turn.");
            AssertWinPanelVisible();
        }
        finally
        {
            AnimeManager.Instant = oldInstant;
        }
    }

    [UnityTest]
    public IEnumerator Snapshot_MarksGameOverFromZeroHealthWithoutCheckGameOver()
    {
        yield return LoadBattle();

        DisableAutoActors();
        PlayerController enemy = FindPlayer(false);
        enemy.SetHealth(0);
        Assert.IsFalse(GM.Ins.BM.IsGameOver, "SetHealth must not itself end the game.");

        int observerIndex = GM.Ins.BM.players.IndexOf(FindPlayer(true));
        Assert.IsTrue(
            SnapshotFactory.TryCreate(GM.Ins.BM, observerIndex, out BattleStateSnapshot snapshot, out string error, null),
            $"Snapshot creation failed: {error}");
        Assert.IsTrue(snapshot.IsGameOver, "Snapshot must treat a 0-health hero as game over.");

        List<SimulatedAction> actions = new BattleStateSimulator().GenerateLegalActions(snapshot);
        Assert.IsFalse(
            actions.Exists(action => action.Type != SimulatedActionType.EndTurn),
            "Legal actions must not include further plays after lethal damage.");
        Assert.IsEmpty(actions, "A finished snapshot must not generate playable actions.");
    }

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        AnimeManager.Instant = false;
        AutoPlayDriver driver = Object.FindObjectOfType<AutoPlayDriver>();
        if (driver != null)
        {
            Object.Destroy(driver.gameObject);
        }
    }

    private static IEnumerator LoadBattle()
    {
        SceneManager.LoadScene("BattleScene");
        yield return null;

        float elapsed = 0f;
        while (elapsed < InitTimeoutSeconds && !IsBattleInitialized())
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Assert.IsTrue(IsBattleInitialized(), "BattleScene failed to initialize within timeout.");
    }

    private static IEnumerator WaitUntilGameOver()
    {
        float elapsed = 0f;
        while (elapsed < ResolveTimeoutSeconds && !GM.Ins.BM.IsGameOver)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private static void DisableAutoActors()
    {
        AutoPlayDriver driver = Object.FindObjectOfType<AutoPlayDriver>();
        if (driver != null)
        {
            driver.enabled = false;
        }

        foreach (PlayerController player in GM.Ins.BM.players)
        {
            if (player == null)
            {
                continue;
            }

            AIController ai = player.GetComponent<AIController>();
            if (ai != null)
            {
                ai.enabled = false;
            }

            player.IsAIControlled = false;
        }
    }

    private static void AssertWinPanelVisible()
    {
        Assert.IsNotNull(GM.Ins.UM, "UIManager must exist.");
        Assert.IsNotNull(GM.Ins.UM.WinPanel, "WinPanel must be assigned.");
        Assert.IsTrue(GM.Ins.UM.WinPanel.activeInHierarchy, "WinPanel must be visible after lethal damage.");
    }

    private static bool IsBattleInitialized()
    {
        if (GM.Ins == null || GM.Ins.BM == null || GM.Ins.BM.players == null || GM.Ins.BM.players.Count < 2)
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

    private static CardController CreateHandCard(PlayerController player, CardData data)
    {
        CardController card = Object.Instantiate(GM.Ins.BM.cardPrefab, player.handController.transform)
            .GetComponent<CardController>();
        card.Init(data, player);
        player.handController.AddCard(card);
        return card;
    }

    private static CardController SpawnFieldCard(PlayerController player, int cardIndex)
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
}
