using System.Windows.Controls;
using System.Windows.Media;

namespace v2rayN.Base;

public static class ViewHost
{
    public static void Show<TViewModel>(
        ContentControl host,
        TViewModel? viewModel)
        where TViewModel : class
    {
        if (viewModel == null)
        {
            host.Content = null;
            return;
        }

        var view = SimpleViewLocator.Instance.ResolveView(viewModel);
        view?.ViewModel = viewModel;

        // ProSyS: hosts tagged "card" sit inside a rounded panel, so the view must not paint its own page background.
        if (view is Control control && host.Tag as string == "card")
        {
            control.Background = Brushes.Transparent;
        }
        host.Content = view;
    }
}
