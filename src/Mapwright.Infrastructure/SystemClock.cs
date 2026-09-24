using Mapwright.Application;

namespace Mapwright.Infrastructure;

public sealed class SystemClock : IApplicationClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
