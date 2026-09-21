namespace Antiphon.TestSupport;

public static class TestAppHostPath
{
    public static string Resolve(string name, string baseDirectory, bool windows, bool siblingProducer)
    {
        var file = windows ? name + ".exe" : name;
        var directory = siblingProducer ? Path.Combine(baseDirectory, name) : baseDirectory;
        return Path.Combine(directory, file);
    }

    public static string Require(string name, string baseDirectory, bool? windows = null, bool siblingProducer = true)
    {
        var path = Resolve(name, baseDirectory, windows ?? OperatingSystem.IsWindows(), siblingProducer);
        if (!File.Exists(path))
            throw new FileNotFoundException("App host was not found at the attempted path.", path);
        return path;
    }
}
