using OpenCvSharp;
using Shared;
using Shared.Models.Packets;

namespace WebcamServer
{
    internal class VideoStreaming
    {
        public static async Task Main()
        {
            var server = new SystemServer();
            server.Start();

            using var capture = new VideoCapture(2);
            if (!capture.IsOpened())
                return;

            capture.FrameWidth = 1920;
            capture.FrameHeight = 1280;
            capture.AutoFocus = true;

            var lastTimestamp = 0;
            var currentImage = new Mat();
            while (true)
            {
                capture.Read(currentImage);
                if (currentImage.Empty())
                    break;

                var bytes = currentImage.ImEncode(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, 20));

                var frame = new Frame {
                    FileId = Guid.NewGuid().ToString(),
                    Compressed = false,
                    Data = bytes
                };

                var packet = new Packet
                {
                    Frame = frame
                };

                await server.BroadcastToAllClients(packet);
            }
        }
    }
}
