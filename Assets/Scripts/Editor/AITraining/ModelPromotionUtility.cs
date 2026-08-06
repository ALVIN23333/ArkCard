using System;
using System.IO;
using System.Security.Cryptography;
using Unity.Barracuda;
using UnityEditor;
using UnityEngine;

public static class ModelPromotionUtility
{
    public const string PolicyPath = "Assets/AI/Models/policy.onnx";
    public const string ValuePath = "Assets/AI/Models/value.onnx";
    public const string ManifestPath = "Assets/AI/Models/manifest.json";
    public const string ConfigPath = "Assets/AI/Configs/DefaultAIModelConfig.asset";

    [MenuItem("Tools/AI Training/Refresh Default Config From Promoted Models")]
    public static void RefreshDefaultConfig()
    {
        if (!File.Exists(PolicyPath) || !File.Exists(ValuePath) || !File.Exists(ManifestPath))
        {
            throw new FileNotFoundException(
                $"Promoted model files must exist at {PolicyPath}, {ValuePath}, and {ManifestPath}.");
        }

        PromotedModelManifest manifest = JsonUtility.FromJson<PromotedModelManifest>(File.ReadAllText(ManifestPath));
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.modelVersion))
        {
            throw new InvalidDataException("Promoted model manifest is missing modelVersion.");
        }
        if (manifest.featureSchemaVersion != AIEncodingSchema.Version)
        {
            throw new InvalidDataException(
                $"Promoted model schema {manifest.featureSchemaVersion} does not match runtime schema {AIEncodingSchema.Version}.");
        }

        string sourceChecksum = ComputeSourceChecksum(PolicyPath, ValuePath);
        if (!string.Equals(sourceChecksum, manifest.combinedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Promoted ONNX checksum mismatch. Expected {manifest.combinedSha256}, actual {sourceChecksum}.");
        }

        AssetDatabase.ImportAsset(PolicyPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(ValuePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        NNModel policy = AssetDatabase.LoadAssetAtPath<NNModel>(PolicyPath);
        NNModel value = AssetDatabase.LoadAssetAtPath<NNModel>(ValuePath);
        if (policy == null || value == null)
        {
            throw new InvalidOperationException("Barracuda did not import the promoted ONNX files as NNModel assets.");
        }

        AIModelConfig config = AssetDatabase.LoadAssetAtPath<AIModelConfig>(ConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<AIModelConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
        }
        config.policyModel = policy;
        config.valueModel = value;
        config.featureSchemaVersion = manifest.featureSchemaVersion;
        config.modelVersion = manifest.modelVersion;
        config.modelChecksum = AIModelConfig.ComputeCombinedChecksum(policy, value);
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssetIfDirty(config);
        AssetDatabase.Refresh();
        Debug.Log($"[AI ML] Default model config refreshed: version={config.modelVersion}, runtimeChecksum={config.modelChecksum}");
    }

    private static string ComputeSourceChecksum(string policyPath, string valuePath)
    {
        byte[] policy = File.ReadAllBytes(policyPath);
        byte[] value = File.ReadAllBytes(valuePath);
        using SHA256 sha256 = SHA256.Create();
        sha256.TransformBlock(policy, 0, policy.Length, null, 0);
        sha256.TransformFinalBlock(value, 0, value.Length);
        return BitConverter.ToString(sha256.Hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    [Serializable]
    private sealed class PromotedModelManifest
    {
        public string modelVersion;
        public int featureSchemaVersion;
        public string combinedSha256;
    }
}
