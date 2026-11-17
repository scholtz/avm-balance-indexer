using System.Reflection;

namespace AvmBalanceIndexer.Worker;

/// <summary>The version of this build as the image was built with it (<c>-p:InformationalVersion</c>), recorded with every writer term.</summary>
public static class BuildVersion
{
    /// <summary>The informational version of the worker assembly, or <c>unknown</c>.</summary>
    public static string Current { get; } =
        typeof(BuildVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { Length: > 0 } version ? version : "unknown";
}
