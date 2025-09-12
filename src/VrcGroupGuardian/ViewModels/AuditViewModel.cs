using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.Audit;

namespace VrcGroupGuardian.ViewModels;

public class AuditViewModel : INotifyPropertyChanged, IRefreshable
{
    private readonly IAuditService _auditService;
    
    private ObservableCollection<AuditRecord> _auditRecords = new();
    private ObservableCollection<AuditRecord> _filteredAuditRecords = new();
    private AuditRecord? _selectedRecord;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private string? _selectedActionFilter;
    private string? _selectedSeverityFilter;
    private string _userFilter = "";
    private string _statusText = "Ready";
    
    // Pagination
    private int _currentPage = 1;
    private int _pageSize = 100;
    private int _totalPages = 1;

    public AuditViewModel(IAuditService auditService)
    {
        _auditService = auditService;
        
        InitializeCommands();
        InitializeFilters();
        InitializeAsync();
    }

    public ObservableCollection<AuditRecord> AuditRecords
    {
        get => _auditRecords;
        set => SetProperty(ref _auditRecords, value);
    }

    public ObservableCollection<AuditRecord> FilteredAuditRecords
    {
        get => _filteredAuditRecords;
        set => SetProperty(ref _filteredAuditRecords, value);
    }

    public AuditRecord? SelectedRecord
    {
        get => _selectedRecord;
        set => SetProperty(ref _selectedRecord, value);
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set => SetProperty(ref _fromDate, value);
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set => SetProperty(ref _toDate, value);
    }

    public string? SelectedActionFilter
    {
        get => _selectedActionFilter;
        set => SetProperty(ref _selectedActionFilter, value);
    }

    public string? SelectedSeverityFilter
    {
        get => _selectedSeverityFilter;
        set => SetProperty(ref _selectedSeverityFilter, value);
    }

