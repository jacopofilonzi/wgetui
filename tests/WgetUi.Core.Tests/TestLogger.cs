using Microsoft.Extensions.Logging.Abstractions;

namespace WgetUi.Core.Tests;

internal static class TestLogger
{
    public static Microsoft.Extensions.Logging.ILogger<T> For<T>() => NullLogger<T>.Instance;
}
