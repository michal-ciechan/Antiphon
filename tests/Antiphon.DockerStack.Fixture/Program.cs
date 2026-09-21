namespace Antiphon.DockerStack.Fixture;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
            return 2;
        if (string.Equals(args[0], "observe", StringComparison.Ordinal))
            return Observe(args);
        if (string.Equals(args[0], "serve", StringComparison.Ordinal))
            return Serve(args);
        Console.Error.WriteLine("unknown mode");
        return 2;
    }

    public static int Observe(string[] args)
    {
        if (!TryParseObserve(args, out var identityPath, out var error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        if (!File.Exists(identityPath))
        {
            Console.Error.WriteLine("identity file is required");
            return 2;
        }

        Console.WriteLine("observe");
        return 0;
    }

    public static bool TryParseObserve(string[] args, out string identityPath, out string error)
    {
        identityPath = "";
        error = "";
        for (var i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--connection-string", StringComparison.OrdinalIgnoreCase))
            {
                error = "argument rejected";
                return false;
            }

            if (string.Equals(args[i], "--identity", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                identityPath = args[++i];
                continue;
            }

            error = "argument rejected";
            return false;
        }

        if (identityPath.Length == 0)
        {
            error = "identity file is required";
            return false;
        }

        return true;
    }

    public static int Serve(string[] args)
    {
        Console.WriteLine("serve");
        return 0;
    }
}
