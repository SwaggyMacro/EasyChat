using Avalonia;
using EasyChat.Contracts.Ocr;
using EasyChat.Presentation.Features.ScreenshotOcr.Controls;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class ScreenshotOcrViewportTests
{
    [TestMethod]
    public void TransformRoundTripPreservesHighDpiCoordinates()
    {
        var transform = new OcrImageViewport.ViewportTransform(1.75, new Point(-82.5, 41.25));
        var imagePoint = new ImagePoint(640.25, 359.75);

        var viewPoint = transform.ToView(imagePoint);
        var actual = transform.ToImage(viewPoint);

        Assert.AreEqual(imagePoint.X, actual.X, 0.0001);
        Assert.AreEqual(imagePoint.Y, actual.Y, 0.0001);
    }

    [TestMethod]
    public void SpatialIndexHitTestsRotatedPolygonRatherThanItsBounds()
    {
        var diamond = new OcrTextRegion(
            "rotated",
            [new ImagePoint(50, 10), new ImagePoint(90, 50), new ImagePoint(50, 90), new ImagePoint(10, 50)],
            45);
        var index = new OcrRegionSpatialIndex([diamond]);

        Assert.AreEqual(0, index.HitTest(new Point(50, 50)));
        Assert.IsNull(index.HitTest(new Point(12, 12)));
    }

    [TestMethod]
    public void SpatialIndexBoxSelectionReturnsIntersectingBlocksOnly()
    {
        var index = new OcrRegionSpatialIndex(
        [
            Region("first", 0, 0, 20, 10),
            Region("second", 80, 80, 20, 10),
            Region("third", 180, 180, 20, 10)
        ]);

        var selected = index.Query(new Rect(10, 5, 100, 100));

        CollectionAssert.AreEquivalent(new[] { 0, 1 }, selected.ToArray());
    }

    [TestMethod]
    public void SelectionWithoutControlReplacesExistingBlocks()
    {
        var selected = new HashSet<int> { 0, 2 };

        OcrRegionSelection.Apply(selected, [1], toggle: false);

        CollectionAssert.AreEquivalent(new[] { 1 }, selected.ToArray());
    }

    [TestMethod]
    public void ControlSelectionTogglesBlocksWithoutClearingOthers()
    {
        var selected = new HashSet<int> { 0, 2 };

        OcrRegionSelection.Apply(selected, [1, 2], toggle: true);

        CollectionAssert.AreEquivalent(new[] { 0, 1 }, selected.ToArray());
    }

    [TestMethod]
    public void TextSelectorNearRightEdgeRemainsFullyInsideViewport()
    {
        var layout = OcrImageViewport.GetTextSelectorLayout(
            new Rect(470, 40, 45, 24),
            new Size(500, 300));

        Assert.IsGreaterThanOrEqualTo(220, layout.Bounds.Width);
        Assert.IsLessThanOrEqualTo(492, layout.Bounds.Right);
        Assert.IsGreaterThanOrEqualTo(8, layout.Bounds.Left);
        Assert.IsGreaterThanOrEqualTo(14, layout.FontSize);
    }

    private static OcrTextRegion Region(
        string text,
        double x,
        double y,
        double width,
        double height) => new(
        text,
        [
            new ImagePoint(x, y),
            new ImagePoint(x + width, y),
            new ImagePoint(x + width, y + height),
            new ImagePoint(x, y + height)
        ],
        0);
}
