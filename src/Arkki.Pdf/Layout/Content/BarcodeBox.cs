using Arkki.Pdf.Barcodes;

namespace Arkki.Pdf.Layout;

/// <summary>
/// A barcode of a fixed size. The size includes the symbology's quiet zone on the left and right
/// (and above and below for a 2D code), so the light margin scanners need is always kept.
/// </summary>
public sealed class BarcodeBox(BarcodeMatrix matrix, double width, double height, HAlign align = HAlign.Left) : Element
{
    /// <summary>A box that encodes <paramref name="text"/> with <paramref name="symbology"/> and fits it, quiet zone included, into width × height points.</summary>
    public BarcodeBox(IBarcodeSymbology symbology, string text, double width, double height, HAlign align = HAlign.Left)
        : this(symbology.Encode(text), width, height, align) { }

    /// <inheritdoc />
    public override LayoutResult Layout(double available, double availableHeight) =>
        height > availableHeight + 0.001 ? LayoutResult.None(this) : LayoutResult.Full(new BarcodeFragment(matrix, width, height, available, align));

    private sealed class BarcodeFragment : Fragment
    {
        private readonly BarcodeMatrix _m;
        private readonly double _w;
        private readonly HAlign _align;

        public BarcodeFragment(BarcodeMatrix m, double w, double h, double width, HAlign align)
        {
            _m = m; _w = Math.Min(w, width); _align = align; Width = width; Height = h;
        }

        public override void Draw(PdfCanvas canvas, double x, double y)
        {
            double dx = _align switch { HAlign.Right => Width - _w, HAlign.Center => (Width - _w) / 2, _ => 0 };
            int q = _m.QuietZone;
            double mw = _w / (_m.Columns + 2 * q);
            if (_m.Rows == 1)
            {
                canvas.Barcode(_m, x + dx + q * mw, y, _m.Columns * mw, Height);
                return;
            }
            double mh = Height / (_m.Rows + 2 * q);
            canvas.Barcode(_m, x + dx + q * mw, y + q * mh, _m.Columns * mw, _m.Rows * mh);
        }
    }
}
