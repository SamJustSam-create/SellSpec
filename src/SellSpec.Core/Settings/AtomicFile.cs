namespace SellSpec.Core.Settings;

/// <summary>
/// Writes a file so that readers only ever see the old or the new content. The text goes to a freshly created,
/// randomly named temporary file in the same folder (CreateNew refuses to follow a pre-planted link or to reuse a
/// predictable name) and is then moved over the destination. A failed write leaves nothing behind.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string text)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory ?? string.Empty, Path.GetFileName(path) + "." + Path.GetRandomFileName() + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(text);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
