using System;
using System.IO;
using System.Security.Cryptography;
using Unity.Barracuda;
using UnityEditor;
using UnityEngine;

public static class ModelPromotionUtility
{
    public const string ModelsDirectory = "Assets/AI/Models";
    public const string ConfigPath = "Assets/AI/Configs/DefaultAIModelConfig.asset";

    [MenuItem("Tools/AI Training/Refresh Default Config From Promoted Models")]
    public static void RefreshDefaultConfig()
    {
        RefreshConfig(ModelsDirectory, ConfigPath);
    }

    public static void RefreshCandidateConfigFromCommandLine()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        string modelsDirectory = ReadStringArgument(
            arguments,
            "-aiCandidateModelsDir",
            "Assets/AI/Models/Candidate");
        string configPath = ReadStringArgument(
            arguments,
            "-aiCandidateConfigPath",
            "Assets/AI/Configs/CandidateAIModelConfig.asset");
        AIModelConfig config = RefreshConfig(modelsDirectory, configPath);
        Debug.Log($"[AI ML] Candidate config refreshed: version={config.modelVersion}, path={configPath}");
    }

    public static string ReadStringArgument(string[] arguments, string name, string fallback)
    {
        for (int index = 0; index + 1 < arguments.Length; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }
        return fallback;
    }

    public static AIModelConfig RefreshConfig(string modelsDirectory, string configPath)
    {
        string policyPath = CombineAssetPath(modelsDirectory, "policy.onnx");
        string valuePath = CombineAssetPath(modelsDirectory, "value.onnx");
        string manifestPath = CombineAssetPath(modelsDirectory, "manifest.json");
        if (!File.Exists(policyPath) || !File.Exists(valuePath) || !File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                $"Promoted model files must exist at {policyPath}, {valuePath}, and {manifestPath}.");
        }

        PromotedModelManifest manifest = JsonUtility.FromJson<PromotedModelManifest>(File.ReadAllText(manifestPath));
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.modelVersion))
        {
            throw new InvalidDataException("Promoted model manifest is missing modelVersion.");
        }
        if (manifest.featureSchemaVersion != AIEncodingSchema.Version)
        {
            throw new InvalidDataException(
                $"Promoted model schema {manifest.featureSchemaVersion} does not match runtime schema {AIEncodingSchema.Version}.");
        }

        string sourceChecksum = ComputeSourceChecksum(policyPath, valuePath);
        if (!string.Equals(sourceChecksum, manifest.combinedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Promoted ONNX checksum mismatch. Expected {manifest.combinedSha256}, actual {sourceChecksum}.");
        }

        AssetDatabase.ImportAsset(policyPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(valuePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        NNModel policy = AssetDatabase.LoadAssetAtPath<NNModel>(policyPath);
        NNModel value = AssetDatabase.LoadAssetAtPath<NNModel>(valuePath);
        if (policy == null || value == null)
        {
            throw new InvalidOperationException("Barracuda did not import the promoted ONNX files as NNModel assets.");
        }

        AIModelConfig config = AssetDatabase.LoadAssetAtPath<AIModelConfig>(configPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<AIModelConfig>();
            AssetDatabase.CreateAsset(config, configPath);
        }
        config.policyModel = policy;
        config.valueModel = value;
        config.featureSchemaVersion = manifest.featureSchemaVersion;
        config.modelVersion = manifest.modelVersion;
        config.modelChecksum = AIModelConfig.ComputeCombinedChecksum(policy, value);
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssetIfDirty(config);
        AssetDatabase.Refresh();
        Debug.Log($"[AI ML] Model config refreshed at {configPath}: version={config.modelVersion}, runtimeChecksum={config.modelChecksum}");
        return config;
    }

    private static string CombineAssetPath(string directory, string fileName)
    {
        return directory.TrimEnd('/') + "/" + fileName;
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
