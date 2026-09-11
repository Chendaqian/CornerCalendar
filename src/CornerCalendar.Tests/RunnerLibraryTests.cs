using CornerCalendar.Core.Helpers;
using CornerCalendar.Core.Models;
using System.Drawing;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace CornerCalendar.Tests;

public class RunnerLibraryTests
{
    [Fact]
    public void 枚举全部分发跑者与帧数()
    {
        IReadOnlyList<RunnerInfo> runners = RunnerLibrary.EnumerateRunners();

        Assert.Equal(40, runners.Count);
        Assert.Equal(331, runners.Sum(runner => runner.FramePaths.Count));
        Assert.Contains(runners, runner => runner.Name == "cat");
        Assert.Contains(runners, runner => runner.Name == "welsh-corgi-frames");
        Assert.All(runners, runner => Assert.True(runner.FramePaths.Count > 0));
    }

    [Fact]
    public void 帧按末尾数字排序且兼容两种命名()
    {
        string root = CreateTempDirectory();
        try
        {
            string gallery = Path.Combine(root, "test-frames");
            Directory.CreateDirectory(gallery);
            foreach (string suffix in new[] { "10", "2", "0", "1" })
                WritePng(Path.Combine(gallery, $"test-frame-{suffix}.png"), 4, 4);

            string builtin = Path.Combine(root, "cat");
            Directory.CreateDirectory(builtin);
            foreach (string suffix in new[] { "2", "10", "0", "1" })
                WritePng(Path.Combine(builtin, $"cat_{suffix}.png"), 4, 4);

            IReadOnlyList<RunnerInfo> runners = RunnerLibrary.EnumerateRunners(root);

            Assert.Equal(2, runners.Count);
            RunnerInfo cat = Assert.Single(
                runners, runner => runner.Name == "cat");
            Assert.Equal(
                new[] { "cat_0.png", "cat_1.png", "cat_2.png", "cat_10.png" },
                cat.FramePaths.Select(Path.GetFileName).ToArray());

            RunnerInfo test = Assert.Single(
                runners, runner => runner.Name == "test-frames");
            Assert.Equal(
                new[]
                {
                    "test-frame-0.png",
                    "test-frame-1.png",
                    "test-frame-2.png",
                    "test-frame-10.png"
                },
                test.FramePaths.Select(Path.GetFileName).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 显示名去掉frames后缀并逐词大写()
    {
        Assert.Equal("Cat", RunnerLibrary.ToDisplayName("cat"));
        Assert.Equal("Horse", RunnerLibrary.ToDisplayName("horse"));
        Assert.Equal("Welsh Corgi", RunnerLibrary.ToDisplayName("welsh-corgi-frames"));
        Assert.Equal(
            "Jack Russell Terrier",
            RunnerLibrary.ToDisplayName("jack-russell-terrier-frames"));
    }

    [Fact]
    public void 损坏帧跳过且不影响其余帧()
    {
        string root = CreateTempDirectory();
        try
        {
            string folder = Path.Combine(root, "broken-frames");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(
                Path.Combine(folder, "broken-frame-0.png"), new byte[] { 1, 2, 3 });
            WritePng(Path.Combine(folder, "broken-frame-1.png"), 6, 4);
            File.WriteAllText(
                Path.Combine(folder, "broken-frame-2.png"), "这不是 PNG");

            IReadOnlyList<RunnerInfo> runners = RunnerLibrary.EnumerateRunners(root);
            RunnerInfo runner = Assert.Single(runners);

            IReadOnlyList<Icon> icons = RunnerLibrary.LoadRunnerIcons(runner, recolorWhite: false);
            Icon icon = Assert.Single(icons);
            Assert.Equal(6, icon.Width);
            Assert.Equal(4, icon.Height);
            foreach (Icon loaded in icons)
                loaded.Dispose();

            // 反白路径走 WPF 解码，损坏帧同样跳过
            IReadOnlyList<Icon> recolored = RunnerLibrary.LoadRunnerIcons(runner, recolorWhite: true);
            Assert.Single(recolored);
            foreach (Icon loaded in recolored)
                loaded.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 反白保留alpha且RGB置白()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "pixel.png");
            // 两个像素：半透明黑 + 不透明红（Bgra32 顺序）
            WritePng(path, 2, 1, new byte[]
            {
                0, 0, 0, 128,
                0, 0, 255, 255
            });

            bool built = RunnerLibrary.TryBuildFramePng(
                path, recolorWhite: true, out byte[] pngBytes, out int width, out int height);

            Assert.True(built);
            Assert.Equal(2, width);
            Assert.Equal(1, height);

            byte[] pixels = DecodeBgra32Pixels(pngBytes, out int decodedWidth);
            Assert.Equal(2, decodedWidth);
            Assert.Equal(new byte[]
            {
                255, 255, 255, 128,   // alpha 保留，RGB 置白
                255, 255, 255, 255
            }, pixels);

            // 不反白时返回原始文件字节
            bool original = RunnerLibrary.TryBuildFramePng(
                path, recolorWhite: false, out byte[] originalBytes, out _, out _);
            Assert.True(original);
            Assert.Equal(File.ReadAllBytes(path), originalBytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 真实跑者帧产出有效图标()
    {
        IReadOnlyList<RunnerInfo> runners = RunnerLibrary.EnumerateRunners();

        // 内置 32x32 正方形帧
        RunnerInfo cat = Assert.Single(runners, runner => runner.Name == "cat");
        IReadOnlyList<Icon> catIcons = RunnerLibrary.LoadRunnerIcons(cat, recolorWhite: false);
        Assert.Equal(cat.FramePaths.Count, catIcons.Count);
        Assert.All(catIcons, icon => Assert.Equal(32, icon.Width));
        foreach (Icon icon in catIcons)
            icon.Dispose();

        // 画廊非正方形帧（beagle 首帧 60x36），反白路径同样全部成功
        RunnerInfo beagle = Assert.Single(runners, runner => runner.Name == "beagle-frames");
        IReadOnlyList<Icon> beagleIcons = RunnerLibrary.LoadRunnerIcons(beagle, recolorWhite: true);
        Assert.Equal(beagle.FramePaths.Count, beagleIcons.Count);
        Assert.Equal(60, beagleIcons[0].Width);
        Assert.Equal(36, beagleIcons[0].Height);
        foreach (Icon icon in beagleIcons)
            icon.Dispose();
    }

    [Fact]
    public void PngInIco封装产出可读图标()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "frame.png");
            WritePng(path, 60, 36);
            byte[] pngBytes = File.ReadAllBytes(path);

            Assert.True(RunnerLibrary.TryReadPngSize(pngBytes, out int width, out int height));
            Assert.Equal(60, width);
            Assert.Equal(36, height);

            using Icon? icon = RunnerLibrary.ToIcon(pngBytes, width, height);

            Assert.NotNull(icon);
            Assert.Equal(60, icon!.Width);
            Assert.Equal(36, icon.Height);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] DecodeBgra32Pixels(byte[] pngBytes, out int width)
    {
        using MemoryStream stream = new(pngBytes);
        BitmapDecoder decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        FormatConvertedBitmap converted = new(
            decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        width = converted.PixelWidth;
        byte[] pixels = new byte[width * 4 * converted.PixelHeight];
        converted.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "CornerCalendarTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WritePng(string path, int width, int height, byte[]? bgraPixels = null)
    {
        byte[] pixels = bgraPixels ?? new byte[width * height * 4];
        BitmapSource source = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }
}