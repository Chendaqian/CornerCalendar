using CornerCalendar.Core.Models;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CornerCalendar.Core.Helpers;

/// <summary>
/// 跑者资源库：枚举 Resources\Runners 下的跑者并构建托盘动画帧图标。
/// </summary>
/// <remark>
/// 帧图为剪影 PNG（含透明通道），支持 {name}_{i}.png 与 {name}-frame-{i}.png 两种命名，
/// 帧序按文件名末尾整数升序；深色主题下反白（保留 alpha、RGB 置白）。
/// PNG-in-ICO 封装与反白思路移植自 RunCat365（Apache-2.0，Copyright Takuto Nakamura），
/// 见 https://github.com/runcat-dev/RunCat365 ；
/// 跑者资源来源见 https://runcat-dev.github.io/RunnerGallery/ 。
/// </remark>
internal static class RunnerLibrary
{
    /// <summary>
    /// 默认跑者名（设置缺失或已保存跑者不存在时的回退值）。
    /// </summary>
    internal const string DefaultRunnerName = "cat";

    /// <summary>
    /// 跑者资源根目录（程序目录下 Resources\Runners）。
    /// </summary>
    internal static string GetRunnersDirectory()
        => Path.Combine(AppContext.BaseDirectory, "Resources", "Runners");

