using Godot;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;

public partial class TextureRect2 : TextureRect
{
    private TcpClient _tcpClient;
    private ImageTexture _imageTexture;
    private string _senderIp = "127.0.0.1";
    private int _senderPort = 8255;
    private byte[] _frameBuffer = new byte[4000000];

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

        int len = 0;
        byte[] readBuffer = new byte[65536];
        bool capturing = false;

        while (_tcpClient.Connected)
        {
            int bytesRead = 0;
            try
            {
                // Read at least might be better for efficiency but likely worse frame timing
                bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
                // GD.Print($"read {bytesRead}");
            }
            catch (Exception e)
            {
                GD.PrintErr($"\tstream read error: {e.Message}");
            }
            if (bytesRead == 0) break;

            //iterate through stream and copy data
            //track when next image starts
            for (int i = 0; i < bytesRead; i++)
            {
                //add and increment
                _frameBuffer[len++] = readBuffer[i];

                // start of jpeg
                if (!capturing && len >= 2 && _frameBuffer[len - 2] == 0xFF && _frameBuffer[len - 1] == 0xD8)
                {
                    capturing = true;
                    //start new image by writing over the last one
                    len = 2;
                    _frameBuffer[0] = 0xFF;
                    _frameBuffer[1] = 0xD8;
                }
                // end of jpeg
                else if (capturing && len >= 2 && _frameBuffer[len - 2] == 0xFF && _frameBuffer[len - 1] == 0xD9)
                {
                    capturing = false;
                    len = 0;
                    // GD.Print($"\tgot frame of size {completeFrame.Length}");
                    // Send the complete frame back to the Godot main thread
                    CallDeferred(MethodName.UpdateTexture, _frameBuffer);
                }
            }
        }
        GD.Print("End of stream!");
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
