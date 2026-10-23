using System.Security.Cryptography;
using System.Text;
using KDesktop.Core;

namespace KDesktop.Assistant.Tools.Knowledge;

/// <summary>How a document of the index is: read with text, without any (a photo, a scanned PDF without the OCR), or unreadable.</summary>
public enum KnowledgeState : byte
{
    Ready,
    Empty,
    Failed,
}

/// <summary>What the index knows of a file, as written on disk: when it was read and its pieces.</summary>
/// <param name="Written">The file's last write time (UTC ticks) when it was read: another one, and it is read again.</param>
/// <param name="Chunker">How it was cut (<see cref="KnowledgeBase.Chunker"/>): another way, and it is read again.</param>
/// <param name="Model">The model of its vectors; empty: none (found only by its words).</param>
public sealed record KnowledgeRecord(string Path, long Size, long Written, string Chunker, string Model, KnowledgeState State, int Pages, bool Ocr,
    IReadOnlyList<KnowledgeStoredPiece> Pieces);

/// <summary>A piece of a document: its page, its text and its vector (null without one).</summary>
public sealed record KnowledgeStoredPiece(int Page, string Text, float[]? Vector);

/// <summary>
/// The index on disk (stage 19.1): a file per document in <c>&lt;data&gt;\Assistant\Knowledge</c>, named by its path, so a
/// changed document is one file written again and a gone one, one deleted. Vectors as half floats (256 → 512 bytes).
/// Written whole to a temporary file first: a crash never leaves half a document. Thread-safe (one file at a time each).
/// </summary>
public sealed class KnowledgeStore(string folder)
{
    private const int Magic = 0x314B444B; // «KDK1»

    public string Folder => folder;

    /// <summary>All the documents written, as they were read (with their texts), or skipped when broken.</summary>
    public IEnumerable<KnowledgeRecord> ReadAll()
    {
        if (!Directory.Exists(folder)) yield break;
        foreach (var file in Directory.EnumerateFiles(folder, "*.kdk"))
        {
            var record = Read(file);
            if (record is not null) yield return record;
            else TryDelete(file);
        }
    }

    /// <summary>The document of <paramref name="path"/>, or null when it is not written (or broken).</summary>
    public KnowledgeRecord? Find(string path) => File.Exists(FileOf(path)) ? Read(FileOf(path)) : null;

    public void Write(KnowledgeRecord record)
    {
        Directory.CreateDirectory(folder);
        var file = FileOf(record.Path);
        var temporary = file + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            writer.Write(Magic);
            writer.Write(record.Path);
            writer.Write(record.Size);
            writer.Write(record.Written);
            writer.Write(record.Chunker);
            writer.Write(record.Model);
            writer.Write((byte)record.State);
            writer.Write(record.Pages);
            writer.Write(record.Ocr);
            writer.Write(record.Pieces.Count);
            foreach (var piece in record.Pieces)
            {
                writer.Write(piece.Page);
                writer.Write(piece.Text);
                var vector = piece.Vector ?? [];
                writer.Write((ushort)vector.Length);
                foreach (var value in vector) writer.Write(BitConverter.HalfToUInt16Bits((Half)value));
            }
        }
        File.Move(temporary, file, overwrite: true);
    }

    public void Delete(string path) => TryDelete(FileOf(path));

    /// <summary>Forgets every document.</summary>
    public void Clear()
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder)) TryDelete(file);
    }

    /// <summary>What the index takes on disk.</summary>
    public long Bytes()
    {
        try { return Directory.Exists(folder) ? new DirectoryInfo(folder).EnumerateFiles("*.kdk").Sum(f => f.Length) : 0; }
        catch (IOException) { return 0; }
    }

    /// <summary>The file of a document: a hash of its path (any length, any letters).</summary>
    private string FileOf(string path) =>
        Path.Combine(folder, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..32] + ".kdk");

    private static KnowledgeRecord? Read(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadInt32() != Magic) return null;
            var path = reader.ReadString();
            var size = reader.ReadInt64();
            var written = reader.ReadInt64();
            var chunker = reader.ReadString();
            var model = reader.ReadString();
            var state = (KnowledgeState)reader.ReadByte();
            var pages = reader.ReadInt32();
            var ocr = reader.ReadBoolean();
            var count = reader.ReadInt32();
            if (count is < 0 or > 1_000_000) return null;
            var pieces = new List<KnowledgeStoredPiece>(count);
            for (var i = 0; i < count; i++)
            {
                var page = reader.ReadInt32();
                var text = reader.ReadString();
                var dimensions = reader.ReadUInt16();
                float[]? vector = null;
                if (dimensions > 0)
                {
                    vector = new float[dimensions];
                    for (var d = 0; d < dimensions; d++) vector[d] = (float)BitConverter.UInt16BitsToHalf(reader.ReadUInt16());
                }
                pieces.Add(new KnowledgeStoredPiece(page, text, vector));
            }
            return new KnowledgeRecord(path, size, written, chunker, model, state, pages, ocr, pieces);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException)
        {
            Log.Warn($"Asistente: un documento del índice está roto ({Path.GetFileName(file)}): {ex.Message}");
            return null;
        }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Asistente: no se ha podido borrar {file}: {ex.Message}"); }
    }
}
