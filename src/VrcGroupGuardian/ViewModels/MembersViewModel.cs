using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VrcGroupGuardian.Models;
using VrcGroupGuardian.Services.Groups;
using VrcGroupGuardian.Services.Members;

namespace VrcGroupGuardian.ViewModels;

public class MembersViewModel : INotifyPropertyChanged, IRefreshable
{
    private readonly IMembersService _membersService;
    private readonly IGroupService _groupService;
    
    private ObservableCollection<GroupMember> _members = new();
    private ObservableCollection<GroupMember> _filteredMembers = new();
    private ObservableCollection<GroupMember> _selectedMembers = new();
    private GroupMember? _selectedMember;
    private string _searchText = "";
    private string? _selectedRoleFilter;
    private string? _selectedStatusFilter;
    private string _statusText = "Ready";

    public MembersViewModel(IMembersService membersService, IGroupService groupService)
    {
        _membersService = membersService;
        _groupService = groupService;
        
        InitializeCommands();
        InitializeFilters();
        InitializeAsync();
    }

    public ObservableCollection<GroupMember> Members
    {
        get => _members;
        set => SetProperty(ref _members, value);
    }

    public ObservableCollection<GroupMember> FilteredMembers
    {
        get => _filteredMembers;
        set => SetProperty(ref _filteredMembers, value);
    }

    public ObservableCollection<GroupMember> SelectedMembers
    {
        get => _selectedMembers;
        set => SetProperty(ref _selectedMembers, value);
    }

