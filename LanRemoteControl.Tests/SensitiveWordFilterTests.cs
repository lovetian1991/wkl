using LanRemoteControl.Shared;

namespace LanRemoteControl.Tests;

public class SensitiveWordFilterTests
{
    [Theory]
    [InlineData("RemoteDesktop", true)]
    [InlineData("ScreenCapture", true)]
    [InlineData("SpyAgent", true)]
    [InlineData("SystemMonitor", true)]
    [InlineData("KeyLogger", true)]
    [InlineData("ControlPanel", true)]
    [InlineData("REMOTE_SERVICE", true)]
    [InlineData("wkl", false)]
    [InlineData("svchost", false)]
    [InlineData("explorer", false)]
    [InlineData("", false)]
    public void ContainsSensitiveProcessName_DetectsCorrectly(string name, bool expected)
    {
        Assert.Equal(expected, SensitiveWordFilter.ContainsSensitiveProcessName(name));
    }

    [Fact]
    public void ContainsSensitiveProcessName_NullInput_ReturnsFalse()
    {
        Assert.False(SensitiveWordFilter.ContainsSensitiveProcessName(null!));
    }

    [Fact]
    public void ContainsSensitiveProcessName_CaseInsensitive()
    {
        Assert.True(SensitiveWordFilter.ContainsSensitiveProcessName("REMOTE"));
        Assert.True(SensitiveWordFilter.ContainsSensitiveProcessName("Remote"));
        Assert.True(SensitiveWordFilter.ContainsSensitiveProcessName("remote"));
    }

    [Fact]
    public void FilterLogMessage_RemovesSensitivePhrases()
    {
        string input = "Starting remote control session";
        string result = SensitiveWordFilter.FilterLogMessage(input);
        Assert.DoesNotContain("remote control", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("***", result);
    }

    [Fact]
    public void FilterLogMessage_RemovesDesktopCapture()
    {
        string input = "Initializing desktop capture module";
        string result = SensitiveWordFilter.FilterLogMessage(input);
        Assert.DoesNotContain("desktop capture", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FilterLogMessage_RemovesScreenSpy()
    {
        string input = "Screen spy detected";
        string result = SensitiveWordFilter.FilterLogMessage(input);
        Assert.DoesNotContain("screen spy", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FilterLogMessage_RemovesInputSimulation()
    {
        string input = "Input simulation started";
        string result = SensitiveWordFilter.FilterLogMessage(input);
        Assert.DoesNotContain("input simulation", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FilterLogMessage_CaseInsensitive()
    {
        string input = "REMOTE CONTROL is active";
        string result = SensitiveWordFilter.FilterLogMessage(input);
        Assert.DoesNotContain("remote control", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FilterLogMessage_MultiplePhrases()
    {
        string input = "remote control with desktop capture and input simulation";
        string result = SensitiveWordFilter.FilterLogMessage(input);
        Assert.DoesNotContain("remote control", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("desktop capture", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("input simulation", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FilterLogMessage_NoSensitivePhrases_ReturnsUnchanged()
    {
        string input = "Service wkl started successfully";
        string result = SensitiveWordFilter.FilterLogMessage(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void FilterLogMessage_EmptyString_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SensitiveWordFilter.FilterLogMessage(string.Empty));
    }

    [Fact]
    public void FilterLogMessage_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SensitiveWordFilter.FilterLogMessage(null!));
    }
}
