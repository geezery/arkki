namespace Arkki.Pdf.Layout;

/// <summary>An image scaled to fit inside a maximum box while keeping its aspect ratio.</summary>
public sealed class ImageBox(PdfImage image, double maxWidth, double maxHeight, HAlign align = HAlign.Left) : Element
{
    /// <inheritdoc />
    public override LayoutResult Layout(double width, double height)
    {
        double scale = Math.Min(Math.Min(maxWidth, width) / image.Width, maxHeight / image.Height);
        double w = image.Width * scale, h = image.Height * scale;
        return h > height + 0.001 ? LayoutResult.None(this) : LayoutResult.Full(new ImageFragment(image, w, h, width, align));
    }

    private sealed class ImageFragment : Fragment
    {
        private readonly PdfImage _image;
        private readonly double _w;
        private readonly HAlign _align;

        public ImageFragment(PdfImage image, double w, double h, double width, HAlign align)
        {
            _image = image; _w = w; _align = align; Width = width; Height = h;
        }

        public override void Draw(PdfCanvas canvas, double x, double y)
        {
            double dx = _align switch { HAlign.Right => Width - _w, HAlign.Center => (Width - _w) / 2, _ => 0 };
            canvas.Image(_image, x + dx, y, _w, Height);
        }
    }
}
