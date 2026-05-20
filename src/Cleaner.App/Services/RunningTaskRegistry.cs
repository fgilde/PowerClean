using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.Services;

public sealed partial class RunningTask : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public DateTime StartedAt { get; } = DateTime.Now;
    public string Title { get; init; } = "";
    public string Category { get; init; } = "";
    public CancellationTokenSource? CancelSource { get; init; }

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private long _bytesProcessed;

    [ObservableProperty]
    private int _itemsProcessed;

    [ObservableProperty]
    private bool _isCancelling;

    partial void OnIsCancellingChanged(bool value) => CancelCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel()
    {
        Cleaner.App.App.LogInfo($"Cancel clicked: {Title} (cts={CancelSource is not null})");
        try
        {
            IsCancelling = true;
            StatusText = "Wird abgebrochen...";
            CancelSource?.Cancel();
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("RunningTask.Cancel", ex);
        }
        finally
        {
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCancel() => !IsCancelling && CancelSource is not null;
}

public sealed partial class RunningTaskRegistry : ObservableObject
{
    public ObservableCollection<RunningTask> Tasks { get; } = new();

    [ObservableProperty]
    private int _count;

    public RunningTask Start(string title, string category, CancellationTokenSource? cts = null, bool indeterminate = false)
    {
        var task = new RunningTask
        {
            Title = title,
            Category = category,
            CancelSource = cts,
            IsIndeterminate = indeterminate,
        };

        var app = Application.Current;
        if (app?.Dispatcher.CheckAccess() == true)
        {
            Tasks.Add(task);
            Count = Tasks.Count;
        }
        else
        {
            app?.Dispatcher.Invoke(() =>
            {
                Tasks.Add(task);
                Count = Tasks.Count;
            });
        }
        return task;
    }

    public void Complete(RunningTask? task)
    {
        if (task is null) return;
        var app = Application.Current;
        if (app?.Dispatcher.CheckAccess() == true)
        {
            Tasks.Remove(task);
            Count = Tasks.Count;
        }
        else
        {
            app?.Dispatcher.Invoke(() =>
            {
                Tasks.Remove(task);
                Count = Tasks.Count;
            });
        }
    }
}
