namespace Nexus.Tools.Standard;

internal static class PathSandbox
{
    public static string ResolvePath(string baseDirectory, string relativeOrAbsolutePath)
    {
        var basePath = Path.GetFullPath(baseDirectory);
        var combined = Path.IsPathRooted(relativeOrAbsolutePath)
            ? Path.GetFullPath(relativeOrAbsolutePath)
            : Path.GetFullPath(Path.Combine(basePath, relativeOrAbsolutePath));

        if (!combined.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The requested path escapes the configured tool sandbox.");

        return combined;
    }

    public static string ToRelativePath(string baseDirectory, string absolutePath)
        => Path.GetRelativePath(baseDirectory, absolutePath).Replace('\\', '/');
}