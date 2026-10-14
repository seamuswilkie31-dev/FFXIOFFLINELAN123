using System.Windows;
using System.Windows.Controls;
using LocalXI.App.ViewModels;

namespace LocalXI.App.Views;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LogsViewModel oldVm)
        {
            oldVm.TextAppended -= ScrollToEnd;
        }

        if (e.NewValue is LogsViewModel newVm)
        {
            newVm.TextAppended += ScrollToEnd;
            ScrollToEnd();
        }
    }

    private void ScrollToEnd() => Dispatcher.BeginInvoke(Output.ScrollToEnd);
}
