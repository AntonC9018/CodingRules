using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace SealedClasses;

public abstract class AbstractClass { }
public static class StaticClass { }
public sealed class SealedClass { }
public abstract record AbstractRecord;
public sealed record SealedRecord;
public record struct ValueRecord;
public struct ValueType { }
public interface IContract { }
public delegate void Callback();

public class TypeAttribute : Attribute
{
    public TypeAttribute(Type type) { }
}
public sealed class TypeAttribute<T> : TypeAttribute
{
    public TypeAttribute() : base(typeof(T)) { }
}

public class PrimaryAttribute(Type type) : Attribute
{
    public Type Type { get; } = type;
}
public sealed class PrimaryAttribute<T>() : PrimaryAttribute(typeof(T));

[ComImport, Guid("1A894A19-2FCD-4F87-A5A2-83C64F9FA833")]
public class ImportedComClass { }

[SuppressMessage("Readability", "CR1400", Justification = "External framework requires a derivable class; contract reviewed manually.")]
public class ReviewedExternalConstraint { }

#pragma warning disable CR1400 // This integration deliberately supplies a framework base.
public class ReviewedExtensionPoint { }
#pragma warning restore CR1400
