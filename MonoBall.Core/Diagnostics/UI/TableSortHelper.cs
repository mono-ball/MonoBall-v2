namespace MonoBall.Core.Diagnostics.UI;

using System;
using System.Collections.Generic;
using Hexa.NET.ImGui;

/// <summary>
/// Generic sort direction for table columns.
/// </summary>
public enum SortDirection
{
    Ascending,
    Descending,
}

/// <summary>
/// Defines a sortable column configuration.
/// </summary>
/// <typeparam name="T">The type of items being sorted.</typeparam>
public sealed class SortableColumn<T>
{
    /// <summary>Column index in the table.</summary>
    public int Index { get; init; }

    /// <summary>Column header name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Comparison function for ascending sort.</summary>
    public Comparison<T> Compare { get; init; } = null!;

    /// <summary>ImGui column flags.</summary>
    public ImGuiTableColumnFlags Flags { get; init; } = ImGuiTableColumnFlags.None;

    /// <summary>Initial column width (0 for auto).</summary>
    public float InitialWidth { get; init; }
}

/// <summary>
/// Helper for managing ImGui table sorting state.
/// </summary>
/// <typeparam name="T">The type of items being sorted.</typeparam>
public sealed class TableSortState<T>
{
    private readonly List<SortableColumn<T>> _columns = new();
    private int _sortColumnIndex;
    private SortDirection _sortDirection = SortDirection.Descending;

    /// <summary>
    /// Current sort column index.
    /// </summary>
    public int SortColumnIndex => _sortColumnIndex;

    /// <summary>
    /// Current sort direction.
    /// </summary>
    public SortDirection SortDirection => _sortDirection;

    /// <summary>
    /// Adds a sortable column definition.
    /// </summary>
    public TableSortState<T> AddColumn(
        string name,
        Comparison<T> compare,
        ImGuiTableColumnFlags flags = ImGuiTableColumnFlags.None,
        float width = 0
    )
    {
        _columns.Add(
            new SortableColumn<T>
            {
                Index = _columns.Count,
                Name = name,
                Compare = compare,
                Flags = flags,
                InitialWidth = width,
            }
        );
        return this;
    }

    /// <summary>
    /// Sets up table columns in ImGui. Call after BeginTable.
    /// </summary>
    public void SetupColumns()
    {
        foreach (var col in _columns)
        {
            var flags = col.Flags;
            if (col.InitialWidth > 0)
            {
                flags |= ImGuiTableColumnFlags.WidthFixed;
            }
            ImGui.TableSetupColumn(col.Name, flags, col.InitialWidth);
        }
        ImGui.TableHeadersRow();
    }

    /// <summary>
    /// Handles ImGui sort spec changes. Call after SetupColumns.
    /// </summary>
    public unsafe void HandleSortSpecs()
    {
        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.Handle == null || !sortSpecs.SpecsDirty)
            return;

        if (sortSpecs.SpecsCount > 0)
        {
            var spec = sortSpecs.Specs;
            _sortColumnIndex = spec.ColumnIndex;
            _sortDirection =
                spec.SortDirection == ImGuiSortDirection.Ascending
                    ? SortDirection.Ascending
                    : SortDirection.Descending;
        }

        sortSpecs.SpecsDirty = false;
    }

    /// <summary>
    /// Sorts the list according to current sort state.
    /// </summary>
    public void Sort(List<T> items)
    {
        if (_sortColumnIndex < 0 || _sortColumnIndex >= _columns.Count)
            return;

        var column = _columns[_sortColumnIndex];
        if (column.Compare == null)
            return;

        if (_sortDirection == SortDirection.Ascending)
        {
            items.Sort(column.Compare);
        }
        else
        {
            items.Sort((a, b) => column.Compare(b, a));
        }
    }

    /// <summary>
    /// Sets the default sort column and direction.
    /// </summary>
    public TableSortState<T> SetDefaultSort(
        int columnIndex,
        SortDirection direction = SortDirection.Descending
    )
    {
        _sortColumnIndex = columnIndex;
        _sortDirection = direction;
        return this;
    }
}
