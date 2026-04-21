using Godot;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;

public partial class TextureRect2 : TextureRect
{
    private TcpClient _tcpClient;
    private ImageTexture _imageTexture;
    private string _senderIp = "141.219.250.187";
    private int _senderPort = 8255;
    private byte[] _frameBuffer = new byte[4000000];
    private Vector2I size = new();
    private Image img = new Image();
    private Error err;

    public override void _Ready()
    {
        //texture init
        CreateSurface(1920, 1200);

        //run decode work async, do not block frames on VR!
        GD.Print("starting async frame handler...");
        Task.Run(ReceiveStream);
    }

    private void CreateSurface(int inWidth, int inHeight)
    {
        Image emptyImg = Image.CreateEmpty(inWidth, inHeight, false, Image.Format.Rgb8);
        _imageTexture = ImageTexture.CreateFromImage(emptyImg);
        this.Texture = _imageTexture;
        size.X = inWidth;
        size.Y = inHeight;
        SubViewport port = (SubViewport)GetParent().GetParent();
        // port.Size = size;
        // port must be square apparently
        port.Size = new Vector2I(size.X, size.X);
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
        int bytesRead = 0;

        while (_tcpClient.Connected)
        {
            bytesRead = 0;
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
                    // GD.Print($"\tgot frame of size {completeFrame.Length}");
                    // generate jpeg on background thread and defer it to render thread when ready
                    // use a span to copy subset of array to loadjpeg and reduce allocs
                    err = img.LoadJpgFromBuffer(_frameBuffer.AsSpan(0, len));
                    len = 0;
                    if (err == Error.Ok)
                    {
                        CallDeferred(MethodName.UpdateTexture, img);
                    }
                }
            }
        }
        GD.Print("End of stream!");
    }

    //back in sync context, runs on object idle/end of frame
    private void UpdateTexture(Image inImg)
    {
        Vector2I newXY = inImg.GetSize();
        if (newXY != size)
        {
            GD.Print($"New image size! {newXY}");
            CreateSurface(newXY.X, newXY.Y);
        }

        _imageTexture.Update(inImg);
    }
}
