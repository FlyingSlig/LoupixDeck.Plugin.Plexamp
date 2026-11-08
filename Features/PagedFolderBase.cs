using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Plexamp.Features;

/// <summary>
/// Folder with a list of items split over pages. Every side knob turns pages; the title
/// and the knob labels show where the user is when there is more than one page.
/// </summary>
internal abstract class PagedFolderBase(IPluginHost host) : FolderProviderBase
{
    // Side dial indices: top/middle/bottom left are 0/1/2, top/middle/bottom right are 3/4/5.
    private static readonly int[] PageRotaries = [0, 1, 2, 3, 4, 5];

    private static readonly IReadOnlyDictionary<int, RotaryOverride> EmptyOverrides =
        new Dictionary<int, RotaryOverride>();

    private int _page;

    protected IPluginHost Host { get; } = host;

    protected abstract int ItemCount { get; }

    protected abstract string EmptyText { get; }

    protected abstract FolderEntry BuildItem(int index, int slot);

    /// <summary>Non-null while the folder wants to show a loading/error/empty state instead.</summary>
    protected virtual FolderEntry? StatusEntry() => null;

    protected int ItemsPerPage => Math.Max(1, Host.FolderGrid.TotalSlots - 1);

    protected int PageCount => Math.Max(1, (ItemCount + ItemsPerPage - 1) / ItemsPerPage);

    protected string PageSuffix => PageCount > 1 ? $" {_page + 1}/{PageCount}" : string.Empty;

    /// <summary>Called after a rotary page turn, before the host is asked to redraw.</summary>
    protected virtual void OnPageChanged()
    {
    }

    public override IReadOnlyList<FolderEntry> BuildEntries()
    {
        FolderEntry? status = StatusEntry();
        if (status is not null)
            return [status];

        if (ItemCount == 0)
            return [Message(EmptyText)];

        (int start, int count) = CurrentPageRange();
        List<FolderEntry> entries = [];

        for (int i = 0; i < count; i++)
        {
            int slot = Host.FolderGrid.SlotForIndex(i);
            if (slot < 0) break;
            entries.Add(BuildItem(start + i, slot));
        }

        return entries;
    }

    /// <summary>The item range currently visible. Artwork prefetching uses the same range.</summary>
    protected (int Start, int Count) CurrentPageRange()
    {
        ClampPage();
        int start = _page * ItemsPerPage;
        int count = Math.Min(ItemsPerPage, Math.Max(0, ItemCount - start));
        return (start, count);
    }

    public override IReadOnlyDictionary<int, RotaryOverride> RotaryOverrides
    {
        get
        {
            if (PageCount <= 1)
                return EmptyOverrides;

            Dictionary<int, RotaryOverride> overrides = new();
            string label = $"Page {_page + 1}/{PageCount}";

            foreach (int index in PageRotaries)
            {
                overrides[index] = new RotaryOverride
                {
                    OnLeft = () => TurnAsync(-1),
                    OnRight = () => TurnAsync(+1),
                    // Keep the side strips quiet: only the bottom-right knob shows the page.
                    Label = index == 5 ? label : null
                };
            }

            return overrides;
        }
    }

    private Task TurnAsync(int delta)
    {
        int next = Math.Clamp(_page + delta, 0, PageCount - 1);
        if (next == _page)
            return Task.CompletedTask;

        _page = next;
        OnPageChanged();
        RaiseEntriesChanged();
        return Task.CompletedTask;
    }

    private void ClampPage()
    {
        int max = PageCount - 1;
        if (_page > max) _page = max;
        if (_page < 0) _page = 0;
    }

    protected static FolderEntry Message(string text, PluginColor? color = null) => new()
    {
        SlotIndex = 0,
        Text = text,
        TextSize = 14,
        BackColor = color ?? FolderTheme.Tile,
        TextColor = PluginColor.White
    };
}
