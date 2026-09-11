namespace CornerCalendar.Core.Models;

/// <summary>
/// 跑者描述符：文件夹名、显示名与按帧序排列的帧文件路径。
/// </summary>
/// <remark>
/// Name 为持久化到设置中的跑者标识（Resources\Runners 下的文件夹名）；
/// FramePaths 已按文件名末尾整数升序排序，枚举与排序逻辑见 RunnerLibrary。
/// 跑者动画与资源来源见 https://runcat-dev.github.io/RunnerGallery/ 。
/// </remark>
public sealed record RunnerInfo(
    string Name,
    string DisplayName,
    IReadOnlyList<string> FramePaths);