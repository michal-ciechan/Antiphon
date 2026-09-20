namespace Antiphon.SessionRunner;

public static class PhoneHomeStoreIdentity
{
    public static Guid LoadOrCreate(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        if (File.Exists(path)
            && Guid.TryParse(File.ReadAllText(path).Trim(), out var existing)
            && existing != Guid.Empty)
            return existing;

        var created = Guid.NewGuid();
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, created.ToString("D"));
        File.Move(tmp, path, overwrite: true);
        return created;
    }
}
