using Xunit;

namespace StatusLine.Tests;

public sealed class GroupLayoutTests {
    [Theory]
    [InlineData(20, 4, 3, 8, 7, 7)]
    [InlineData(21, 4, 3, 8, 7, 8)]
    [InlineData(20, 0, 3, 10, 20, 16)]
    [InlineData(20, 0, 20, 10, 20, 0)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(1, 1, 0, 0, 0, 0)]
    [InlineData(20, 20, 0, 0, 0, 0)]
    public void CenterOwnsItsBudgetAndSidesCannotOverlap(int width, int centerCells, int leftCells,
        int centerColumn, int leftBudget, int rightBudget) {
        var layout = new GroupLayout(width, centerCells);
        Assert.Equal(centerColumn, layout.CenterColumn);
        Assert.Equal(leftBudget, layout.LeftBudget);
        Assert.Equal(rightBudget, layout.RightBudget(leftCells));
    }
}
