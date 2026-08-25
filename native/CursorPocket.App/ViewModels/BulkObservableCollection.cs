using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CursorPocket_App.ViewModels;

/// <summary>
/// An observable collection that can be refilled with a single reset notification.
/// <para>
/// Clearing and re-adding N rows makes a <c>ListView</c> react N + 1 times and
/// regenerate every container it had, which is a visible hitch on a full library.
/// </para>
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
