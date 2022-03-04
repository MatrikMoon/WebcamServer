/*using OpenCvSharp;

using var cascadeClassifier = new CascadeClassifier("haarcascade_frontalface_default.xml");
using var capture = new VideoCapture(2);
if (!capture.IsOpened())
    return;

capture.FrameWidth = 1920;
capture.FrameHeight = 1280;
capture.AutoFocus = true;

const int sleepTime = 1;

using var window = new Window("capture");
using var transformWindow = new Window("face");
var image = new Mat();

while (true)
{
    capture.Read(image);
    if (image.Empty())
        break;

    *//*var bytes = image.ImEncode(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, 15));
    image = Cv2.ImDecode(bytes, ImreadModes.Unchanged);*//*

    var faces = cascadeClassifier.DetectMultiScale(image, 1.1, 20, HaarDetectionTypes.ScaleImage | HaarDetectionTypes.DoCannyPruning, new OpenCvSharp.Size(30, 30));

    foreach (var face in faces)
    {
        Cv2.Rectangle(image, face, Scalar.Red);
    }

    if (faces.Length > 0)
    {
        // Create coordinates for passing to GetPerspectiveTransform
        var sourceCoordinates = new List<Point2f>
        {
            new Point(faces[0].X - 10, faces[0].Y - 10),
            new Point(faces[0].X + faces[0].Width + 10, faces[0].Y - 10),
            new Point(faces[0].X + faces[0].Width + 10, faces[0].Y + faces[0].Height + 10),
            new Point(faces[0].X - 10, faces[0].Y + faces[0].Height + 10)
        };
        var destinationCoordinates = new List<Point2f>
        {
            new Point2f(0, 0),
            new Point2f(1024, 0),
            new Point2f(1024, 1024),
            new Point2f(0, 1024),
        };

        using var transform = Cv2.GetPerspectiveTransform(sourceCoordinates, destinationCoordinates);
        using var normalizedImage = new Mat();
        Cv2.WarpPerspective(image, normalizedImage, transform, new Size(1024, 1024));

        transformWindow.ShowImage(normalizedImage);
    }

    window.ShowImage(image);

    int c = Cv2.WaitKey(sleepTime);
    if (c >= 0)
    {
        break;
    }
}*/