using UnityEditor;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;

public static class MLAgentsSceneSetup
{
    [MenuItem("ArkCard/AI/Configure ML-Agents on AI Player")]
    public static void ConfigureAIPlayer()
    {
        AIController controller = Object.FindObjectOfType<AIController>();
        if (controller == null)
        {
            Debug.LogError("[ArkCard AI] No AIController was found in the active scene.");
            return;
        }

        CardBattleAgent agent = controller.GetComponent<CardBattleAgent>();
        if (agent == null)
        {
            agent = Undo.AddComponent<CardBattleAgent>(controller.gameObject);
        }

        BehaviorParameters behavior = controller.GetComponent<BehaviorParameters>();
        if (behavior == null)
        {
            behavior = Undo.AddComponent<BehaviorParameters>(controller.gameObject);
        }

        DecisionRequester requester = controller.GetComponent<DecisionRequester>();
        if (requester == null)
        {
            requester = Undo.AddComponent<DecisionRequester>(controller.gameObject);
        }

        Undo.RecordObjects(new Object[] { behavior, requester }, "Configure ML-Agents");
        behavior.BehaviorName = "CardBattleAgent";
        behavior.BrainParameters.VectorObservationSize = AIEncodingSchema.StateFeatureCount;
        behavior.BrainParameters.NumStackedVectorObservations = 1;
        behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(new[] { 64 });
        behavior.BehaviorType = BehaviorType.Default;
        requester.DecisionPeriod = 5;
        requester.TakeActionsBetweenDecisions = true;

        EditorUtility.SetDirty(agent);
        EditorUtility.SetDirty(behavior);
        EditorUtility.SetDirty(requester);
        Debug.Log($"[ArkCard AI] Configured {controller.gameObject.name}: observation={AIEncodingSchema.StateFeatureCount}, action branch=64.");
    }
}