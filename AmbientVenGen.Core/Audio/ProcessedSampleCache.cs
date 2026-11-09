using System.Security.Cryptography;
using System.Text;

namespace AmbientVenGen.Core.Audio;

/// <summary>
/// Keeps the results of the heavy sound processing (Paulstretch) on disk, so that a project opens without calculating
/// them again. The file is found by the recording (path, size, change time) and the settings. The folder is kept
/// below a size limit: the oldest results are deleted first.
/// </summary>
internal static class ProcessedSampleCache
{
    private const int Version = 1;                    // change when the algorithm changes
    private const long LimitBytes = 3L << 30;         // 3 GB
    private const long TrimToBytes = 2L << 30;        // 2 GB

    private static readonly string Directory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AmbientForge", "cache");

    public static string KeyFor(string path, double stretch, double pitch)
    {
        try
        {
            var info = new FileInfo(path);
            string text = FormattableString.Invariant(
                $"{Version}|{System.IO.Path.GetFullPath(path).ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{stretch:0.####}|{pitch:0.####}");

            return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)));
        }
        catch
        {
            return string.Empty;
        }
    }

    public static DecodedSample? TryLoad(string key)
    {
        if (key.Length == 0)
            return null;

        try
        {
            string file = System.IO.Path.Combine(Directory, key + ".f32");
            if (!File.Exists(file))
                return null;

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            if (reader.ReadInt32() != Version)
                return null;

            int frames = reader.ReadInt32();
            if (frames < 2 || stream.Length != 8 + frames * 8L)
                return null;

            return new DecodedSample(ReadFloats(reader, frames), ReadFloats(reader, frames));
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string key, DecodedSample sample)
    {
        if (key.Length == 0)
            return;

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            string file = System.IO.Path.Combine(Directory, key + ".f32");
            string temporary = file + ".tmp";

            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Version);
                writer.Write(sample.Frames);
                WriteFloats(writer, sample.Left);
                WriteFloats(writer, sample.Right);
            }

            File.Move(temporary, file, overwrite: true);
            Trim();
        }
        catch
        {
            // the cache is only a speed-up: a full disk or a locked file must not break anything
        }
    }

    private static void Trim()
    {
        var files = new DirectoryInfo(Directory).GetFiles("*.f32").OrderBy(f => f.LastWriteTimeUtc).ToList();
        long total = files.Sum(f => f.Length);

        if (total <= LimitBytes)
            return;

        foreach (FileInfo file in files)
        {
            if (total <= TrimToBytes)
                break;

            total -= file.Length;
            file.Delete();
        }
    }

    private static float[] ReadFloats(BinaryReader reader, int count)
    {
        var data = new float[count];
        var bytes = new byte[Math.Min(count, 1 << 16) * 4];

        for (int done = 0; done < count;)
        {
            int block = Math.Min(count - done, 1 << 16);
            reader.BaseStream.ReadExactly(bytes, 0, block * 4);
            Buffer.BlockCopy(bytes, 0, data, done * 4, block * 4);
            done += block;
        }

        return data;
    }

    private static void WriteFloats(BinaryWriter writer, float[] data)
    {
        var bytes = new byte[Math.Min(data.Length, 1 << 16) * 4];

        for (int done = 0; done < data.Length;)
        {
            int block = Math.Min(data.Length - done, 1 << 16);
            Buffer.BlockCopy(data, done * 4, bytes, 0, block * 4);
            writer.Write(bytes, 0, block * 4);
            done += block;
        }
    }
}
