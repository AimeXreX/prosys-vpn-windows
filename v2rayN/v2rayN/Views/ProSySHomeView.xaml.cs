using System.Windows.Controls;
using System.Windows.Media.Animation;
using v2rayN.ViewModels;

namespace v2rayN.Views;

/// <summary>Gaming-style home screen. Visual state (glow / scan animations) follows the view model.</summary>
public partial class ProSySHomeView : UserControl
{
    private readonly Storyboard _connected;
    private readonly Storyboard _busy;

    public ProSySHomeView()
    {
        InitializeComponent();
        _connected = (Storyboard)Resources["SbConnected"];
        _busy = (Storyboard)Resources["SbBusy"];
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ProSySHomeViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnVmChanged;
        }
        if (e.NewValue is ProSySHomeViewModel vm)
        {
            vm.PropertyChanged += OnVmChanged;
            Apply(vm);
        }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ProSySHomeViewModel vm && e.PropertyName is nameof(ProSySHomeViewModel.IsConnected) or nameof(ProSySHomeViewModel.IsBusy))
        {
            Dispatcher.BeginInvoke(() => Apply(vm));
        }
    }

    private void Apply(ProSySHomeViewModel vm)
    {
        Toggle(_connected, vm.IsConnected);
        Toggle(_busy, vm.IsBusy);
    }

    private void Toggle(Storyboard storyboard, bool on)
    {
        if (on)
        {
            storyboard.Begin(this, true);
        }
        else
        {
            storyboard.Stop(this);
        }
    }
}
