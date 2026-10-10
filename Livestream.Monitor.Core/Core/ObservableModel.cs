using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Livestream.Monitor.Core;

public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    protected bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return false;

        storage = value;
        NotifyOfPropertyChange(propertyName);
        return true;
    }

    public void NotifyOfPropertyChange([CallerMemberName] string propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void NotifyOfPropertyChange<T>(Expression<Func<T>> property) =>
        NotifyOfPropertyChange(((MemberExpression)property.Body).Member.Name);
}

public sealed class ObservableList<T> : ObservableCollection<T>
{
    public ObservableList() { }

    public ObservableList(IEnumerable<T> items) : base(items) { }

    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items) Add(item);
    }

    public void RemoveRange(IEnumerable<T> items)
    {
        foreach (var item in items) Remove(item);
    }
}

public static class ModelEnvironment
{
    public static bool IsDesignMode { get; set; } = LicenseManager.UsageMode == LicenseUsageMode.Designtime;
}
