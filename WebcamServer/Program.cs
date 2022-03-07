using OpenCvSharp;
using Shared;
using Shared.Models.Packets;

namespace WebcamServer
{
    internal class VideoServer
    {
        private static Window window = new Window("server");
        private static long _lastFrameTimestamp = 0;
        //private static VideoCapture capture = new VideoCapture(2);

        public static async Task Main()
        {
            var client = new SystemClient("127.0.0.1", 10356, "moon", Connect.ConnectTypes.User);
            client.FrameReceived += Client_FrameReceived;
            await client.Start();

            /*if (!capture.IsOpened())
                return;

            capture.FrameWidth = 1920;
            capture.FrameHeight = 1280;
            capture.AutoFocus = true;*/

            while (true)
            {
                int c = Cv2.WaitKey(1);
                if (c >= 0)
                {
                    break;
                }
            }
        }

        private static Task Client_FrameReceived(Frame frame)
        {
            /*var currentImage = new Mat();
            capture.Read(currentImage);
            if (currentImage.Empty())
                return Task.CompletedTask;

            var bytes = currentImage.ImEncode(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, 20));

            frame = new Frame
            {
                Timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond,
                FileId = Guid.NewGuid().ToString(),
                Compressed = false,
                Data = bytes
            };*/

            if (frame.Timestamp < _lastFrameTimestamp) return Task.CompletedTask;
            else _lastFrameTimestamp = frame.Timestamp;

            var jpgImage = Cv2.ImDecode(frame.Data, ImreadModes.Unchanged);
            window.ShowImage(jpgImage);
            //jpgImage.Dispose();
            return Task.CompletedTask;
        }
    }
}
