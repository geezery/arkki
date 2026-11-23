namespace Arkki.Pdf.Barcodes;

/// <summary>
/// A barcode as a grid of dark and light modules. Linear symbologies have one row; their bars run
/// the full height of the symbol. <see cref="PdfCanvas.Barcode"/> draws it as vector rectangles.
/// </summary>
public sealed class BarcodeMatrix
{
    private readonly bool[] _dark;

    /// <param name="quietZone">Light modules the symbology needs around the symbol for scanners.</param>
    public BarcodeMatrix(int columns, int rows, int quietZone)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(quietZone);
        Columns = columns; Rows = rows; QuietZone = quietZone;
        _dark = new bool[columns * rows];
    }

    public int Columns { get; }
    public int Rows { get; }
    public int QuietZone { get; }

    public bool this[int x, int y]
    {
        get => _dark[y * Columns + x];
        set => _dark[y * Columns + x] = value;
    }
}

/// <summary>
/// A barcode symbology. Implement it to add one that Arkki does not ship; the result draws and lays
/// out like the built-in Code 128, EAN-13 and QR code.
/// </summary>
public interface IBarcodeSymbology
{
    BarcodeMatrix Encode(string text);
}
