namespace StatusLine.Refreshing;

internal sealed partial class RefreshValue<T> {
    static partial void NotifyRefresh() => StatusLineUpdater.Wake();
}
