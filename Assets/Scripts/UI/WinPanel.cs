using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class WinPanel : CostomPanel
{
    private TMP_Text resultText;
    private Button restartButton;
    private Button quitButton;
    private UnityAction restartAction;
    private UnityAction quitAction;

    public override void Init()
    {
        CacheReferences();
        base.Init();
    }

    public void ShowWinner(PlayerController winner, bool isDraw)
    {
        CacheReferences();

        if (resultText != null)
        {
            if (isDraw)
            {
                resultText.text = "平局";
            }
            else
            {
                resultText.text = winner != null && winner.isMainPlayer ? "我方获胜" : "敌方获胜";
            }
        }

        Show();
    }

    protected override void BindEvents()
    {
        restartAction = OnRestartClicked;
        quitAction = OnQuitClicked;

        if (restartButton != null)
        {
            restartButton.onClick.AddListener(restartAction);
        }

        if (quitButton != null)
        {
            quitButton.onClick.AddListener(quitAction);
        }
    }

    protected override void UnbindEvents()
    {
        if (restartButton != null && restartAction != null)
        {
            restartButton.onClick.RemoveListener(restartAction);
        }

        if (quitButton != null && quitAction != null)
        {
            quitButton.onClick.RemoveListener(quitAction);
        }
    }

    private void CacheReferences()
    {
        if (resultText == null)
        {
            resultText = GetComponentInChildren<TMP_Text>(true);
        }

        if (restartButton != null && quitButton != null)
        {
            return;
        }

        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            TMP_Text buttonText = button.GetComponentInChildren<TMP_Text>(true);
            if (buttonText == null)
            {
                continue;
            }

            string text = buttonText.text;
            if (restartButton == null && text.Contains("重新开始"))
            {
                restartButton = button;
                continue;
            }

            if (quitButton == null && text.Contains("退出"))
            {
                quitButton = button;
            }
        }
    }

    private void OnRestartClicked()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.name);
    }

    private void OnQuitClicked()
    {
        Application.Quit();
    }
}
