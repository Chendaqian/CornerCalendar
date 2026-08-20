using System.IO;

namespace CornerCalendar.Core.Helpers;

/// <summary>
/// 极简文件日志（按项目约定不引入日志框架）：
/// 错误信息追加写入 %LOCALAPPDATA%\CornerCalendar\error.log，超过 512KB 时重写，避免无限增长。
/// </summary>
public static class ErrorLog
{
    private static readonly object Sync = new();

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CornerCalendar");

    private static readonly string LogPath = Path.Combine(LogDir, "error.log");
    private const long MaxBytes = 512 * 1024;

    /// <summary>
    /// 追加一条错误记录。日志写入失败时静默忽略 —— 日志绝不能影响主流程。
    /// </summary>
    public static void Write(string source, Exception exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDir);

                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                    File.Delete(LogPath);

                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }
}