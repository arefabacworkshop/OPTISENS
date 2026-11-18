using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using OPTISENS.App.ViewModels;

namespace OPTISENS.App.Views;

public partial class CoachTerminalView : UserControl
{
    public CoachTerminalView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is CoachViewModel vm)
                vm.Transcript.CollectionChanged += OnTranscriptChanged;
        };
    }

    private void OnTranscriptChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => TranscriptScroll.ScrollToEnd(), DispatcherPriority.Background);
    }
}