    public string UserFilter
    {
        get => _userFilter;
        set => SetProperty(ref _userFilter, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(CurrentPageText));
                OnPropertyChanged(nameof(CanGoToPreviousPage));
                OnPropertyChanged(nameof(CanGoToNextPage));
                ApplyPagination();
            }
        }
    }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (SetProperty(ref _pageSize, value))
            {
                CurrentPage = 1;
                ApplyFiltersAndPagination();
            }
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        set
        {
            if (SetProperty(ref _totalPages, value))
            {
                OnPropertyChanged(nameof(CurrentPageText));
                OnPropertyChanged(nameof(CanGoToPreviousPage));
                OnPropertyChanged(nameof(CanGoToNextPage));
            }
        }
    }

    // Filter Options
    public ObservableCollection<string> AvailableActions { get; } = new();
    public ObservableCollection<string> AvailableSeverities { get; } = new();
    public ObservableCollection<int> AvailablePageSizes { get; } = new() { 50, 100, 250, 500 };

    // Computed Properties
    public string CurrentPageText => $"Page {CurrentPage} of {TotalPages}";
    public bool CanGoToPreviousPage => CurrentPage > 1;
    public bool CanGoToNextPage => CurrentPage < TotalPages;

    // Statistics
    public int TotalRecords => AuditRecords.Count;
    public int TodayRecords => AuditRecords.Count(r => r.Timestamp.Date == DateTime.Today);
    public int WarningRecords => AuditRecords.Count(r => r.Severity == AuditSeverity.Warning);
    public int ErrorRecords => AuditRecords.Count(r => r.Severity == AuditSeverity.Error);
    public int FilteredRecords => FilteredAuditRecords.Count;

    // Commands
    public ICommand ApplyFiltersCommand { get; private set; } = null!;
    public ICommand RefreshCommand { get; private set; } = null!;
    public ICommand ExportCsvCommand { get; private set; } = null!;
    public ICommand ExportJsonCommand { get; private set; } = null!;
    public ICommand ClearOldRecordsCommand { get; private set; } = null!;
    public ICommand ViewDetailsCommand { get; private set; } = null!;
    public ICommand FirstPageCommand { get; private set; } = null!;
    public ICommand PreviousPageCommand { get; private set; } = null!;
    public ICommand NextPageCommand { get; private set; } = null!;
    public ICommand LastPageCommand { get; private set; } = null!;

    private void InitializeCommands()
    {
        ApplyFiltersCommand = new AsyncRelayCommand(ApplyFilters);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsv);
        ExportJsonCommand = new AsyncRelayCommand(ExportJson);
        ClearOldRecordsCommand = new AsyncRelayCommand(ClearOldRecords);
        ViewDetailsCommand = new RelayCommand<AuditRecord>(ViewDetails);
        FirstPageCommand = new RelayCommand(() => CurrentPage = 1);
        PreviousPageCommand = new RelayCommand(() => CurrentPage = Math.Max(1, CurrentPage - 1));
        NextPageCommand = new RelayCommand(() => CurrentPage = Math.Min(TotalPages, CurrentPage + 1));
        LastPageCommand = new RelayCommand(() => CurrentPage = TotalPages);
    }

    private void InitializeFilters()
    {
        AvailableActions.Add("All Actions");
        AvailableActions.Add("Instance Created");
        AvailableActions.Add("Instance Closed");
        AvailableActions.Add("Member Kicked");
        AvailableActions.Add("Member Banned");
        AvailableActions.Add("Policy Updated");
        AvailableActions.Add("Authentication");
        AvailableActions.Add("System Event");
        
        AvailableSeverities.Add("All Severities");
        AvailableSeverities.Add("Info");
        AvailableSeverities.Add("Warning");
        AvailableSeverities.Add("Error");
        
        SelectedActionFilter = "All Actions";
        SelectedSeverityFilter = "All Severities";
        
        // Default to last 7 days
        FromDate = DateTime.Today.AddDays(-7);
        ToDate = DateTime.Today.AddDays(1);
    }

    private async void InitializeAsync()
    {
        await RefreshAsync();
        
        // Subscribe to real-time events
        _auditService.AuditRecordCreated += OnAuditRecordCreated;
    }

    private async Task ApplyFilters()
    {
        StatusText = "Applying filters...";
        ApplyFiltersAndPagination();
        StatusText = $"Found {FilteredRecords} records";
    }

    public async Task RefreshAsync()
    {
        try
        {
            StatusText = "Loading audit records...";
            
            var fromDate = FromDate ?? DateTime.Today.AddDays(-30);
            var toDate = ToDate ?? DateTime.Today.AddDays(1);
            
            var records = await _auditService.GetAuditRecordsAsync(fromDate, toDate);
            
            AuditRecords.Clear();
            foreach (var record in records.OrderByDescending(r => r.Timestamp))
            {
                AuditRecords.Add(record);
            }
            
            UpdateAvailableActions();
            ApplyFiltersAndPagination();
            UpdateStatistics();
            StatusText = $"Loaded {records.Count} records";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private async Task ExportCsv()
    {
        try
        {
            StatusText = "Exporting to CSV...";
            
            var fromDate = FromDate ?? DateTime.Today.AddDays(-30);
            var toDate = ToDate ?? DateTime.Today.AddDays(1);
            
            var exportResult = await _auditService.ExportAuditRecordsAsync(
                fromDate, toDate, AuditExportFormat.Csv, GetCurrentFilters());
            
            StatusText = exportResult.Success ? $"CSV exported: {exportResult.FilePath}" : exportResult.Message;
        }
        catch (Exception ex)
        {
            StatusText = $"Export error: {ex.Message}";
        }
    }

    private async Task ExportJson()
    {
        try
        {
            StatusText = "Exporting to JSON...";
            
            var fromDate = FromDate ?? DateTime.Today.AddDays(-30);
            var toDate = ToDate ?? DateTime.Today.AddDays(1);
            
            var exportResult = await _auditService.ExportAuditRecordsAsync(
                fromDate, toDate, AuditExportFormat.Json, GetCurrentFilters());
            
            StatusText = exportResult.Success ? $"JSON exported: {exportResult.FilePath}" : exportResult.Message;
        }
        catch (Exception ex)
        {
            StatusText = $"Export error: {ex.Message}";
        }
    }

    private async Task ClearOldRecords()
    {
        try
        {
            StatusText = "Clearing old records...";
            
            var cutoffDate = DateTime.Today.AddDays(-30);
            var result = await _auditService.ClearOldRecordsAsync(cutoffDate);
            
            StatusText = result.Success 
                ? $"Cleared {result.RecordsDeleted} old records" 
                : result.Message;
            
            if (result.Success)
            {
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Clear error: {ex.Message}";
        }
    }

    private void ViewDetails(AuditRecord? record)
    {
        if (record == null) return;
        
        // TODO: Show detailed audit record dialog with full metadata
        StatusText = $"Viewing details for {record.Action} at {record.Timestamp:MM/dd/yyyy HH:mm:ss}";
    }

    private void ApplyFiltersAndPagination()
    {
        ApplyFiltersInternal();
        ApplyPagination();
    }

    private void ApplyFiltersInternal()
    {
        var filtered = AuditRecords.AsEnumerable();
        
        // Apply date range filter
        if (FromDate.HasValue)
        {
            filtered = filtered.Where(r => r.Timestamp.Date >= FromDate.Value.Date);
        }
        
        if (ToDate.HasValue)
        {
            filtered = filtered.Where(r => r.Timestamp.Date <= ToDate.Value.Date);
        }
        
        // Apply action filter
        if (!string.IsNullOrWhiteSpace(SelectedActionFilter) && SelectedActionFilter != "All Actions")
        {
            filtered = filtered.Where(r => r.Action == SelectedActionFilter);
        }
        
        // Apply severity filter
        if (!string.IsNullOrWhiteSpace(SelectedSeverityFilter) && SelectedSeverityFilter != "All Severities")
        {
            if (Enum.TryParse<AuditSeverity>(SelectedSeverityFilter, out var severity))
            {
                filtered = filtered.Where(r => r.Severity == severity);
            }
        }
        
        // Apply user filter
        if (!string.IsNullOrWhiteSpace(UserFilter))
        {
            var userLower = UserFilter.ToLower();
            filtered = filtered.Where(r => 
                (!string.IsNullOrEmpty(r.Username) && r.Username.ToLower().Contains(userLower)) ||
                (!string.IsNullOrEmpty(r.TargetName) && r.TargetName.ToLower().Contains(userLower)));
        }
        
        var filteredList = filtered.ToList();
        
        // Calculate pagination
        TotalPages = Math.Max(1, (int)Math.Ceiling((double)filteredList.Count / PageSize));
        CurrentPage = Math.Min(CurrentPage, TotalPages);
        
        // Store the filtered results for pagination
        _allFilteredRecords = filteredList;
        
        UpdateStatistics();
    }

    private List<AuditRecord> _allFilteredRecords = new();

    private void ApplyPagination()
    {
        var skip = (CurrentPage - 1) * PageSize;
        var pagedRecords = _allFilteredRecords.Skip(skip).Take(PageSize);
        
        FilteredAuditRecords.Clear();
        foreach (var record in pagedRecords)
        {
            FilteredAuditRecords.Add(record);
        }
    }

    private void UpdateAvailableActions()
    {
        var currentActions = AuditRecords.Select(r => r.Action).Distinct().ToList();
        
        // Keep "All Actions" at the top
        var allActionsItem = AvailableActions.FirstOrDefault();
        AvailableActions.Clear();
        
        if (allActionsItem != null)
        {
            AvailableActions.Add(allActionsItem);
        }
        
        foreach (var action in currentActions.OrderBy(a => a))
        {
            if (!AvailableActions.Contains(action))
            {
                AvailableActions.Add(action);
            }
        }
    }

    private void UpdateStatistics()
    {
        OnPropertyChanged(nameof(TotalRecords));
        OnPropertyChanged(nameof(TodayRecords));
        OnPropertyChanged(nameof(WarningRecords));
        OnPropertyChanged(nameof(ErrorRecords));
        OnPropertyChanged(nameof(FilteredRecords));
    }

    private Dictionary<string, object>? GetCurrentFilters()
    {
        var filters = new Dictionary<string, object>();
        
        if (FromDate.HasValue)
            filters["FromDate"] = FromDate.Value;
        if (ToDate.HasValue)
            filters["ToDate"] = ToDate.Value;
        if (SelectedActionFilter != "All Actions")
            filters["Action"] = SelectedActionFilter;
        if (SelectedSeverityFilter != "All Severities")
            filters["Severity"] = SelectedSeverityFilter;
        if (!string.IsNullOrWhiteSpace(UserFilter))
            filters["Username"] = UserFilter;
            
        return filters.Count > 0 ? filters : null;
    }

    private void OnAuditRecordCreated(object? sender, AuditRecordCreatedEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            // Add new record at the top (most recent first)
            AuditRecords.Insert(0, e.Record);
            
            // Re-apply filters to include new record if it matches
            ApplyFiltersAndPagination();
            UpdateStatistics();
            StatusText = $"New audit record: {e.Record.Action}";
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

// Helper classes for audit filtering and export
public class AuditFilterCriteria
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Action { get; set; }
    public string? Severity { get; set; }
    public string? Username { get; set; }
}

public enum AuditExportFormat
{
    Csv,
    Json
}