namespace Arkki.Pdf.Barcodes;

/// <summary>
/// A barcode as a grid of dark and light modules. Linear symbologies have one row; their bars run
/// the full height of the symbol. <see cref="PdfCanvas.Barcode"/> draws it as vector rectangles.
/// </summary>
public sealed class BarcodeMatrix
{
    private readonly bool[] _dark;

    /// <param name="columns">Width in modules.</param>
    /// <param name="rows">Height in modules.</param>
    /// <param name="quietZone">Light modules the symbology needs around the symbol for scanners.</param>
    public BarcodeMatrix(int columns, int rows, int quietZone)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(quietZone);
        Columns = columns; Rows = rows; QuietZone = quietZone;
        _dark = new bool[columns * rows];
    }

    /// <summary>Width in modules, without the quiet zone.</summary>
    public int Columns { get; }
    /// <summary>Height in modules (1 for a linear barcode).</summary>
    public int Rows { get; }
    /// <summary>The light margin, in modules, that scanners need on each side.</summary>
    public int QuietZone { get; }

    /// <summary>True when the module at column <paramref name="x"/>, row <paramref name="y"/> is dark.</summary>
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
    /// <summary>Encodes <paramref name="text"/>; throws <see cref="ArgumentException"/> for text the symbology cannot hold.</summary>
    BarcodeMatrix Encode(string text);
}
