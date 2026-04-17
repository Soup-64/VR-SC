using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

public partial class TextureRect2 : TextureRect
{
    private TcpClient _tcpClient;
    private CancellationTokenSource _cts;
    private ImageTexture _imageTexture;
    private string _senderIp = "127.0.0.1";
    private int _senderPort = 8255;
    private List<byte> _frameBuffer;

    private int width = 1920, height = 1200;

    public override void _Ready()
    {
        //texture init
        Image emptyImg = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);
        _imageTexture = ImageTexture.CreateFromImage(emptyImg);
        this.Texture = _imageTexture;

        //run decode work async, do not block frames on VR!
        GD.Print("starting async frame handler...");
        Task.Run(ReceiveStream);
    }

    private async Task ReceiveStream()
    {
        _tcpClient = new TcpClient();
        GD.Print($"\tconnecting to {_senderIp}:{_senderPort}...");
        try
        {
            _tcpClient.Connect(_senderIp, _senderPort);
        }
        catch (Exception e)
        {
            GD.PrintErr($"\tconnect error: {e.Message}");
        }

        using NetworkStream stream = _tcpClient.GetStream();

        //4MB buffer, more than we ever really need
        _frameBuffer = new List<byte>(4000000);
        byte[] readBuffer = new byte[65536];
        bool capturing = false;

        while (_tcpClient.Connected)
        {
            int bytesRead = 0;
            try
            {
                bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
            }
            catch (Exception e)
            {
                GD.PrintErr($"\tstream read error: {e.Message}");
            }
            if (bytesRead == 0) break;

            for (int i = 0; i < bytesRead; i++)
            {
                byte b = readBuffer[i];
                _frameBuffer.Add(b);
                int count = _frameBuffer.Count;

                // start of jpeg
                if (!capturing && count >= 2 && _frameBuffer[count - 2] == 0xFF && _frameBuffer[count - 1] == 0xD8)
                {
                    capturing = true;
                    //clear buffer and start new image
                    _frameBuffer.Clear();
                    _frameBuffer.AddRange([0xFF, 0xD8]);
                }
                // EOF/end of jpeg
                else if (capturing && count >= 2 && _frameBuffer[count - 2] == 0xFF && _frameBuffer[count - 1] == 0xD9)
                {
                    capturing = false;
                    byte[] completeFrame = _frameBuffer.ToArray();
                    GD.Print($"\tgot frame of size {completeFrame.Length}");
                    _frameBuffer.Clear();

                    // Send the complete frame back to the Godot main thread
                    CallDeferred(MethodName.UpdateTexture, completeFrame);
                }
            }
        }
    }

    //back in sync context, runs on object idle/end of frame
    private void UpdateTexture(byte[] jpegData)
    {
        Image img = new Image();
        Error err = img.LoadJpgFromBuffer(jpegData);
        img.Crop(width, height: height);

        if (err == Error.Ok)
        {
            _imageTexture.Update(img);
        }
        else
        {
            GD.PrintErr("\tfailed to decode!");
        }
    }
}