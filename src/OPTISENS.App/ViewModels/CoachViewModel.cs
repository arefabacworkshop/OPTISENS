using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OPTISENS.Ai;
using OPTISENS.Core.Config;

namespace OPTISENS.App.ViewModels;

public sealed partial class ChatLine : ObservableObject
{
    [ObservableProperty] private string _text;
    public IBrush Brush { get; }

    public static readonly IBrush UserBrush = new SolidColorBrush(Color.Parse("#8E97A6"));
    public static readonly IBrush CoachBrush = new SolidColorBrush(Color.Parse("#F4F6F8"));
    public static readonly IBrush ToolBrush = new SolidColorBrush(Color.Parse("#66B8C4"));

    public ChatLine(string prefix, string text, IBrush brush)
    {
        _text = prefix.Length > 0 ? $"{prefix}{text}" : text;
        Brush = brush;
    }
}

public partial class CoachViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan IdleUnloadDelay = TimeSpan.FromMinutes(5);

    private Lfm3Engine? _engine;
    private ICoachSession? _session;
    private ToolDispatcher? _dispatcher;
    private string? _modelDir;
    private bool _skipAi;
    private AiProviderMode _aiMode = AiProviderMode.LocalLfm3;
    private InferenceBackendMode _backendMode = InferenceBackendMode.DirectMlGpu;
    private List<ChatMessage> _historySnapshot = [];
    private DispatcherTimer? _idleTimer;
    private CancellationTokenSource? _turnCts;

    [ObservableProperty] private bool _isLocalAiMode = true;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isAiDisabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(ToggleBackendCommand), nameof(UnloadModelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(ToggleBackendCommand), nameof(UnloadModelCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnloadModelCommand))]
    private bool _isModelLoaded;

    [ObservableProperty] private string _statusBanner = "";
    [ObservableProperty] private string _providerBadge = "IDLE";

    public ObservableCollection<ChatLine> Transcript { get; } = new();

    public event Action<double>? Cm360Adjusted;
    public event Action<AiProviderMode?>? ConfigureAiRequested;

    [RelayCommand]
    private void ConfigureAi() => ConfigureAiRequested?.Invoke(null);

    [RelayCommand]
    private void ConfigureLocalAi() => ConfigureAiRequested?.Invoke(AiProviderMode.LocalLfm3);

    [RelayCommand]
    private void ConfigureApiAi() => ConfigureAiRequested?.Invoke(AiProviderMode.OpenAiCompatible);

    public void UpdateDpi(double dpi) => _dispatcher?.UpdateDpi(dpi);

    /// <summary>Re-arms the coach with fresh calibration data (e.g. after AI setup from Results).</summary>
    public async Task ReinitializeAsync(
        string modelDir,
        bool skipAi,
        string telemetryJson,
        double initialCm360,
        double dpi,
        double boundsLo,
        double boundsHi)
    {
        CancelTurn();
        CaptureAndDetach();
        Transcript.Clear();
        await InitializeAsync(modelDir, skipAi, telemetryJson, initialCm360, dpi, boundsLo, boundsHi);
    }

    public async Task InitializeAsync(
        string modelDir,
        bool skipAi,
        string telemetryJson,
        double initialCm360,
        double dpi,
        double boundsLo,
        double boundsHi)
    {
        if (skipAi)
        {
            _skipAi = true;
            _aiMode = AiProviderMode.Disabled;
            IsAiDisabled = true;
            IsLocalAiMode = false;
            ProviderBadge = "DISABLED";
            StatusBanner = "AI disabled (--skip-ai).";
            return;
        }

        var settings = AppSettingsStore.Load();
        _aiMode = settings.AiMode;
        _modelDir = modelDir;
        _skipAi = false;

        if (_aiMode == AiProviderMode.Disabled)
        {
            IsAiDisabled = true;
            IsLocalAiMode = false;
            ProviderBadge = "OFF";
            StatusBanner = "";
            return;
        }

        IsAiDisabled = false;
        _dispatcher = new ToolDispatcher(initialCm360, dpi, boundsLo, boundsHi);
        _dispatcher.Cm360Changed += cm => Dispatcher.UIThread.Post(() => Cm360Adjusted?.Invoke(cm));

        string introPrompt =
            $"Here is the completed calibration telemetry:\n```json\n{telemetryJson}\n```\nIntroduce the results in <=60 words.";

        if (_aiMode == AiProviderMode.OpenAiCompatible)
        {
            IsLocalAiMode = false;
            if (string.IsNullOrWhiteSpace(settings.OpenAiBaseUrl) || string.IsNullOrWhiteSpace(settings.OpenAiModel))
            {
                StatusBanner = "OpenAI-compatible endpoint is not configured. Click AI Settings to set Base URL and Model.";
                ProviderBadge = "API · UNCONFIGURED";
                return;
            }

            try
            {
                var openAiSession = new OpenAiCoachSession(
                    settings.OpenAiBaseUrl,
                    settings.OpenAiApiKey,
                    settings.OpenAiModel,
                    _dispatcher,
                    history: _historySnapshot);
                openAiSession.ToolCallObserved += OnToolCallObserved;
                _session = openAiSession;
                ProviderBadge = $"API · {settings.OpenAiModel}";
                StatusBanner = "";
                await RunTurnAsync(introPrompt, showUserLine: false);
            }
            catch (Exception ex)
            {
                StatusBanner = $"OpenAI-compatible AI unavailable: {ex.Message}";
                ProviderBadge = "API · ERROR";
            }
            return;
        }

        IsLocalAiMode = true;
        _backendMode = settings.GetBackendMode();

        if (await EnsureEngineAsync())
        {
            await RunTurnAsync(introPrompt, showUserLine: false);
        }
    }

    /// <summary>
    /// Loads the engine + session if absent (transparent reload after idle unload or
    /// backend toggle), preserving the conversation history across the reload.
    /// </summary>
    private async Task<bool> EnsureEngineAsync()
    {
        if (_session is not null) return true;
        if (_skipAi || _modelDir is null) return false;

        IsLoading = true;
        ProviderBadge = "INITIALIZING…";
        StatusBanner = "Loading LFM3-1.7B coach model…";
        try
        {
            var modelDir = _modelDir;
            var preferGpu = _backendMode == InferenceBackendMode.DirectMlGpu;
            var dispatcher = _dispatcher!;
            var history = _historySnapshot;

            var (engine, session) = await Task.Run(() =>
            {
                var e = new Lfm3Engine(modelDir, preferGpu);
                var s = new CoachSession(e, dispatcher, history: history);
                s.ToolCallObserved += OnToolCallObserved;
                return (e, s);
            });

            _engine = engine;
            _session = session;
            IsModelLoaded = true;
            ProviderBadge = engine.ExecutionProviderName;
            StatusBanner = "";
            RestartIdleTimer();
            return true;
        }
        catch (ModelMissingException ex)
        {
            StatusBanner = $"{ex.Message}\n{ex.Remediation}";
        }
        catch (Exception ex)
        {
            StatusBanner = $"AI coach unavailable: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
        return false;
    }

    /// <summary>Free RAM now: keep the conversation, drop the model; it reloads on demand.</summary>
    [RelayCommand(CanExecute = nameof(CanUnload))]
    private void UnloadModel()
    {
        if (!IsModelLoaded) return;
        CaptureAndDetach();
        IsModelLoaded = false;
        ProviderBadge = "UNLOADED";
        StatusBanner = "Model unloaded — RAM freed. It reloads automatically on your next message.";
        _idleTimer?.Stop();

        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        Lfm3Engine.TrimProcessMemory();
    }

    private bool CanUnload() => IsModelLoaded && !IsBusy && !IsLoading;

    /// <summary>Switch GPU (DirectML) / CPU Eco at runtime, persisting the choice.</summary>
    [RelayCommand(CanExecute = nameof(CanToggleBackend))]
    private async Task ToggleBackendAsync()
    {
        _backendMode = _backendMode == InferenceBackendMode.DirectMlGpu
            ? InferenceBackendMode.CpuEco
            : InferenceBackendMode.DirectMlGpu;
        PersistBackendMode();

        if (IsModelLoaded)
        {
            CaptureAndDetach();
            IsModelLoaded = false;
            await EnsureEngineAsync();
        }
        else
        {
            StatusBanner = _backendMode == InferenceBackendMode.DirectMlGpu
                ? "Backend set to GPU (DirectML) — applies when the model loads. Note: some AMD GPUs produce garbled replies on DirectML; if output looks like gibberish, switch back to CPU Eco."
                : "Backend set to CPU Eco — applies when the model loads.";
        }
    }

    private bool CanToggleBackend() => !_skipAi && !IsBusy && !IsLoading;

    private void PersistBackendMode()
    {
        try
        {
            var settings = AppSettingsStore.Load();
            AppSettingsStore.Save(settings with
            {
                InferenceBackend = _backendMode == InferenceBackendMode.DirectMlGpu ? "DirectMlGpu" : "CpuEco"
            });
        }
        catch { /* best effort: in-memory switch still applies */ }
    }

    private List<ChatMessage> CaptureAndDetach()
    {
        CancelTurn();
        _historySnapshot = _session?.History.ToList() ?? _historySnapshot;
        _session?.Dispose();
        _session = null;
        _engine?.Dispose();
        _engine = null;
        IsModelLoaded = false;
        return _historySnapshot;
    }

    private void OnToolCallObserved(ToolCallRecord call)
    {
        var args = string.Join(", ", call.Arguments.Select(a => a.Value));
        Dispatcher.UIThread.Post(() =>
            Transcript.Add(new ChatLine("", $"⚙ {call.Name}({args})", ChatLine.ToolBrush)));
    }

    private void RestartIdleTimer()
    {
        if (_idleTimer is null)
        {
            _idleTimer = new DispatcherTimer { Interval = IdleUnloadDelay };
            _idleTimer.Tick += OnIdleTimeout;
        }
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void OnIdleTimeout(object? sender, EventArgs e)
    {
        if (IsBusy || IsLoading) { RestartIdleTimer(); return; }
        if (IsModelLoaded) UnloadModel();
        else _idleTimer?.Stop();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText;
        InputText = "";
        if (_session is null && !await EnsureEngineAsync())
        {
            InputText = text; // preserve the draft on a failed reload
            return;
        }
        await RunTurnAsync(text, showUserLine: true);
    }

    private async Task RunTurnAsync(string text, bool showUserLine)
    {
        if (_session is null) return;
        CancelTurn();
        _turnCts = new CancellationTokenSource();
        IsBusy = true;
        if (showUserLine)
            Transcript.Add(new ChatLine("> ", text, ChatLine.UserBrush));

        var streamed = new System.Text.StringBuilder();
        var line = new ChatLine("", "", ChatLine.CoachBrush);
        Transcript.Add(line);
        try
        {
            var reply = await _session.AskAsync(
                text,
                token => Dispatcher.UIThread.Post(() =>
                {
                    streamed.Append(token);
                    line.Text = streamed.ToString();
                }),
                _turnCts.Token);
            line.Text = reply;
        }
        catch (OperationCanceledException)
        {
            line.Text = streamed.Length > 0 ? streamed.ToString() : "(cancelled)";
        }
        catch (Exception ex)
        {
            line.Text = $"coach error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            if (IsLocalAiMode) RestartIdleTimer();
        }
    }

    private void CancelTurn()
    {
        _turnCts?.Cancel();
        _turnCts?.Dispose();
        _turnCts = null;
    }

    private bool CanSend() =>
        !IsAiDisabled && !IsBusy && !IsLoading && !string.IsNullOrWhiteSpace(InputText) &&
        (_session is not null || (IsLocalAiMode && _modelDir is not null));

    public void Dispose()
    {
        CancelTurn();
        _idleTimer?.Stop();
        _idleTimer = null;
        _session?.Dispose();
        _session = null;
        _engine?.Dispose();
        _engine = null;
    }
}
