namespace FindIt;

public partial class MainPage : ContentPage
{
	private readonly Data.FindItDatabase _database;
	private readonly Services.SyncService _syncService;
	private Models.LostItem? _editingItem;
	private bool _syncInProgress;
	private bool _connectivitySubscribed;
	private const string ServerUrlPreference = "findit-server-url";

	public System.Collections.ObjectModel.ObservableCollection<Models.LostItem> Items { get; } = [];
	public System.Collections.ObjectModel.ObservableCollection<Models.ItemChange> Changes { get; } = [];
	public System.Collections.ObjectModel.ObservableCollection<Models.SyncAuditLog> AuditLogs { get; } = [];

	public MainPage(Data.FindItDatabase database, Services.SyncService syncService)
	{
		InitializeComponent();
		_database = database;
		_syncService = syncService;
		BindingContext = this;
		DateFoundPicker.Date = DateTime.Today;
		ServerUrlEntry.Text = Preferences.Default.Get(ServerUrlPreference, "http://10.0.2.2:5187");
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		if (!_connectivitySubscribed)
		{
			Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
			_connectivitySubscribed = true;
		}
		await LoadDataAsync();
		await RunFullSyncAsync(automatic: true);
	}

	private async void OnSaveClicked(object? sender, EventArgs e)
	{
		if (!ValidateForm())
		{
			await DisplayAlertAsync("Missing information", "Item name, location, and staff member are required.", "OK");
			return;
		}

		var isCreating = _editingItem is null;
		var item = _editingItem ?? new Models.LostItem();
		item.ItemName = ItemNameEntry.Text.Trim();
		item.Description = DescriptionEditor.Text?.Trim() ?? string.Empty;
		item.LocationFound = LocationEntry.Text.Trim();
		item.DateFound = DateFoundPicker.Date ?? DateTime.Today;
		item.Status = StatusPicker.SelectedItem?.ToString() ?? "Unclaimed";
		item.ModifiedBy = ModifiedByEntry.Text.Trim();

		if (isCreating)
			await _database.CreateItemAsync(item);
		else
			await _database.UpdateItemAsync(item);

		ResetForm();
		await LoadDataAsync();
	}

	private void OnEditClicked(object? sender, EventArgs e)
	{
		if (sender is not Button { CommandParameter: Models.LostItem item })
			return;

		_editingItem = item;
		FormTitleLabel.Text = "Update Found Item";
		SaveButton.Text = "Save Changes";
		CancelButton.IsVisible = true;
		ItemNameEntry.Text = item.ItemName;
		DescriptionEditor.Text = item.Description;
		LocationEntry.Text = item.LocationFound;
		DateFoundPicker.Date = item.DateFound;
		StatusPicker.SelectedItem = item.Status;
		ModifiedByEntry.Text = item.ModifiedBy;
		ItemNameEntry.Focus();
	}

	private async void OnDeleteClicked(object? sender, EventArgs e)
	{
		if (sender is not Button { CommandParameter: Models.LostItem item })
			return;

		var modifiedBy = await DisplayPromptAsync(
			"Staff member",
			"Enter the name of the staff member deleting this item:",
			initialValue: item.ModifiedBy);

		if (string.IsNullOrWhiteSpace(modifiedBy))
			return;

		var confirmed = await DisplayAlertAsync(
			"Delete item",
			$"Soft-delete '{item.ItemName}'? The change will remain in the audit log.",
			"Delete",
			"Cancel");

		if (!confirmed)
			return;

		item.ModifiedBy = modifiedBy.Trim();
		await _database.DeleteItemAsync(item);

		if (_editingItem?.Id == item.Id)
			ResetForm();

		await LoadDataAsync();
	}

	private void OnCancelClicked(object? sender, EventArgs e) => ResetForm();

	private async void OnRefreshClicked(object? sender, EventArgs e) => await LoadDataAsync();

	private async void OnSyncClicked(object? sender, EventArgs e)
	{
		await RunFullSyncAsync(automatic: false);
	}

