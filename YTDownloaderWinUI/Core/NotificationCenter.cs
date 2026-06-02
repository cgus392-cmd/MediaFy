using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace YTDownloader.Core;

/// <summary>Una tarea en curso/terminada que se muestra en el centro de notificaciones.</summary>
public partial class AppTask : ObservableObject
{
    public AppTask(string title, string glyph)
    {
        _title = title;
        Glyph = glyph;
    }

    public string Glyph { get; }
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _status = "En curso…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBar))]
    private double _progress;      // 0..100

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBar))]
    private bool _indeterminate = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(ShowBar), nameof(BarVisibility))]
    private TaskState _state = TaskState.Running;

    public bool IsRunning => State == TaskState.Running;
    public bool ShowBar => State == TaskState.Running;
    public Visibility BarVisibility => ShowBar ? Visibility.Visible : Visibility.Collapsed;

    public void Report(double percent, string? status = null)
    {
        Indeterminate = false;
        Progress = Math.Clamp(percent, 0, 100);
        if (status != null) Status = status;
    }
    public void Done(string status = "Completado")  { State = TaskState.Done;  Status = status; Progress = 100; Indeterminate = false; }
    public void Fail(string status = "Error")        { State = TaskState.Error; Status = status; Indeterminate = false; }
}

public enum TaskState { Running, Done, Error }

/// <summary>Centro de notificaciones global: lista de tareas (descargas de modelo, separación, etc.).</summary>
public static class NotificationCenter
{
    public static ObservableCollection<AppTask> Tasks { get; } = new();

    public static event Action? Changed;

    /// <summary>Crea y registra una tarea en curso (llamar desde el hilo de UI).</summary>
    public static AppTask Start(string title, string glyph)
    {
        var t = new AppTask(title, glyph);
        t.PropertyChanged += (_, _) => Changed?.Invoke();
        Tasks.Insert(0, t);
        Changed?.Invoke();
        return t;
    }

    public static int ActiveCount
    {
        get { int n = 0; foreach (var t in Tasks) if (t.IsRunning) n++; return n; }
    }

    public static void Clear()
    {
        for (int i = Tasks.Count - 1; i >= 0; i--)
            if (!Tasks[i].IsRunning) Tasks.RemoveAt(i);
        Changed?.Invoke();
    }
}
