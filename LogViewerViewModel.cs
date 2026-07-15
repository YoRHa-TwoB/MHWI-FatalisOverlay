using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FatalisOverlay.Models;

namespace FatalisOverlay;

public class LogViewerViewModel : INotifyPropertyChanged
{
    public ObservableCollection<LogSession> Sessions { get; } = new();
    public ObservableCollection<LogEntry> FilteredEntries { get; } = new();

    private LogSession? _selectedSession;
    public LogSession? SelectedSession
    {
        get => _selectedSession;
        set { _selectedSession = value; OnPropertyChanged(); RefreshEntries(); }
    }

    // Type filters
    private bool _showAction = true;
    public bool ShowAction { get => _showAction; set { _showAction = value; OnPropertyChanged(); RefreshEntries(); } }

    private bool _showDamage = true;
    public bool ShowDamage { get => _showDamage; set { _showDamage = value; OnPropertyChanged(); RefreshEntries(); } }

    private bool _showEnrage = true;
    public bool ShowEnrage { get => _showEnrage; set { _showEnrage = value; OnPropertyChanged(); RefreshEntries(); } }

    private string _logsDir = "";

    public LogViewerViewModel()
    {
        _logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        LoadSessions();
    }

    public void LoadSessions()
    {
        Sessions.Clear();
        if (!Directory.Exists(_logsDir)) return;

        var metadata = LoadMetadata();
        var files = Directory.GetFiles(_logsDir, "*.jsonl")
            .OrderByDescending(f => f)
            .ToList();

        foreach (var file in files)
        {
            var session = LogSession.Parse(file);
            if (session == null) continue;

            // Apply custom name
            if (metadata.TryGetValue(session.FileName, out var name) && !string.IsNullOrEmpty(name))
                session.DisplayName = name;

            Sessions.Add(session);
        }
    }

    public void RefreshEntries()
    {
        FilteredEntries.Clear();
        if (_selectedSession == null) return;

        foreach (var entry in _selectedSession.Entries)
        {
            if (entry.Type == "action" && !_showAction) continue;
            if (entry.Type == "damage" && !_showDamage) continue;
            if ((entry.Type == "enrage_start" || entry.Type == "enrage_end") && !_showEnrage) continue;
            FilteredEntries.Add(entry);
        }
    }

    public void RenameSession(LogSession session, string newName)
    {
        session.DisplayName = newName;
        SaveMetadata();
        // Refresh to update display
        var idx = Sessions.IndexOf(session);
        if (idx >= 0)
        {
            Sessions.RemoveAt(idx);
            Sessions.Insert(idx, session);
        }
    }

    public void DeleteSession(LogSession session)
    {
        try { File.Delete(session.FilePath); } catch { }
        Sessions.Remove(session);
    }

    private Dictionary<string, string> LoadMetadata()
    {
        try
        {
            var path = Path.Combine(_logsDir, "_metadata.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
        }
        catch { }
        return new();
    }

    private void SaveMetadata()
    {
        try
        {
            var data = new Dictionary<string, string>();
            foreach (var s in Sessions)
                if (!string.IsNullOrEmpty(s.DisplayName))
                    data[s.FileName] = s.DisplayName;
            var path = Path.Combine(_logsDir, "_metadata.json");
            File.WriteAllText(path, JsonSerializer.Serialize(data));
        }
        catch { }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
