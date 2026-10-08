using Unity.MLAgents;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class MLAgentsTrainingHarness : MonoBehaviour
{
    [SerializeField] private float trainingTimeScale = 4f;
    [SerializeField] private float trainingActionInterval = 0.2f;
    [SerializeField] private float restartDelay = 0.75f;

    private bool restartQueued;

    private void Update()
    {
        if (!Academy.IsInitialized || !Academy.Instance.IsCommunicatorOn)
        {
            return;
        }

        ApplyTrainingSetup();
        if (GM.Ins == null || GM.Ins.BM == null || !GM.Ins.BM.IsGameOver || restartQueued)
        {
            return;
        }

        restartQueued = true;
        Invoke(nameof(ReloadBattle), restartDelay);
    }

    private void ApplyTrainingSetup()
    {
        Time.timeScale = trainingTimeScale;
        AIController agentPlayer = FindObjectOfType<AIController>();
        if (agentPlayer != null)
        {
            agentPlayer.SetLLMStrategyEnabled(false);
            agentPlayer.ApplyTrainingCadence(trainingActionInterval);
        }

        AutoPlayDriver driver = AutoPlayDriver.GetOrCreate();
        driver.SetActionInterval(trainingActionInterval);
        driver.SetSearchBudget(200, 10);
        if (!driver.enabled)
        {
            driver.enabled = true;
        }
    }

    private void ReloadBattle()
    {
        restartQueued = false;
        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.name);
    }
}
