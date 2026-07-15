using System.ComponentModel;
using System.Runtime.CompilerServices;
using FatalisOverlay.Models;
using FatalisOverlay.Services;

namespace FatalisOverlay;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ProcessService _processService = new();
    private readonly AppConfig _config;
    private readonly SynchronizationContext _syncContext;
    private CancellationTokenSource? _cts;

    private GameData _data = new();
    public GameData Data
    {
        get => _data;
        set { _data = value; OnPropertyChanged(); }
    }

    private bool _overlayVisible = true;
    public bool OverlayVisible
    {
        get => _overlayVisible;
        set { _overlayVisible = value; OnPropertyChanged(); }
    }

    public AppConfig Config => _config;

    public MainViewModel()
    {
        _config = AppConfig.Load();
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        BattleLogger.LoadActionNames();
    }

    public void StartPolling()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!_processService.IsConnected)
                    {
                        _processService.TryConnect();
                        if (!_processService.IsConnected)
                        {
                            UpdateUI(new GameData { StatusText = "等待 MonsterHunterWorld.exe..." });
                            await Task.Delay(1000, token);
                            continue;
                        }
                    }

                    try
                    {
                        var procs = System.Diagnostics.Process.GetProcessesByName("MonsterHunterWorld");
                        if (procs.Length == 0) throw new Exception("process exited");
                    }
                    catch
                    {
                        _processService.Disconnect();
                        UpdateUI(new GameData { StatusText = "等待 MonsterHunterWorld.exe..." });
                        continue;
                    }

                    var data = _processService.ReadGameData();

                    BattleLogger.Log(data, _config.ShowBattleLog);

                    if (!data.InQuest)
                        _processService.ResetOnNewQuest();

                    UpdateUI(data);

                    await Task.Delay(_config.PollingRateMs, token);
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(1000, token); }
            }
        }, token);
    }

    private void UpdateUI(GameData data)
    {
        _syncContext.Post(_ => Data = data, null);
    }

    public void StopPolling()
    {
        _cts?.Cancel();
        _processService.Disconnect();
    }

    public void SaveConfig() => _config.Save();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        StopPolling();
        SaveConfig();
    }
}
