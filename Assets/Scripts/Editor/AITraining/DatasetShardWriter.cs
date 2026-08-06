using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

public sealed class TrainingActionLabel
{
    public float[] Features;
    public int VisitCount;
}

public sealed class TrainingSample
{
    public long Seed;
    public int GameId;
    public int Ply;
    public int ObserverPlayerIndex;
    public float Outcome;
    public int[] FirstDeck = Array.Empty<int>();
    public int[] SecondDeck = Array.Empty<int>();
    public float[] StateFeatures;
    public List<TrainingActionLabel> Actions = new();
}

public sealed class DatasetShardWriter : IDisposable
{
    public const int FormatVersion = 1;
    public const string FileExtension = ".arkds.gz";

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ARKDS001");

    private readonly string outputDirectory;
    private readonly string prefix;
    private readonly int maxSamplesPerShard;
    private readonly List<string> completedShards = new();
    private BinaryWriter writer;
    private string currentPath;
    private int currentSampleCount;
    private int shardIndex;
    private bool disposed;

    public DatasetShardWriter(string outputDirectory, string prefix = "teacher", int maxSamplesPerShard = 2048)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Dataset output directory is required.", nameof(outputDirectory));
        }

        this.outputDirectory = Path.GetFullPath(outputDirectory);
        this.prefix = SanitizePrefix(prefix);
        this.maxSamplesPerShard = Math.Max(1, maxSamplesPerShard);
        Directory.CreateDirectory(this.outputDirectory);
    }

    public IReadOnlyList<string> CompletedShards => completedShards;
    public int TotalSamplesWritten { get; private set; }

    public void Write(TrainingSample sample)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(DatasetShardWriter));
        }

        Validate(sample);
        if (writer == null || currentSampleCount >= maxSamplesPerShard)
        {
            CompleteCurrentShard();
            OpenNextShard();
        }

        using MemoryStream payloadStream = new();
        using (BinaryWriter payload = new(payloadStream, Encoding.UTF8, true))
        {
            payload.Write(sample.Seed);
            payload.Write(sample.GameId);
            payload.Write(sample.Ply);
            payload.Write(sample.ObserverPlayerIndex);
            payload.Write(sample.Outcome);
            WriteIntArray(payload, sample.FirstDeck);
            WriteIntArray(payload, sample.SecondDeck);
            WriteFloatArray(payload, sample.StateFeatures);
            payload.Write(sample.Actions.Count);
            foreach (TrainingActionLabel action in sample.Actions)
            {
                WriteFloatArray(payload, action.Features);
                payload.Write(action.VisitCount);
            }
        }

        byte[] record = payloadStream.ToArray();
        writer.Write(record.Length);
        writer.Write(record);
        currentSampleCount++;
        TotalSamplesWritten++;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CompleteCurrentShard();
    }

    private void OpenNextShard()
    {
        currentPath = Path.Combine(outputDirectory, $"{prefix}-{shardIndex:D5}{FileExtension}");
        shardIndex++;
        FileStream file = new(currentPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        GZipStream gzip = new(file, CompressionLevel.Optimal);
        writer = new BinaryWriter(gzip, Encoding.UTF8);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(AIEncodingSchema.Version);
        writer.Write(AIEncodingSchema.StateFeatureCount);
        writer.Write(AIEncodingSchema.ActionFeatureCount);
        currentSampleCount = 0;
    }

    private void CompleteCurrentShard()
    {
        if (writer == null)
        {
            return;
        }

        writer.Write(0);
        writer.Dispose();
        writer = null;

        string checksum = ComputeSha256(currentPath);
        string manifestPath = currentPath + ".manifest.json";
        string manifest = "{\n"
            + $"  \"formatVersion\": {FormatVersion},\n"
            + $"  \"schemaVersion\": {AIEncodingSchema.Version},\n"
            + $"  \"stateFeatureCount\": {AIEncodingSchema.StateFeatureCount},\n"
            + $"  \"actionFeatureCount\": {AIEncodingSchema.ActionFeatureCount},\n"
            + $"  \"sampleCount\": {currentSampleCount},\n"
            + $"  \"sha256\": \"{checksum}\"\n"
            + "}\n";
        File.WriteAllText(manifestPath, manifest, new UTF8Encoding(false));
        completedShards.Add(currentPath);
        currentPath = null;
        currentSampleCount = 0;
    }

    private static void Validate(TrainingSample sample)
    {
        if (sample == null)
        {
            throw new ArgumentNullException(nameof(sample));
        }
        if (sample.ObserverPlayerIndex < 0)
        {
            throw new InvalidDataException("Observer player index must be non-negative.");
        }
        if (sample.StateFeatures == null || sample.StateFeatures.Length != AIEncodingSchema.StateFeatureCount)
        {
            throw new InvalidDataException($"State feature length must be {AIEncodingSchema.StateFeatureCount}.");
        }
        if (sample.Actions == null || sample.Actions.Count == 0)
        {
            throw new InvalidDataException("A training sample must contain at least one legal action.");
        }
        if (float.IsNaN(sample.Outcome) || float.IsInfinity(sample.Outcome) || sample.Outcome < -1f || sample.Outcome > 1f)
        {
            throw new InvalidDataException("Outcome must be finite and inside [-1, 1].");
        }

        foreach (float value in sample.StateFeatures)
        {
            EnsureFinite(value, "state feature");
        }
        foreach (TrainingActionLabel action in sample.Actions)
        {
            if (action == null || action.Features == null || action.Features.Length != AIEncodingSchema.ActionFeatureCount)
            {
                throw new InvalidDataException($"Action feature length must be {AIEncodingSchema.ActionFeatureCount}.");
            }
            if (action.VisitCount < 0)
            {
                throw new InvalidDataException("Action visit count cannot be negative.");
            }
            foreach (float value in action.Features)
            {
                EnsureFinite(value, "action feature");
            }
        }
    }

    private static void EnsureFinite(float value, string field)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new InvalidDataException($"Dataset {field} is not finite.");
        }
    }

    private static void WriteFloatArray(BinaryWriter destination, float[] values)
    {
        foreach (float value in values)
        {
            destination.Write(value);
        }
    }

    private static void WriteIntArray(BinaryWriter destination, int[] values)
    {
        values ??= Array.Empty<int>();
        destination.Write(values.Length);
        foreach (int value in values)
        {
            destination.Write(value);
        }
    }

    private static string SanitizePrefix(string value)
    {
        string result = string.IsNullOrWhiteSpace(value) ? "dataset" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalid, '_');
        }
        return result;
    }

    private static string ComputeSha256(string path)
    {
        using SHA256 sha256 = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        byte[] hash = sha256.ComputeHash(stream);
        StringBuilder builder = new(hash.Length * 2);
        foreach (byte value in hash)
        {
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
}

public static class DatasetShardReader
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ARKDS001");

    public static List<TrainingSample> ReadAll(string path)
    {
        List<TrainingSample> samples = new();
        using FileStream file = File.OpenRead(path);
        using GZipStream gzip = new(file, CompressionMode.Decompress);
        using BinaryReader reader = new(gzip, Encoding.UTF8);

        byte[] actualMagic = reader.ReadBytes(Magic.Length);
        if (actualMagic.Length != Magic.Length || !BytesEqual(actualMagic, Magic))
        {
            throw new InvalidDataException("Dataset magic is invalid.");
        }

        int formatVersion = reader.ReadInt32();
        int schemaVersion = reader.ReadInt32();
        int stateFeatureCount = reader.ReadInt32();
        int actionFeatureCount = reader.ReadInt32();
        if (formatVersion != DatasetShardWriter.FormatVersion
            || schemaVersion != AIEncodingSchema.Version
            || stateFeatureCount != AIEncodingSchema.StateFeatureCount
            || actionFeatureCount != AIEncodingSchema.ActionFeatureCount)
        {
            throw new InvalidDataException("Dataset header is incompatible with the current feature schema.");
        }

        while (true)
        {
            int recordLength = reader.ReadInt32();
            if (recordLength == 0)
            {
                break;
            }
            if (recordLength < 0 || recordLength > 64 * 1024 * 1024)
            {
                throw new InvalidDataException("Dataset record length is invalid.");
            }

            byte[] record = reader.ReadBytes(recordLength);
            if (record.Length != recordLength)
            {
                throw new EndOfStreamException("Dataset record was truncated.");
            }
            using MemoryStream recordStream = new(record, false);
            using BinaryReader payload = new(recordStream, Encoding.UTF8);
            TrainingSample sample = new()
            {
                Seed = payload.ReadInt64(),
                GameId = payload.ReadInt32(),
                Ply = payload.ReadInt32(),
                ObserverPlayerIndex = payload.ReadInt32(),
                Outcome = payload.ReadSingle(),
                FirstDeck = ReadIntArray(payload),
                SecondDeck = ReadIntArray(payload),
                StateFeatures = ReadFloatArray(payload, stateFeatureCount),
            };
            int actionCount = payload.ReadInt32();
            if (actionCount <= 0 || actionCount > 100000)
            {
                throw new InvalidDataException("Dataset action count is invalid.");
            }
            for (int action = 0; action < actionCount; action++)
            {
                sample.Actions.Add(new TrainingActionLabel
                {
                    Features = ReadFloatArray(payload, actionFeatureCount),
                    VisitCount = payload.ReadInt32(),
                });
            }
            if (recordStream.Position != recordStream.Length)
            {
                throw new InvalidDataException("Dataset record contains unexpected trailing bytes.");
            }
            samples.Add(sample);
        }

        return samples;
    }

    private static int[] ReadIntArray(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > 10000)
        {
            throw new InvalidDataException("Dataset integer array length is invalid.");
        }
        int[] result = new int[length];
        for (int index = 0; index < length; index++)
        {
            result[index] = reader.ReadInt32();
        }
        return result;
    }

    private static float[] ReadFloatArray(BinaryReader reader, int length)
    {
        float[] result = new float[length];
        for (int index = 0; index < length; index++)
        {
            result[index] = reader.ReadSingle();
        }
        return result;
    }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }
        return true;
    }
}