	private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
	{
		if (e.NetworkAccess == NetworkAccess.Internet)
			await RunFullSyncAsync(automatic: true);
		else
		{
			await _database.AddAuditLogAsync(
				"AutomaticSync", "Offline",
				"Automatic sync was attempted but no internet connection was available.");
			SyncStatusLabel.Text = "Automatic sync skipped: device is offline.";
			await LoadDataAsync();
		}
	}

	private async Task RunFullSyncAsync(bool automatic)
	{
		if (_syncInProgress)
			return;

		_syncInProgress = true;
		SyncButton.IsEnabled = false;
		var operation = automatic ? "AutomaticSync" : "ManualSync";
		var serverUrl = ServerUrlEntry.Text?.Trim() ?? string.Empty;
		Preferences.Default.Set(ServerUrlPreference, serverUrl);

		try
		{
			if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
			{
				await _database.AddAuditLogAsync(
					operation, "Offline",
					$"{operation} was attempted but no internet connection was available.");
				SyncStatusLabel.Text = $"{(automatic ? "Automatic" : "Manual")} sync skipped: device is offline.";
				if (!automatic)
					await DisplayAlertAsync("Sync unavailable", "The device is offline. Pending data was preserved.", "OK");
				return;
			}

			await _database.AddAuditLogAsync(operation, "Started", $"Sync started using {serverUrl}.");
			SyncStatusLabel.Text = automatic ? "Automatic sync in progress..." : "Sync in progress...";

			var pending = await _database.GetPendingChangesAsync();
			var pushed = 0;
			var conflicts = 0;
			if (pending.Count > 0)
			{
				var pushResult = await _syncService.PushAsync(serverUrl, pending);
				await _database.ClearPendingChangesAsync(pushResult.AcceptedChangeIds);
				pushed = pushResult.AcceptedChangeIds.Count;
				conflicts = pushResult.Conflicts.Count;
				foreach (var conflict in pushResult.Conflicts)
				{
					await _database.AddAuditLogAsync(
						"ConflictResolution", conflict.Resolution, conflict.Details, conflict.LostItemId);
				}
			}

			var appliedCount = 0;
			if (await _database.GetUnprocessedIncomingCountAsync() > 0)
			{
				var waiting = await _database.ApplyStagedChangesAsync();
				appliedCount += waiting.Inserted + waiting.Updated + waiting.Deleted;
				conflicts += waiting.Ignored;
				await _syncService.AcknowledgePullAsync(serverUrl, waiting.BatchId);
			}

			var lastPulledAt = await _database.GetLastPullCursorAsync();
			var pullResult = await _syncService.PullAsync(serverUrl, lastPulledAt);
			await _database.StagePulledChangesAsync(pullResult);
			var applied = await _database.ApplyStagedChangesAsync();
			appliedCount += applied.Inserted + applied.Updated + applied.Deleted;
			conflicts += applied.Ignored;
			await _syncService.AcknowledgePullAsync(serverUrl, applied.BatchId);

			var details = $"Sync completed: {pushed} pushed, {appliedCount} applied, {conflicts} conflict(s) resolved.";
			await _database.AddAuditLogAsync(operation, "Succeeded", details);
			await UploadPendingAuditLogsAsync(serverUrl);
			SyncStatusLabel.Text = details;

			if (!automatic)
				await DisplayAlertAsync("Sync complete", details, "OK");
		}
		catch (Exception ex)
		{
			await _database.AddAuditLogAsync(operation, "Failed", $"Sync failed: {ex.Message}");
			SyncStatusLabel.Text = $"{(automatic ? "Automatic" : "Manual")} sync failed. Pending data was preserved.";
			if (!automatic)
				await DisplayAlertAsync("Sync failed", ex.Message, "OK");
		}
		finally
		{
			_syncInProgress = false;
			SyncButton.IsEnabled = true;
			await LoadDataAsync();
		}
	}

