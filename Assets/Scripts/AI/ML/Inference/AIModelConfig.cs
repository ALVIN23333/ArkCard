using System;
using System.Security.Cryptography;
using Unity.Barracuda;
using UnityEngine;

[CreateAssetMenu(fileName = "DefaultAIModelConfig", menuName = "ArkCards/AI/Model Config")]
public sealed class AIModelConfig : ScriptableObject
{
    [Header("Models")]
    public NNModel policyModel;
    public NNModel valueModel;
    public int featureSchemaVersion = AIEncodingSchema.Version;
    public string modelVersion = "untrained";
    public string modelChecksum = string.Empty;

    [Header("Search")]
    [Range(16, 2048)] public int searchIterations = 192;
    [Range(1, 250)] public int timeBudgetMs = 50;
    [Range(0.1f, 5f)] public float explorationConstant = 1.5f;
    [Range(1, 16)] public int determinizationCount = 4;
    [Range(1, 3)] public int maxRootTurns = 2;
    [Range(4, 128)] public int maxSearchDepth = 32;
    public WorkerFactory.Type inferenceBackend = WorkerFactory.Type.CSharpBurst;

    public bool Validate(out string error)
    {
        if (featureSchemaVersion != AIEncodingSchema.Version)
        {
            error = $"Model schema {featureSchemaVersion} does not match runtime schema {AIEncodingSchema.Version}.";
            return false;
        }
        if (!AIEncodingSchema.IsRuntimeCompatible(out error))
        {
            return false;
        }
        if (policyModel == null || valueModel == null)
        {
            error = "Policy and value ONNX models must both be assigned.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(modelChecksum))
        {
            string actualChecksum;
            try
            {
                actualChecksum = ComputeCombinedChecksum(policyModel, valueModel);
            }
            catch (Exception exception)
            {
                error = $"Could not calculate the AI model checksum: {exception.Message}";
                return false;
            }
            if (!string.Equals(modelChecksum.Trim(), actualChecksum, StringComparison.OrdinalIgnoreCase))
            {
                error = $"AI model checksum mismatch. Expected {modelChecksum.Trim()}, actual {actualChecksum}.";
                return false;
            }
        }
        if (searchIterations <= 0 || timeBudgetMs <= 0 || determinizationCount <= 0 || maxSearchDepth <= 0)
        {
            error = "Search settings must be positive.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public NeuralMCTSSettings CreateSearchSettings()
    {
        return new NeuralMCTSSettings
        {
            Iterations = searchIterations,
            TimeBudgetMs = timeBudgetMs,
            ExplorationConstant = explorationConstant,
            DeterminizationCount = determinizationCount,
            MaxRootTurns = maxRootTurns,
            MaxSearchDepth = maxSearchDepth,
        };
    }

    public static string ComputeCombinedChecksum(NNModel policy, NNModel value)
    {
        byte[] policyBytes = policy != null && policy.modelData != null ? policy.modelData.Value : null;
        byte[] valueBytes = value != null && value.modelData != null ? value.modelData.Value : null;
        if (policyBytes == null || policyBytes.Length == 0 || valueBytes == null || valueBytes.Length == 0)
        {
            throw new InvalidOperationException("Imported policy/value model bytes are missing.");
        }

        using SHA256 sha256 = SHA256.Create();
        sha256.TransformBlock(policyBytes, 0, policyBytes.Length, null, 0);
        sha256.TransformFinalBlock(valueBytes, 0, valueBytes.Length);
        return BitConverter.ToString(sha256.Hash).Replace("-", string.Empty).ToLowerInvariant();
    }
}
