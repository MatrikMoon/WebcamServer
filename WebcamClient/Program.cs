using OpenCvSharp;
using Shared;
using Shared.Models.Packets;

namespace WebcamServer
{
    internal class VideoClient
    {
        private static Window window = new Window("client");

        public static async Task Main()
        {
            var client = new SystemClient("127.0.0.1", 10356, "moon", Connect.ConnectTypes.User);
            client.FrameReceived += Client_FrameReceived;
            await client.Start();
            while (true)
            {
                int c = Cv2.WaitKey(10);
                if (c >= 0)
                {
                    break;
                }
            }
        }

        private static Task Client_FrameReceived(Frame frame)
        {
            var jpgImage = Cv2.ImDecode(frame.Data, ImreadModes.Unchanged);
            window.ShowImage(jpgImage);
            jpgImage.Dispose();
            return Task.CompletedTask;
        }
    }
}
