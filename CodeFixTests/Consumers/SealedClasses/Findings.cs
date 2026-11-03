using System;
using System.Threading;
using System.Threading.Tasks;

namespace SealedClasses;

// A16: declaration and virtual seam reduced from Commands.Calendar.cs:23-33,
// ScheduleLib 12e601e7b62511bdecc44e280407fee12147b230. Dependency types are
// represented by local contracts; existing derived tests must not hide this finding.
public class CalendarCommands // CR1400: CalendarCommands
{
    protected virtual void ConfigureServices(IServiceCollection services) => AppConfiguration.ConfigureServices(services);
    protected virtual Task<ICalendarSyncProvider> Connect(IServiceProvider services, BuiltGoogleCalendarConfig config, CancellationToken token)
        => GoogleCalendarSyncProvider.Connect(services, config, token);
}

public sealed class CalendarCommandTests : CalendarCommands
{
    protected override void ConfigureServices(IServiceCollection services) { }
    protected override Task<ICalendarSyncProvider> Connect(IServiceProvider services, BuiltGoogleCalendarConfig config, CancellationToken token)
        => Task.FromResult<ICalendarSyncProvider>(new FakeProvider());
    private sealed class FakeProvider : ICalendarSyncProvider { }
}

public class Ordinary // CR1400: Ordinary
{
}
internal class GenericBase<T> // CR1400: GenericBase
{
}
internal sealed class GenericChild : GenericBase<int> { }
public record ConcreteRecord(int Value); // CR1400: ConcreteRecord
public class DomainException : Exception { } // CR1400: DomainException
public class WithVirtualMember { public virtual void Run() { } } // CR1400: WithVirtualMember
public class ImplementsExternalContract : IDisposable { public void Dispose() { } } // CR1400: ImplementsExternalContract

// Inheritance alone is not the attribute exception.
public class StandaloneAttribute : Attribute { } // CR1400: StandaloneAttribute
public class WrongTypeAttribute : Attribute // CR1400: WrongTypeAttribute
{
    public WrongTypeAttribute(Type type) { }
}
public sealed class WrongTypeAttribute<T> : WrongTypeAttribute
{
    public WrongTypeAttribute() : base(typeof(string)) { }
}

public interface IServiceCollection { }
public interface ICalendarSyncProvider { }
public sealed class BuiltGoogleCalendarConfig { }
public static class AppConfiguration
{
    public static void ConfigureServices(IServiceCollection services) { }
}
public static class GoogleCalendarSyncProvider
{
    public static Task<ICalendarSyncProvider> Connect(IServiceProvider services, BuiltGoogleCalendarConfig config, CancellationToken token)
        => throw new NotImplementedException();
}
