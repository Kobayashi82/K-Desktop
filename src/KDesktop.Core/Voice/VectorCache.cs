using System.Text;

namespace KDesktop.Core.Voice;

/// <summary>
/// The vectors of texts that are asked for again and again (command names, the example phrases of the assistant's
/// tools), kept on disk next to the model so only new texts cost anything. The header names the model's weights and
/// the prompt: another model or prompt starts afresh. Not thread-safe: the owner locks.
/// </summary>
public sealed class VectorCache(string file, string magic)
{
    private Dictionary<string, float[]>? _items;

    public bool Dirty { get; private set; }

    public int Count => Items.Count;

    private Dictionary<string, float[]> Items => _items ??= Load();

    public bool TryGet(string text, out float[] vector) => Items.TryGetValue(text, out vector!);

    public void Put(string text, float[] vector)
    {
        Items[text] = vector;
        Dirty = true;
    }

    /// <summary>Forgets everything (on disk at the next <see cref="Save"/>).</summary>
    public void Clear()
    {
        Items.Clear();
        Dirty = true;
    }

    private string Header => $"{magic}|{ModelDownloader.Files.First(f => f.Path == VoiceModels.EmbeddingWeights).Sha256}|{TextEmbedder.Prompt}";

    private Dictionary<string, float[]> Load()
    {
        var cache = new Dictionary<string, float[]>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(file)) return cache;
            using var reader = new BinaryReader(File.OpenRead(file), Encoding.UTF8);
            if (reader.ReadString() != Header) return cache;
            var count = reader.ReadInt32();
            for (var i = 0; i < count; i++)
            {
                var text = reader.ReadString();
                var vector = new float[TextEmbedder.Size];
                for (var j = 0; j < vector.Length; j++) vector[j] = reader.ReadSingle();
                cache[text] = vector;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo leer la caché del modelo de significado ({Path.GetFileName(file)}): {ex.Message}");
            cache.Clear();
        }
        return cache;
    }

    /// <summary>Writes the cache, keeping only <paramref name="keep"/> when given (renamed or deleted commands leave it).</summary>
    public void Save(IReadOnlySet<string>? keep = null)
    {
        try
        {
            if (keep is not null)
                foreach (var old in Items.Keys.Where(k => !keep.Contains(k)).ToList()) Items.Remove(old);
            var temp = file + ".tmp";
            using (var writer = new BinaryWriter(File.Create(temp), Encoding.UTF8))
            {
                writer.Write(Header);
                writer.Write(Items.Count);
                foreach (var (text, vector) in Items)
                {
                    writer.Write(text);
                    foreach (var x in vector) writer.Write(x);
                }
            }
            File.Move(temp, file, overwrite: true);
            Dirty = false;
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo guardar la caché del modelo de significado ({Path.GetFileName(file)}): {ex.Message}");
        }
    }
}
