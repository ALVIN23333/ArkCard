using System;
using System.Collections.Generic;
using Unity.Barracuda;

public sealed class BarracudaPolicyValueProvider : IPolicyValueProvider
{
    public const string PolicyInputName = "policy_input";
    public const string PolicyOutputName = "policy_logit";
    public const string ValueInputName = "state_input";
    public const string ValueOutputName = "value";

    private readonly IWorker policyWorker;
    private readonly IWorker valueWorker;
    private bool disposed;

    public BarracudaPolicyValueProvider(AIModelConfig config)
    {
        if (config == null)
        {
            throw new AIModelUnavailableException("AI model config is missing.");
        }
        if (!config.Validate(out string error))
        {
            throw new AIModelUnavailableException(error);
        }

        ModelVersion = string.IsNullOrWhiteSpace(config.modelVersion) ? "unknown" : config.modelVersion;
        try
        {
            Model policy = ModelLoader.Load(config.policyModel);
            Model value = ModelLoader.Load(config.valueModel);
            ValidateModelContract(policy, PolicyInputName, AIEncodingSchema.PolicyInputFeatureCount, PolicyOutputName, "policy");
            ValidateModelContract(value, ValueInputName, AIEncodingSchema.StateFeatureCount, ValueOutputName, "value");
            policyWorker = WorkerFactory.CreateWorker(config.inferenceBackend, policy);
            valueWorker = WorkerFactory.CreateWorker(config.inferenceBackend, value);
            IsReady = true;
        }
        catch (Exception exception)
        {
            Dispose();
            throw new AIModelUnavailableException("Barracuda could not load the policy/value models.", exception);
        }
    }

    public bool IsReady { get; private set; }
    public string ModelVersion { get; }

    public PolicyValueEvaluation Evaluate(
        BattleStateSnapshot state,
        int observerPlayerIndex,
        IReadOnlyList<SimulatedAction> legalActions)
    {
        if (disposed || !IsReady)
        {
            return PolicyValueEvaluation.Failed("Barracuda provider is not ready.", ModelVersion);
        }
        try
        {
            float[] stateFeatures = AIFeatureEncoder.EncodeState(state, observerPlayerIndex);
            List<float> priors = new();
            if (legalActions != null && legalActions.Count > 0)
            {
                float[] policyBatch = new float[legalActions.Count * AIEncodingSchema.PolicyInputFeatureCount];
                for (int actionIndex = 0; actionIndex < legalActions.Count; actionIndex++)
                {
                    float[] actionFeatures = AIFeatureEncoder.EncodeAction(state, observerPlayerIndex, legalActions[actionIndex]);
                    int rowOffset = actionIndex * AIEncodingSchema.PolicyInputFeatureCount;
                    Array.Copy(stateFeatures, 0, policyBatch, rowOffset, stateFeatures.Length);
                    Array.Copy(actionFeatures, 0, policyBatch, rowOffset + stateFeatures.Length, actionFeatures.Length);
                }
                float[] logits = ExecutePolicy(policyBatch, legalActions.Count);
                priors = Softmax(logits);
            }

            float value = ExecuteValue(stateFeatures);
            if (!IsFinite(value))
            {
                return PolicyValueEvaluation.Failed("Value model returned a non-finite value.", ModelVersion);
            }

            return new PolicyValueEvaluation
            {
                Success = true,
                ModelVersion = ModelVersion,
                Value = Math.Max(-1f, Math.Min(1f, value)),
                Priors = priors,
            };
        }
        catch (Exception exception)
        {
            return PolicyValueEvaluation.Failed($"Barracuda inference failed: {exception.Message}", ModelVersion);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        IsReady = false;
        policyWorker?.Dispose();
        valueWorker?.Dispose();
    }

    private float[] ExecutePolicy(float[] batch, int actionCount)
    {
        using Tensor input = new(actionCount, AIEncodingSchema.PolicyInputFeatureCount, batch, PolicyInputName);
        policyWorker.Execute(new Dictionary<string, Tensor> { { PolicyInputName, input } });
        Tensor output = policyWorker.PeekOutput(PolicyOutputName);
        if (output == null)
        {
            output = policyWorker.PeekOutput();
        }
        if (output == null || output.length != actionCount)
        {
            throw new InvalidOperationException($"Policy output length must be {actionCount}, actual {output?.length ?? 0}.");
        }

        float[] logits = new float[actionCount];
        for (int index = 0; index < actionCount; index++)
        {
            logits[index] = output[index];
            if (!IsFinite(logits[index]))
            {
                throw new InvalidOperationException("Policy model returned a non-finite logit.");
            }
        }
        return logits;
    }

    private float ExecuteValue(float[] stateFeatures)
    {
        using Tensor input = new(1, AIEncodingSchema.StateFeatureCount, stateFeatures, ValueInputName);
        valueWorker.Execute(new Dictionary<string, Tensor> { { ValueInputName, input } });
        Tensor output = valueWorker.PeekOutput(ValueOutputName);
        if (output == null)
        {
            output = valueWorker.PeekOutput();
        }
        if (output == null || output.length != 1)
        {
            throw new InvalidOperationException($"Value output length must be 1, actual {output?.length ?? 0}.");
        }
        return output[0];
    }

    private static void ValidateModelContract(
        Model model,
        string inputName,
        int expectedFeatureCount,
        string outputName,
        string label)
    {
        if (model == null)
        {
            throw new InvalidOperationException($"The {label} model could not be loaded.");
        }

        Model.Input input = default;
        bool foundInput = false;
        foreach (Model.Input candidate in model.inputs)
        {
            if (candidate.name == inputName)
            {
                input = candidate;
                foundInput = true;
                break;
            }
        }
        if (!foundInput)
        {
            throw new InvalidOperationException($"The {label} model input must be named '{inputName}'.");
        }
        if (input.shape == null || input.shape.Length == 0)
        {
            throw new InvalidOperationException($"The {label} model input shape is missing.");
        }

        int declaredFeatureCount = input.shape[input.shape.Length - 1];
        if (declaredFeatureCount > 0 && declaredFeatureCount != expectedFeatureCount)
        {
            throw new InvalidOperationException(
                $"The {label} model feature dimension must be {expectedFeatureCount}, actual {declaredFeatureCount}.");
        }
        if (!model.outputs.Contains(outputName))
        {
            throw new InvalidOperationException($"The {label} model output must be named '{outputName}'.");
        }
    }

    private static List<float> Softmax(float[] logits)
    {
        float maximum = float.NegativeInfinity;
        foreach (float logit in logits)
        {
            maximum = Math.Max(maximum, logit);
        }

        double sum = 0;
        double[] exponents = new double[logits.Length];
        for (int index = 0; index < logits.Length; index++)
        {
            exponents[index] = Math.Exp(logits[index] - maximum);
            sum += exponents[index];
        }
        if (sum <= 0 || double.IsNaN(sum) || double.IsInfinity(sum))
        {
            throw new InvalidOperationException("Policy softmax normalization failed.");
        }

        List<float> priors = new(logits.Length);
        for (int index = 0; index < logits.Length; index++)
        {
            priors.Add((float)(exponents[index] / sum));
        }
        return priors;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
