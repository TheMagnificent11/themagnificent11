using System.Reflection;

namespace TheMagnificent11.Site;

public static class MarkDownHelper
{
    public static string GetMarkdownFromEmbeddedResource(string namespaceWithinAssembly)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"{assembly.GetName().Name}.{namespaceWithinAssembly}";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        using var reader = new StreamReader(stream ?? throw new InvalidOperationException());

        return reader.ReadToEnd();
    }
}
