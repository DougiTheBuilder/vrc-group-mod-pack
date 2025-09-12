using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VrcGroupGuardian.ViewModels;

namespace VrcGroupGuardian.Views;

public partial class MainWindow : Window
{
    private readonly ILogger _logger = Log.ForContext<MainWindow>();
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        
        InitializeComponent();
        
        // Set DataContext for data binding
        DataContext = _viewModel;
        
        _logger.Information("MainWindow initialized with dependency injection");
        
        // Wire up window events
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _logger.Information("MainWindow loaded, initializing view model");
            
            // Initialize the view model if needed
            if (_viewModel is IRefreshable refreshable)
            {
                await refreshable.RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during MainWindow loading");
            MessageBox.Show($"Error during window initialization: {ex.Message}", 
                "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            _logger.Information("MainWindow closing");
            
            // Dispose of ViewModel if it implements IDisposable
            if (_viewModel is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during MainWindow closing");
            // Don't prevent closing due to disposal errors
        }
    }

    // Helper method for navigation - this will be called by the ViewModel
    public void NavigateToView(object view)
    {
        try
        {
            if (ContentFrame != null)
            {
                ContentFrame.Content = view;
                _logger.Debug("Navigated to view: {ViewType}", view?.GetType().Name ?? "null");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during navigation to view: {ViewType}", view?.GetType().Name ?? "unknown");
        }
    }

    // Error display helper
    public void ShowError(string title, string message)
    {
        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        });
    }

    // Information display helper
    public void ShowInformation(string title, string message)
    {
        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }
}