	private async Task UploadPendingAuditLogsAsync(string serverUrl)
	{
		var logs = await _database.GetPendingAuditLogsAsync();
		if (logs.Count == 0)
			return;

		var result = await _syncService.UploadAuditLogsAsync(serverUrl, logs);
		await _database.MarkAuditLogsSyncedAsync(result.AcceptedLogIds);
	}

	private async void OnPullClicked(object? sender, EventArgs e)
	{
		var waitingToProcess = await _database.GetUnprocessedIncomingCountAsync();
		if (waitingToProcess > 0)
		{
			await DisplayAlertAsync(
				"Downloaded changes waiting",
				$"Apply the existing {waitingToProcess} downloaded change(s) before downloading another batch.",
				"OK");
			return;
		}

		PullButton.IsEnabled = false;
		PullStatusLabel.Text = "Checking the server for changes...";

		try
		{
			var serverUrl = ServerUrlEntry.Text ?? string.Empty;
			var lastPulledAt = await _database.GetLastPullCursorAsync();
			var response = await _syncService.PullAsync(serverUrl, lastPulledAt);
			var downloaded = await _database.StagePulledChangesAsync(response);

			PullStatusLabel.Text = $"Downloaded {downloaded} change(s). Ready to process.";
			await DisplayAlertAsync("Download complete", PullStatusLabel.Text, "OK");
		}
		catch (Exception ex)
		{
			PullStatusLabel.Text = "Pull failed. Existing device data was preserved.";
			await DisplayAlertAsync("Pull failed", ex.Message, "OK");
		}
		finally
		{
			PullButton.IsEnabled = true;
		}
	}

	private async void OnApplyPullClicked(object? sender, EventArgs e)
	{
		ApplyPullButton.IsEnabled = false;
		PullStatusLabel.Text = "Applying downloaded changes...";

		try
		{
			var applied = await _database.ApplyStagedChangesAsync();
			await _syncService.AcknowledgePullAsync(
				ServerUrlEntry.Text ?? string.Empty,
				applied.BatchId);
			await LoadDataAsync();

			PullStatusLabel.Text =
				$"Processing complete: {applied.Inserted} added, {applied.Updated} updated, "
				+ $"{applied.Deleted} deleted, {applied.Ignored} older change(s) ignored.";
			await DisplayAlertAsync("Processing complete", PullStatusLabel.Text, "OK");
		}
		catch (Exception ex)
		{
			PullStatusLabel.Text = "Processing failed. Downloaded data was preserved.";
			await DisplayAlertAsync("Processing failed", ex.Message, "OK");
		}
		finally
		{
			ApplyPullButton.IsEnabled = true;
		}
	}

	private async Task LoadDataAsync()
	{
		var items = await _database.GetItemsAsync();
		var changes = await _database.GetChangesAsync();
		var auditLogs = await _database.GetAuditLogsAsync();

		Items.Clear();
		foreach (var item in items)
			Items.Add(item);

		Changes.Clear();
		foreach (var change in changes)
			Changes.Add(change);

		AuditLogs.Clear();
		foreach (var log in auditLogs)
			AuditLogs.Add(log);

		PendingCountLabel.Text = $"Pending changes: {changes.Count}";
		AuditCountLabel.Text = $"Audit records: {auditLogs.Count} ({auditLogs.Count(log => !log.IsSynced)} waiting to upload)";
	}

	private bool ValidateForm() =>
		!string.IsNullOrWhiteSpace(ItemNameEntry.Text)
		&& !string.IsNullOrWhiteSpace(LocationEntry.Text)
		&& !string.IsNullOrWhiteSpace(ModifiedByEntry.Text);

	private void ResetForm()
	{
		_editingItem = null;
		FormTitleLabel.Text = "Register Found Item";
		SaveButton.Text = "Create Item";
		CancelButton.IsVisible = false;
		ItemNameEntry.Text = string.Empty;
		DescriptionEditor.Text = string.Empty;
		LocationEntry.Text = string.Empty;
		DateFoundPicker.Date = DateTime.Today;
		StatusPicker.SelectedIndex = 0;
	}
}