    public GroupMember? SelectedMember
    {
        get => _selectedMember;
        set => SetProperty(ref _selectedMember, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilters();
            }
        }
    }

    public string? SelectedRoleFilter
    {
        get => _selectedRoleFilter;
        set
        {
            if (SetProperty(ref _selectedRoleFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string? SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // Filter Options
    public ObservableCollection<string> AvailableRoles { get; } = new();
    public ObservableCollection<string> AvailableStatuses { get; } = new();

    // Computed Properties
    public bool HasSelectedMembers => SelectedMembers.Count > 0;
    public string SelectedMembersText => $"{SelectedMembers.Count} members selected";
    
    // Statistics
    public int TotalMembers => Members.Count;
    public int OnlineMembers => Members.Count(m => m.IsOnline);
    public int OfflineMembers => Members.Count(m => !m.IsOnline);
    public int FilteredCount => FilteredMembers.Count;
    public int SelectedCount => SelectedMembers.Count;

    // Commands
    public ICommand SearchCommand { get; private set; } = null!;
    public ICommand ClearSearchCommand { get; private set; } = null!;
    public ICommand RefreshCommand { get; private set; } = null!;
    public ICommand ExportCommand { get; private set; } = null!;
    public ICommand ViewMemberCommand { get; private set; } = null!;
    public ICommand KickMemberCommand { get; private set; } = null!;
    public ICommand BanMemberCommand { get; private set; } = null!;
    public ICommand BulkKickCommand { get; private set; } = null!;
    public ICommand BulkBanCommand { get; private set; } = null!;
    public ICommand ClearSelectionCommand { get; private set; } = null!;
    public ICommand ToggleSelectionCommand { get; private set; } = null!;

    private void InitializeCommands()
    {
        SearchCommand = new AsyncRelayCommand(Search);
        ClearSearchCommand = new RelayCommand(ClearSearch);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ExportCommand = new AsyncRelayCommand(Export);
        ViewMemberCommand = new AsyncRelayCommand<GroupMember>(ViewMember);
        KickMemberCommand = new AsyncRelayCommand<GroupMember>(KickMember);
        BanMemberCommand = new AsyncRelayCommand<GroupMember>(BanMember);
        BulkKickCommand = new AsyncRelayCommand(BulkKick, () => HasSelectedMembers);
        BulkBanCommand = new AsyncRelayCommand(BulkBan, () => HasSelectedMembers);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        ToggleSelectionCommand = new RelayCommand<GroupMember>(ToggleSelection);
    }

    private void InitializeFilters()
    {
        AvailableRoles.Add("All Roles");
        AvailableRoles.Add("Owner");
        AvailableRoles.Add("Moderator");
        AvailableRoles.Add("Member");
        
        AvailableStatuses.Add("All");
        AvailableStatuses.Add("Online");
        AvailableStatuses.Add("Offline");
        
        SelectedRoleFilter = "All Roles";
        SelectedStatusFilter = "All";
    }

    private async void InitializeAsync()
    {
        await RefreshAsync();
        
        // Subscribe to real-time events
        _membersService.MemberJoined += OnMemberJoined;
        _membersService.MemberLeft += OnMemberLeft;
        _membersService.MemberUpdated += OnMemberUpdated;
        _membersService.MemberKicked += OnMemberKicked;
        _membersService.MemberBanned += OnMemberBanned;
    }

    private async Task Search()
    {
        StatusText = "Searching members...";
        ApplyFilters();
        StatusText = $"Found {FilteredMembers.Count} members";
    }

    private void ClearSearch()
    {
        SearchText = "";
        SelectedRoleFilter = "All Roles";
        SelectedStatusFilter = "All";
        StatusText = "Search cleared";
    }

    public async Task RefreshAsync()
    {
        try
        {
            StatusText = "Loading members...";
            
            var selectedGroup = await _groupService.GetSelectedGroupAsync();
            if (selectedGroup == null)
            {
                StatusText = "No group selected";
                return;
            }

            var members = await _membersService.GetGroupMembersAsync(selectedGroup.GroupId);
            
            Members.Clear();
            foreach (var member in members)
            {
                Members.Add(member);
            }
            
            ApplyFilters();
            UpdateStatistics();
            UpdateAvailableRoles();
            StatusText = $"Loaded {members.Count} members";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private async Task Export()
    {
        try
        {
            StatusText = "Exporting member data...";
            
            var selectedGroup = await _groupService.GetSelectedGroupAsync();
            if (selectedGroup == null)
            {
                StatusText = "No group selected";
                return;
            }

            var exportResult = await _membersService.ExportMembersAsync(selectedGroup.GroupId, FilteredMembers.ToList());
            StatusText = exportResult.Success ? $"Export completed: {exportResult.FilePath}" : exportResult.Message;
        }
        catch (Exception ex)
        {
            StatusText = $"Export error: {ex.Message}";
        }
    }

    private async Task ViewMember(GroupMember? member)
    {
        if (member == null) return;
        
        try
        {
            StatusText = $"Loading details for {member.DisplayName}...";
            
            var detailedMember = await _membersService.GetMemberDetailsAsync(member.UserId);
            if (detailedMember != null)
            {
                // TODO: Show member details dialog or navigate to member detail view
                StatusText = $"Loaded details for {member.DisplayName}";
            }
            else
            {
                StatusText = "Failed to load member details";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private async Task KickMember(GroupMember? member)
    {
        if (member == null) return;
        
        try
        {
            StatusText = $"Kicking {member.DisplayName}...";
            
            var selectedGroup = await _groupService.GetSelectedGroupAsync();
            if (selectedGroup == null)
            {
                StatusText = "No group selected";
                return;
            }

            var result = await _membersService.KickMemberAsync(selectedGroup.GroupId, member.UserId);
            StatusText = result.Success ? $"Successfully kicked {member.DisplayName}" : result.Message;
            
            if (result.Success)
            {
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private async Task BanMember(GroupMember? member)
    {
        if (member == null) return;
        
        try
        {
            StatusText = $"Banning {member.DisplayName}...";
            
            var selectedGroup = await _groupService.GetSelectedGroupAsync();
            if (selectedGroup == null)
            {
                StatusText = "No group selected";
                return;
            }

            var result = await _membersService.BanMemberAsync(selectedGroup.GroupId, member.UserId);
            StatusText = result.Success ? $"Successfully banned {member.DisplayName}" : result.Message;
            
            if (result.Success)
            {
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private async Task BulkKick()
    {
        if (!HasSelectedMembers) return;
        
        try
        {
            StatusText = $"Kicking {SelectedMembers.Count} members...";
            
            var selectedGroup = await _groupService.GetSelectedGroupAsync();
            if (selectedGroup == null)
            {
                StatusText = "No group selected";
                return;
            }

            var memberIds = SelectedMembers.Select(m => m.UserId).ToList();
            var result = await _membersService.BulkKickMembersAsync(selectedGroup.GroupId, memberIds);
            
            StatusText = result.Success 
                ? $"Successfully kicked {result.SuccessCount} members" 
                : $"Kicked {result.SuccessCount}/{memberIds.Count} members - {result.Message}";
            
            ClearSelection();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private async Task BulkBan()
    {
        if (!HasSelectedMembers) return;
        
        try
        {
            StatusText = $"Banning {SelectedMembers.Count} members...";
            
            var selectedGroup = await _groupService.GetSelectedGroupAsync();
            if (selectedGroup == null)
            {
                StatusText = "No group selected";
                return;
            }

            var memberIds = SelectedMembers.Select(m => m.UserId).ToList();
            var result = await _membersService.BulkBanMembersAsync(selectedGroup.GroupId, memberIds);
            
            StatusText = result.Success 
                ? $"Successfully banned {result.SuccessCount} members" 
                : $"Banned {result.SuccessCount}/{memberIds.Count} members - {result.Message}";
            
            ClearSelection();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private void ClearSelection()
    {
        SelectedMembers.Clear();
        UpdateSelectionStatistics();
    }

    private void ToggleSelection(GroupMember? member)
    {
        if (member == null) return;
        
        if (SelectedMembers.Contains(member))
        {
            SelectedMembers.Remove(member);
        }
        else
        {
            SelectedMembers.Add(member);
        }
        
        UpdateSelectionStatistics();
    }

    private void ApplyFilters()
    {
        var filtered = Members.AsEnumerable();
        
        // Apply search text filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var searchLower = SearchText.ToLower();
            filtered = filtered.Where(m => 
                m.DisplayName.ToLower().Contains(searchLower) ||
                m.Username.ToLower().Contains(searchLower));
        }
        
        // Apply role filter
        if (!string.IsNullOrWhiteSpace(SelectedRoleFilter) && SelectedRoleFilter != "All Roles")
        {
            filtered = filtered.Where(m => m.Roles.Contains(SelectedRoleFilter));
        }
        
        // Apply status filter
        if (!string.IsNullOrWhiteSpace(SelectedStatusFilter) && SelectedStatusFilter != "All")
        {
            var isOnlineFilter = SelectedStatusFilter == "Online";
            filtered = filtered.Where(m => m.IsOnline == isOnlineFilter);
        }
        
        FilteredMembers.Clear();
        foreach (var member in filtered.OrderBy(m => m.DisplayName))
        {
            FilteredMembers.Add(member);
        }
        
        UpdateStatistics();
    }

    private void UpdateAvailableRoles()
    {
        var currentRoles = Members.SelectMany(m => m.Roles).Distinct().ToList();
        
        // Keep "All Roles" at the top
        var allRolesItem = AvailableRoles.FirstOrDefault();
        AvailableRoles.Clear();
        
        if (allRolesItem != null)
        {
            AvailableRoles.Add(allRolesItem);
        }
        
        foreach (var role in currentRoles.OrderBy(r => r))
        {
            if (!AvailableRoles.Contains(role))
            {
                AvailableRoles.Add(role);
            }
        }
    }

    private void UpdateStatistics()
    {
        OnPropertyChanged(nameof(TotalMembers));
        OnPropertyChanged(nameof(OnlineMembers));
        OnPropertyChanged(nameof(OfflineMembers));
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void UpdateSelectionStatistics()
    {
        OnPropertyChanged(nameof(HasSelectedMembers));
        OnPropertyChanged(nameof(SelectedMembersText));
        OnPropertyChanged(nameof(SelectedCount));
        
        // Update command can execute states
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnMemberJoined(object? sender, MemberJoinedEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Members.Add(e.Member);
            ApplyFilters();
            UpdateStatistics();
            StatusText = $"Member joined: {e.Member.DisplayName}";
        });
    }

    private void OnMemberLeft(object? sender, MemberLeftEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var memberToRemove = Members.FirstOrDefault(m => m.UserId == e.Member.UserId);
            if (memberToRemove != null)
            {
                Members.Remove(memberToRemove);
                SelectedMembers.Remove(memberToRemove);
                ApplyFilters();
                UpdateStatistics();
                UpdateSelectionStatistics();
                StatusText = $"Member left: {e.Member.DisplayName}";
            }
        });
    }

    private void OnMemberUpdated(object? sender, MemberUpdatedEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var existingMember = Members.FirstOrDefault(m => m.UserId == e.NewMember.UserId);
            if (existingMember != null)
            {
                var index = Members.IndexOf(existingMember);
                Members[index] = e.NewMember;
                
                // Update selected members collection if this member was selected
                if (SelectedMembers.Contains(existingMember))
                {
                    var selectedIndex = SelectedMembers.IndexOf(existingMember);
                    SelectedMembers[selectedIndex] = e.NewMember;
                }
                
                ApplyFilters();
                UpdateStatistics();
                StatusText = $"Member updated: {e.NewMember.DisplayName}";
            }
        });
    }

    private void OnMemberKicked(object? sender, MemberActionEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var memberToRemove = Members.FirstOrDefault(m => m.UserId == e.UserId);
            if (memberToRemove != null)
            {
                Members.Remove(memberToRemove);
                SelectedMembers.Remove(memberToRemove);
                ApplyFilters();
                UpdateStatistics();
                UpdateSelectionStatistics();
                StatusText = $"Member kicked: {memberToRemove.DisplayName}";
            }
        });
    }

    private void OnMemberBanned(object? sender, MemberActionEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var memberToRemove = Members.FirstOrDefault(m => m.UserId == e.UserId);
            if (memberToRemove != null)
            {
                Members.Remove(memberToRemove);
                SelectedMembers.Remove(memberToRemove);
                ApplyFilters();
                UpdateStatistics();
                UpdateSelectionStatistics();
                StatusText = $"Member banned: {memberToRemove.DisplayName}";
            }
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