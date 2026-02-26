using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DesktopCapture;

class Program
{
    static readonly string targetIp = "127.0.0.1";
    static readonly string targetPort = "8825";


    static async Task Main()
    {
        await Capture.ProcessDbus();
        return;
        //string procArgs = $" -stream_loop -1 -i {file} -vcodec libx264 -preset ultrafast -tune zerolatency -f mpegts udp://{targetIp}:{targetPort}";

        Console.WriteLine($"Starting...");

        ProcessStartInfo gpuproc = new()
        {
            FileName = "sh",
            Arguments = "stream.sh",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        ProcessStartInfo ffproc = new()
        {
            FileName = "ffmpeg",
            //Arguments = $"-fflags nobuffer -analyzeduration 0 -probesize 32 -f matroska -i stream_pipe -vcodec libx264 -preset ultrafast -tune zerolatency -flush_packets 1 -f mpegts udp://{targetIp}:{targetPort}?pkt_size=1316",
            Arguments = $"-fflags nobuffer -analyzeduration 0 -probesize 32 -f matroska -i stream_pipe -c:v copy -bsf:v h264_mp4toannexb -f mpegts udp://{targetIp}:{targetPort}?pkt_size=1316",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        using Process ffmpegProcess = new() { StartInfo = ffproc };
        using Process gpuProcess = new() { StartInfo = gpuproc };

        gpuProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Console.WriteLine($"[GPU-REC] {e.Data}");
            }
        };

        gpuProcess.Start();

        Console.WriteLine("\nPress enter when ready...");
        Console.ReadLine();

        // Capture FFmpeg output so you can see what it's doing in your C# console
        ffmpegProcess.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Console.WriteLine($"[FFmpeg] {e.Data}");
            }
        };

        ffmpegProcess.Start();
        ffmpegProcess.BeginErrorReadLine();

        Console.WriteLine("\nStreaming started. Press [ENTER] to stop the stream...");
        Console.ReadLine();

        // 5. Clean up gracefully
        if (!ffmpegProcess.HasExited)
        {
            Console.WriteLine("Stopping FFmpeg...");
            ffmpegProcess.Kill();
            ffmpegProcess.WaitForExit();
            gpuProcess.Kill();
            gpuProcess.WaitForExit();
        }


        Console.WriteLine("Streaming stopped.");
    }
}