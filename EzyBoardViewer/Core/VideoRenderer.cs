using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using EzyBoardViewer.Models;
using SkiaSharp;

namespace EzyBoardViewer.Core
{
    /// <summary>
    /// 用 SkiaSharp 逐帧把每页绘制成位图，再以 rawvideo 管道喂给 FFmpeg 编码成 MP4。
    /// 录制回放（笔迹随时间出现 / 相机矩阵动画）为后续增强；当前版本为「每页静态若干秒」的幻灯片式视频。
    /// 需要本机安装 FFmpeg（且在 PATH 中，或放在常见目录）。
    /// </summary>
    public static class VideoRenderer
    {
        public static void Render(SuibianPackage pkg, string outputPath, VideoQuality quality = VideoQuality.Mid, int fps = 30, double secondsPerPage = 3)
        {
            var (longEdge, kbps) = quality switch
            {
                VideoQuality.Low => (960, 1500),
                VideoQuality.Mid => (1280, 3000),
                VideoQuality.High => (1920, 8000),
                _ => (1280, 3000)
            };
            if (pkg.Pages.Count == 0) throw new InvalidOperationException("没有可渲染的页");

            // 以第一页宽高比为视频基准尺寸
            var sizes = pkg.Pages.Select(p =>
            {
                double scale = longEdge / (double)Math.Max(p.Width, p.Height);
                return (w: (int)(p.Width * scale), h: (int)(p.Height * scale));
            }).ToList();
            int W = sizes[0].w - (sizes[0].w % 2);
            int H = sizes[0].h - (sizes[0].h % 2);
            if (W <= 0 || H <= 0) throw new InvalidOperationException("视频尺寸非法");

            string ffmpeg = FindFfmpeg();
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-y -f rawvideo -pix_fmt rgba -s {W}x{H} -framerate {fps} -i - -pix_fmt yuv420p -b:v {kbps}k -r {fps} \"{outputPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("无法启动 FFmpeg 进程");
            using (var stdin = new BinaryWriter(proc.StandardInput.BaseStream))
            {
                int frames = (int)(fps * secondsPerPage);
                for (int i = 0; i < pkg.Pages.Count; i++)
                {
                    var page = pkg.Pages[i];
                    int pw = sizes[i].w, ph = sizes[i].h;
                    using var bmp = SkiaRenderer.RenderToBitmap(page, pkg.Source, pw, ph);
                    using var target = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using (var cv = new SKCanvas(target))
                    {
                        cv.Clear(SKColors.White);
                        cv.DrawBitmap(bmp, new SKRect(0, 0, pw, ph), new SKRect(0, 0, W, H));
                    }
                    var span = target.GetPixelSpan();
                    stdin.Write(span.ToArray());
                }
            }

            if (!proc.WaitForExit(60000 * Math.Max(1, pkg.Pages.Count)) || proc.ExitCode != 0)
                throw new InvalidOperationException($"FFmpeg 编码失败（exit={proc.ExitCode}）。FFmpeg 需支持 rawvideo→yuv420p→mp4。");
        }

        private static string FindFfmpeg()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
                p?.WaitForExit(1000);
                return "ffmpeg";
            }
            catch { }

            foreach (var p in new[]
            {
                @"C:\ffmpeg\bin\ffmpeg.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"),
                "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg"
            })
            {
                if (File.Exists(p)) return p;
            }
            throw new InvalidOperationException("未找到 FFmpeg：请安装 FFmpeg 并加入 PATH，或放在 C:\\ffmpeg\\bin\\ffmpeg.exe 等常见路径。");
        }
    }
}
