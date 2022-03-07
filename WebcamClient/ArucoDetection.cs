/*using OpenCvSharp;
using OpenCvSharp.Aruco;

const int upperLeftMarkerId = 160;
const int upperRightMarkerId = 268;
const int lowerRightMarkerId = 176;
const int lowerLeftMarkerId = 168;

using var capture = new VideoCapture(2);
if (!capture.IsOpened())
    return;

capture.FrameWidth = 1920;
capture.FrameHeight = 1280;
capture.AutoFocus = true;

const int sleepTime = 1;

using var window = new Window("capture");
var image = new Mat();

while (true)
{
    capture.Read(image);
    if (image.Empty())
        break;

    var detectorParameters = DetectorParameters.Create();
    detectorParameters.CornerRefinementMethod = CornerRefineMethod.Subpix;
    detectorParameters.CornerRefinementWinSize = 9;

    using var dictionary = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_1000);
    CvAruco.DetectMarkers(image, dictionary, out var corners, out var ids, detectorParameters, out var rejectedPoints);
    CvAruco.DrawDetectedMarkers(image, corners, ids, Scalar.Crimson);

    var upperLeftCornerIndex = Array.FindIndex(ids, id => id == upperLeftMarkerId);
    var upperRightCornerIndex = Array.FindIndex(ids, id => id == upperRightMarkerId);
    var lowerRightCornerIndex = Array.FindIndex(ids, id => id == lowerRightMarkerId);
    var lowerLeftCornerIndex = Array.FindIndex(ids, id => id == lowerLeftMarkerId);

    // Make sure we found all four markers.
    if (upperLeftCornerIndex >= 0 && upperRightCornerIndex >= 0 && lowerRightCornerIndex >= 0 && lowerLeftCornerIndex >= 0)
    {
        // Marker corners are stored clockwise beginning with the upper-left corner.
        // Get the first (upper-left) corner of the upper-left marker.
        var upperLeftPixel = corners[upperLeftCornerIndex][0];
        // Get the second (upper-right) corner of the upper-right marker.
        var upperRightPixel = corners[upperRightCornerIndex][1];
        // Get the third (lower-right) corner of the lower-right marker.
        var lowerRightPixel = corners[lowerRightCornerIndex][2];
        // Get the fourth (lower-left) corner of the lower-left marker.
        var lowerLeftPixel = corners[lowerLeftCornerIndex][3];

        // Create coordinates for passing to GetPerspectiveTransform
        var sourceCoordinates = new List<Point2f>
        {
            upperLeftPixel, upperRightPixel, lowerRightPixel, lowerLeftPixel
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
        window.ShowImage(normalizedImage);
    }
    else window.ShowImage(image);

    int c = Cv2.WaitKey(sleepTime);
    if (c >= 0)
    {
        break;
    }
}*/