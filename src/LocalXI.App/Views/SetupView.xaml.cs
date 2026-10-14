using System.Windows;
using System.Windows.Controls;
using LocalXI.App.ViewModels;

namespace LocalXI.App.Views;

public partial class SetupView : UserControl
{
    public SetupView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // PasswordBox can't be data-bound (by design), so copy values across by hand.
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is SetupViewModel vm)
        {
            // Read both first: setting one box raises PasswordChanged, which writes both back to the view model.
            var (password, confirm) = (vm.Password, vm.ConfirmPassword);
            PasswordInput.Password = password;
            ConfirmInput.Password = confirm;
        }
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SetupViewModel vm)
        {
            vm.Password = PasswordInput.Password;
            vm.ConfirmPassword = ConfirmInput.Password;
        }
    }
}
