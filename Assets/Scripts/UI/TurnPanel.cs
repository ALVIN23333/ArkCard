using System;
using TMPro;
using UnityEngine;

public class TurnPanel : CostomPanel
{
    private TMP_Text turnText;
    private int showVersion;

    public override void Init()
    {
        CacheReferences();
        base.Init();
    }

    public void ShowTurn(PlayerController player, Action onComplete, float minDuration = 0.6f)
    {
        CacheReferences();

        if (turnText != null)
        {
            turnText.text = player != null && player.isMainPlayer ? "我方回合" : "敌方回合";
        }

        int version = ++showVersion;
        Show();

        AnimeManager.Delay(Mathf.Max(0.6f, minDuration), () =>
        {
            if (version != showVersion)
            {
                return;
            }

            Close();
            onComplete?.Invoke();
        });
    }

    private void CacheReferences()
    {
        if (turnText == null)
        {
            turnText = GetComponentInChildren<TMP_Text>(true);
        }
    }
}
