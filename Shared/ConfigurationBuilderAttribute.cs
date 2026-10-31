using System;

namespace CodingRules;

/// <summary>Marks a type as a configuration builder for CR1001.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ConfigurationBuilderAttribute : Attribute
{
    public ConfigurationBuilderAttribute() { }
}
