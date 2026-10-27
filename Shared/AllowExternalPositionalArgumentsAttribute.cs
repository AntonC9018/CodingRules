using System;

namespace CodingRules;

/// <summary>Registers one exact external API using its assembly name and XML declaration ID.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class AllowExternalPositionalArgumentsAttribute : Attribute
{
    public AllowExternalPositionalArgumentsAttribute(string assemblyName, string declarationId)
    {
        AssemblyName = assemblyName;
        DeclarationId = declarationId;
    }
    public string AssemblyName { get; }
    public string DeclarationId { get; }
}
