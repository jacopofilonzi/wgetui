namespace WgetUi.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreNameIsSet() => Assert.Equal("wgetui", CoreInfo.Name);
}
