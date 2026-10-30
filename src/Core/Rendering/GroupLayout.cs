namespace StatusLine;

/// <summary>Shared row geometry: center takes priority, with a gap on either side.</summary>
internal readonly struct GroupLayout {
    /// <summary>Minimum blank cells between adjacent groups.</summary>
    public const int Gap = 1;
    public int CenterColumn { get; }
    public int LeftBudget { get; }
    private readonly int rightBudget;
    private readonly bool hasCenter;

    public GroupLayout(int width, int centerCells) {
        CenterColumn = (width - centerCells) / 2;
        hasCenter = centerCells > 0;
        if (hasCenter) {
            LeftBudget = Math.Max(0, CenterColumn - 1);
            rightBudget = Math.Max(0, width - CenterColumn - centerCells - 1);
        }
        else {
            LeftBudget = width;
            rightBudget = width;
        }
    }

    public int RightBudget(int leftCells) {
        if (hasCenter) {
            return rightBudget;
        }

        return Math.Max(0, rightBudget - leftCells - (leftCells > 0 ? Gap : 0));
    }
}
