using System.Windows;
using Serilog;
using VrcGroupGuardian.ViewModels;

namespace VrcGroupGuardian.Views;

public partial class SetupWizardView : Window
{
    private readonly ILogger _logger = Log.ForContext<SetupWizardView>();
    private readonly SetupWizardViewModel _viewModel;

    public SetupWizardView(SetupWizardViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        
        InitializeComponent();
        
        DataContext = _viewModel;
        
        _logger.Information("SetupWizardView initialized");
        
        // Wire up events
        Loaded += OnLoaded;
        Closing += OnClosing;
        
        // Wire up password box (XAML binding doesn't work with PasswordBox)
        PasswordBox.PasswordChanged += (s, e) => _viewModel.Password = PasswordBox.Password;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _logger.Information("SetupWizard loaded, initializing");
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during SetupWizard initialization");
            MessageBox.Show($"Error during setup wizard initialization: {ex.Message}", 
                "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _logger.Information("SetupWizard closing");
        
        // If setup is not complete, ask for confirmation
        if (!_viewModel.IsSetupComplete)
        {
            var result = MessageBox.Show(
                "Setup is not complete. Are you sure you want to cancel?", 
                "Cancel Setup", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
            }
        }
    }
}