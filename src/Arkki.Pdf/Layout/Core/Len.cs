namespace Arkki.Pdf.Layout;

/// <summary>A column width: a fixed size in points or a share of the remaining space.</summary>
public readonly record struct Len(double Value, bool IsStar)
{
    /// <summary>A fixed width in points.</summary>
    public static Len Pt(double points) => new(points, false);
    /// <summary>A fixed width in millimetres.</summary>
    public static Len Mm(double mm) => new(Unit.Mm(mm), false);
    /// <summary>A share of the width the fixed columns leave; a weight of 2 takes twice as much as 1.</summary>
    public static Len Star(double weight = 1) => new(weight, true);

    internal static double[] Resolve(IReadOnlyList<Len> lens, double available)
    {
        double fixedSum = 0, stars = 0;
        foreach (Len l in lens) { if (l.IsStar) stars += l.Value; else fixedSum += l.Value; }
        double rest = Math.Max(0, available - fixedSum);
        var result = new double[lens.Count];
        for (int i = 0; i < lens.Count; i++)
            result[i] = lens[i].IsStar ? (stars > 0 ? rest * lens[i].Value / stars : 0) : lens[i].Value;
        return result;
    }
}