    /// <summary>
    /// 枚举目录下的全部跑者，按文件夹名忽略大小写排序。
    /// </summary>
    /// <remark>
    /// 每个含有效 PNG 帧的子文件夹视为一个跑者；空文件夹跳过。
    /// runnersDirectory 为空时使用 <see cref="GetRunnersDirectory"/>。
    /// </remark>
    internal static IReadOnlyList<RunnerInfo> EnumerateRunners(string? runnersDirectory = null)
    {
        string directory = runnersDirectory ?? GetRunnersDirectory();
        if (!Directory.Exists(directory))
            return Array.Empty<RunnerInfo>();

        string[] folders = Directory.GetDirectories(directory);
        List<RunnerInfo> runners = new(folders.Length);
        foreach (string folder in folders)
        {
            string name = Path.GetFileName(folder);
            List<string> framePaths = EnumerateFrameFiles(folder);
            if (framePaths.Count == 0)
                continue;   // 没有任何有效帧的目录不作为跑者

            runners.Add(new RunnerInfo(name, ToDisplayName(name), framePaths));
        }

        runners.Sort((left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return runners;
    }

    /// <summary>
    /// 按名称查找跑者（忽略大小写），未找到返回 null。
    /// </summary>
    internal static RunnerInfo? FindRunner(IReadOnlyList<RunnerInfo> runners, string? name)
        => runners.FirstOrDefault(runner =>
            string.Equals(runner.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 枚举文件夹内 PNG 帧并按文件名末尾整数升序排序。
    /// </summary>
    internal static List<string> EnumerateFrameFiles(string folder)
        => Directory.GetFiles(folder, "*.png")
            .OrderBy(path => GetFrameOrder(Path.GetFileNameWithoutExtension(path)))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// 取文件名末尾连续数字作为帧序号，无数字后缀时排在最后。
    /// </summary>
    internal static int GetFrameOrder(string fileNameWithoutExtension)
    {
        int end = fileNameWithoutExtension.Length;
        int start = end;
        while (start > 0 && char.IsAsciiDigit(fileNameWithoutExtension[start - 1]))
            start--;

        if (start == end)
            return int.MaxValue;

        return int.Parse(fileNameWithoutExtension[start..end], CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 文件夹名转可读显示名：去 -frames 后缀、分隔符转空格、逐词首字母大写。
    /// </summary>
    internal static string ToDisplayName(string folderName)
    {
        string name = folderName.EndsWith("-frames", StringComparison.OrdinalIgnoreCase)
            ? folderName[..^"-frames".Length]
            : folderName;
        string[] words = name.Split(
            new[] { '-', '_', ' ' },
            StringSplitOptions.RemoveEmptyEntries);
        StringBuilder builder = new(name.Length);
        foreach (string word in words)
        {
            if (builder.Length > 0)
                builder.Append(' ');
            builder.Append(char.ToUpperInvariant(word[0]));
            if (word.Length > 1)
                builder.Append(word[1..]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 加载跑者全部帧图标，损坏帧自动跳过；调用方负责释放返回的每个 Icon。
    /// </summary>
    /// <remark>
    /// recolorWhite 为 true 时帧被反白（深色主题用）。
    /// </remark>
    internal static IReadOnlyList<Icon> LoadRunnerIcons(RunnerInfo runner, bool recolorWhite)
    {
        List<Icon> icons = new(runner.FramePaths.Count);
        foreach (string path in runner.FramePaths)
        {
            if (!TryBuildFramePng(path, recolorWhite, out byte[] pngBytes, out int width, out int height))
                continue;   // 损坏帧跳过，不影响其余帧

            Icon? icon = ToIcon(pngBytes, width, height);
            if (icon != null)
                icons.Add(icon);
        }

        return icons;
    }

    /// <summary>
    /// 读取帧 PNG 字节并可选反白，返回 PNG 数据与像素尺寸。
    /// </summary>
    /// <remark>
    /// 反白走 WPF 解码管线（Bgra32 逐像素保留 alpha、RGB 置白后重新编码）；
    /// 不反白时直接返回原始文件字节。文件缺失或无法解码时返回 false。
    /// </remark>
    internal static bool TryBuildFramePng(
        string framePath,
        bool recolorWhite,
        out byte[] pngBytes,
        out int width,
        out int height)
    {
        pngBytes = Array.Empty<byte>();
        width = 0;
        height = 0;
        try
        {
            if (!recolorWhite)
            {
                byte[] original = File.ReadAllBytes(framePath);
                if (!TryReadPngSize(original, out width, out height))
                    return false;

                pngBytes = original;
                return true;
            }

            using FileStream stream = File.OpenRead(framePath);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            FormatConvertedBitmap converted = new(
                decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            width = converted.PixelWidth;
            height = converted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);
            for (int offset = 0; offset < pixels.Length; offset += 4)
            {
                // Bgra32：保留 alpha，RGB 置白
                pixels[offset] = 255;
                pixels[offset + 1] = 255;
                pixels[offset + 2] = 255;
            }

            BitmapSource recolored = BitmapSource.Create(
                width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(recolored));
            using MemoryStream output = new();
            encoder.Save(output);
            pngBytes = output.ToArray();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: 跑者帧 '{framePath}' 构建失败：{ex.Message}");
            pngBytes = Array.Empty<byte>();
            width = 0;
            height = 0;
            return false;
        }
    }

    /// <summary>
    /// 从 PNG 字节的 IHDR 块解析像素尺寸（大端）。
    /// </summary>
    internal static bool TryReadPngSize(byte[] pngBytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        // 8 字节 PNG 签名 + 4 字节块长度 + "IHDR" 后跟大端宽高各 4 字节
        if (pngBytes.Length < 24
            || pngBytes[0] != 0x89 || pngBytes[1] != (byte)'P'
            || pngBytes[2] != (byte)'N' || pngBytes[3] != (byte)'G'
            || pngBytes[12] != (byte)'I' || pngBytes[13] != (byte)'H'
            || pngBytes[14] != (byte)'D' || pngBytes[15] != (byte)'R')
            return false;

        width = (pngBytes[16] << 24) | (pngBytes[17] << 16) | (pngBytes[18] << 8) | pngBytes[19];
        height = (pngBytes[20] << 24) | (pngBytes[21] << 16) | (pngBytes[22] << 8) | pngBytes[23];
        return width > 0 && height > 0;
    }

    /// <summary>
    /// 把 PNG 字节封装为单帧 ICO（PNG-in-ICO，Vista+），避免位图转换质量损失。
    /// </summary>
    /// <remark>
    /// 移植自 RunCat365 BitmapExtension.ToIcon（Apache-2.0，Copyright Takuto Nakamura），
    /// 见 https://github.com/runcat-dev/RunCat365 。宽高不小于 256 时目录项写 0。
    /// </remark>
    internal static Icon? ToIcon(byte[] pngBytes, int width, int height)
    {
        if (pngBytes.Length == 0 || width <= 0 || height <= 0)
            return null;

        try
        {
            using MemoryStream icoStream = new(22 + pngBytes.Length);
            using BinaryWriter writer = new(icoStream, Encoding.UTF8, leaveOpen: true);
            // ICONDIR：保留、类型=图标、数量=1
            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)1);
            // ICONDIRENTRY：宽、高、颜色数、保留、平面、位深、数据大小、数据偏移
            writer.Write((byte)(width >= 256 ? 0 : width));
            writer.Write((byte)(height >= 256 ? 0 : height));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(pngBytes.Length);
            writer.Write(22);
            writer.Write(pngBytes);
            icoStream.Position = 0;
            return new Icon(icoStream);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CornerCalendar: 跑者图标封装失败：{ex.Message}");
            return null;
        }
    }
}