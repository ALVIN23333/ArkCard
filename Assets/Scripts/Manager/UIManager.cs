using System;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public Button endTurnButton;
    public GameObject WinPanel;
    public GameObject TurnPanel;

    private WinPanel winPanelComponent;
    private TurnPanel turnPanelComponent;
    private bool isInitialized;

    public void Init()
    {
        if (isInitialized)
        {
            return;
        }

        isInitialized = true;

        if (endTurnButton != null)
        {
            endTurnButton.onClick.AddListener(OnEndTurnButtonClicked);
        }

        ResolvePanels();
        winPanelComponent?.Init();
        turnPanelComponent?.Init();
        winPanelComponent?.Close();
        turnPanelComponent?.Close();
    }

    public void OnEndTurnButtonClicked()
    {
        if (GM.Ins == null || GM.Ins.BM == null)
        {
            return;
        }

        GM.Ins.BM.EndMainPlayerTurn();
    }

    public void ShowWinPanel(PlayerController winner, bool isDraw)
    {
        ResolvePanels();
        winPanelComponent?.ShowWinner(winner, isDraw);
    }

    public void ShowTurnPanel(PlayerController player, Action onComplete, float minDuration = 0.2f)
    {
        ResolvePanels();
        if (turnPanelComponent == null)
        {
            onComplete?.Invoke();
            return;
        }

        turnPanelComponent.ShowTurn(player, onComplete, minDuration);
    }

    private void OnDestroy()
    {
        if (isInitialized && endTurnButton != null)
        {
            endTurnButton.onClick.RemoveListener(OnEndTurnButtonClicked);
        }
    }

    private void ResolvePanels()
    {
        if (WinPanel == null)
        {
            WinPanel = FindSceneObjectByName("WinPanel");
        }

        if (TurnPanel == null)
        {
            TurnPanel = FindSceneObjectByName("TurnPanel");
        }

        if (winPanelComponent == null && WinPanel != null)
        {
            winPanelComponent = GetOrAddPanel<WinPanel>(WinPanel);
        }

        if (turnPanelComponent == null && TurnPanel != null)
        {
            turnPanelComponent = GetOrAddPanel<TurnPanel>(TurnPanel);
        }
    }

    private T GetOrAddPanel<T>(GameObject panelObject) where T : Component
    {
        T panel = panelObject.GetComponent<T>();
        return panel != null ? panel : panelObject.AddComponent<T>();
    }

    private GameObject FindSceneObjectByName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return null;
        }

        foreach (GameObject rootObject in gameObject.scene.GetRootGameObjects())
        {
            Transform found = FindChildByName(rootObject.transform, objectName);
            if (found != null)
            {
                return found.gameObject;
            }
        }

        return null;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        if (root.name == childName)
        {
            return root;
        }

        foreach (Transform child in root)
        {
            Transform found = FindChildByName(child, childName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
