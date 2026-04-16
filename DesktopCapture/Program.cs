using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DesktopCapture;

class Program
{
    static readonly string targetIp = "127.0.0.1";
    static readonly string targetPort = "8255";

    static async Task Main()
    {
        await Capture.ProcessDbus(targetIp, targetPort);
        return;
    }
}