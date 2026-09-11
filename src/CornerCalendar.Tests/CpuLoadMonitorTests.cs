using CornerCalendar.Core.Helpers;
using Xunit;

namespace CornerCalendar.Tests;

public class CpuLoadMonitorTests
{
    [Theory]
    [InlineData(0f, 500)]
    [InlineData(5f, 500)]
    [InlineData(10f, 250)]
    [InlineData(50f, 50)]
    [InlineData(100f, 25)]
    public void 间隔按负载计算(float load, int expected)
        => Assert.Equal(expected, CpuLoadMonitor.CalculateInterval(load));

    [Fact]
    public void 越界负载被钳制()
    {
        // % Processor Utility 睿频时可超过 100，负值视为空闲
        Assert.Equal(25, CpuLoadMonitor.CalculateInterval(400f));
        Assert.Equal(500, CpuLoadMonitor.CalculateInterval(-10f));
    }

    [Fact]
    public void 间隔随负载单调不增()
    {
        int previous = int.MaxValue;
        for (float load = 0f; load <= 100f; load += 5f)
        {
            int interval = CpuLoadMonitor.CalculateInterval(load);
            Assert.True(interval <= previous);
            previous = interval;
        }
    }

    [Fact]
    public void 采样返回非负负载且可释放()
    {
        using CpuLoadMonitor monitor = new();

        float first = monitor.SampleLoad();
        float second = monitor.SampleLoad();

        Assert.True(first >= 0f);
        Assert.True(second >= 0f);
    }
}