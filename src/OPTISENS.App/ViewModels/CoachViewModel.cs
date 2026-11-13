using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OPTISENS.Ai;

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
    private Lfm3Engine? _engine;
    private CoachSession? _session;
    private ToolDispatcher? _dispatcher;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _inputText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isBusy;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusBanner = "";

    public ObservableCollection<ChatLine> Transcript { get; } = new();

    public event Action<double>? Cm360Adjusted;

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
            StatusBanner = "AI disabled (--skip-ai).";
            return;
        }

        IsLoading = true;
        StatusBanner = "Loading LFM3-1.7B coach model…";
        try
        {
            await Task.Run(() =>
            {
                _engine = new Lfm3Engine(modelDir);
                _dispatcher = new ToolDispatcher(initialCm360, dpi, boundsLo, boundsHi);
                _dispatcher.Cm360Changed += cm => Dispatcher.UIThread.Post(() => Cm360Adjusted?.Invoke(cm));
                _session = new CoachSession(_engine, _dispatcher);
                _session.ToolCallObserved += call =>
                {
                    var args = string.Join(", ", call.Arguments.Select(a => a.Value));
                    Dispatcher.UIThread.Post(() =>
                        Transcript.Add(new ChatLine("", $"⚙ {call.Name}({args})", ChatLine.ToolBrush)));
                };
            });

            StatusBanner = "";
            IsLoading = false;

            string introPrompt =
                $"Here is the completed calibration telemetry:\n```json\n{telemetryJson}\n```\nIntroduce the results in <=60 words.";
            await RunTurnAsync(introPrompt, showUserLine: false);
        }
        catch (ModelMissingException ex)
        {
            IsLoading = false;
            StatusBanner = $"{ex.Message}\n{ex.Remediation}";
        }
        catch (Exception ex)
        {
            IsLoading = false;
            StatusBanner = $"AI coach unavailable: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText;
        InputText = "";
        await RunTurnAsync(text, showUserLine: true);
    }

    private async Task RunTurnAsync(string text, bool showUserLine)
    {
        if (_session is null) return;
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
                CancellationToken.None);
            line.Text = reply;
        }
        catch (Exception ex)
        {
            line.Text = $"coach error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSend() => !IsBusy && !IsLoading && _session is not null && !string.IsNullOrWhiteSpace(InputText);

    public void Dispose() => _engine?.Dispose();
}
