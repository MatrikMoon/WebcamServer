using OpenCvSharp;
using Shared;
using Shared.Models.Packets;

namespace WebcamServer
{
    internal class VideoStreaming
    {
        public static async Task Main()
        {
            var client = new SystemClient("127.0.0.1", 10356, "moon", Connect.ConnectTypes.User);
            await client.Start();

            using var capture = new VideoCapture(2);
            if (!capture.IsOpened())
                return;

            capture.FrameWidth = 1920;
            capture.FrameHeight = 1280;
            capture.AutoFocus = true;

            var currentImage = new Mat();
            while (true)
            {
                await Task.Delay(10);
                capture.Read(currentImage);
                if (currentImage.Empty())
                    break;

                var bytes = currentImage.ImEncode(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, 20));

                //var bytes = new byte[] { 0, 0, 0, 0, 0, 0 };

                var frame = new Frame {
                    Timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond,
                    FileId = Guid.NewGuid().ToString(),
                    Compressed = false,
                    Data = bytes
                };

                var packet = new Packet
                {
                    Frame = frame
                };

                await client.Send(packet);
            }
        }
    }
}
