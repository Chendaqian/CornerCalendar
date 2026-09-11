using System.Diagnostics;

namespace CornerCalendar.Core.Helpers;

/// <summary>
/// CPU 负载采样器：为托盘跑者动画提供动态帧速依据。
/// </summary>
/// <remark>
/// 计数器优先 Processor Information / % Processor Utility，失败回退 Processor / % Processor Time，
/// 双失败时 <see cref="SampleLoad"/> 恒为 0（动画退回固定最慢帧速）。
/// 采样取近 5 次均值平滑；间隔算法移植自 RunCat365（Apache-2.0，Copyright Takuto Nakamura），
/// 见 https://github.com/runcat-dev/RunCat365 。
/// </remark>
internal sealed class CpuLoadMonitor : IDisposable
{
    private const int SampleWindowSize = 5;

    private readonly PerformanceCounter? _counter;
    private readonly Queue<float> _samples = new(SampleWindowSize);

    internal CpuLoadMonitor()
    {
        _counter = CreateCounter("Processor Information", "% Processor Utility")
            ?? CreateCounter("Processor", "% Processor Time");
    }

    /// <summary>
    /// 采样一次 CPU 总负载（0–100，% Processor Utility 可能因睿频超过 100），返回近 5 次均值。
    /// </summary>
    internal float SampleLoad()
    {
        if (_counter == null)
            return 0f;

        try
        {
            _samples.Enqueue(_counter.NextValue());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: CPU 采样失败：{ex.Message}");
            return 0f;
        }

        while (_samples.Count > SampleWindowSize)
            _samples.Dequeue();
        return _samples.Average();
    }

    /// <summary>
    /// 由负载计算动画帧间隔（毫秒）：空闲 500ms/帧，负载越高越快，钳制在 25–500ms（约 40fps 上限）。
    /// </summary>
    internal static int CalculateInterval(float load)
    {
        float speed = Math.Max(1f, load / 5f);
        return (int)Math.Clamp(500f / speed, 25f, 500f);
    }

    private static PerformanceCounter? CreateCounter(string category, string counter)
    {
        try
        {
            PerformanceCounter created = new(category, counter, "_Total", readOnly: true);
            created.NextValue();  // 预热：首次读取固定为 0，不计入采样
            return created;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: 计数器 {category}\\{counter} 不可用：{ex.Message}");
            return null;
        }
    }

    public void Dispose()
        => _counter?.Dispose();
}