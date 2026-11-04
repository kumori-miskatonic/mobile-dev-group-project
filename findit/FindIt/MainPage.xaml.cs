namespace FindIt;

public partial class MainPage : ContentPage
{
	private readonly Data.FindItDatabase _database;
	private readonly Services.SyncService _syncService;
	private Models.LostItem? _editingItem;

	public System.Collections.ObjectModel.ObservableCollection<Models.LostItem> Items { get; } = [];
	public System.Collections.ObjectModel.ObservableCollection<Models.ItemChange> Changes { get; } = [];

	public MainPage(Data.FindItDatabase database, Services.SyncService syncService)
	{
		InitializeComponent();
		_database = database;
		_syncService = syncService;
		BindingContext = this;
		DateFoundPicker.Date = DateTime.Today;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await LoadDataAsync();
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
		var pending = await _database.GetPendingChangesAsync();
		if (pending.Count == 0)
		{
			await DisplayAlertAsync("Sync", "There are no pending changes to send.", "OK");
			return;
		}

		SyncButton.IsEnabled = false;
		SyncStatusLabel.Text = $"Sending {pending.Count} pending change(s)...";

		try
		{
			var result = await _syncService.PushAsync(ServerUrlEntry.Text ?? string.Empty, pending);
			await _database.ClearPendingChangesAsync(result.AcceptedChangeIds);
			await LoadDataAsync();

			SyncStatusLabel.Text = $"Sync successful: {result.AcceptedChangeIds.Count} change(s) cleared.";
			await DisplayAlertAsync("Sync complete", "The server accepted the pending changes.", "OK");
		}
		catch (Exception ex)
		{
			SyncStatusLabel.Text = $"Sync failed. {pending.Count} change(s) remain pending.";
			await DisplayAlertAsync("Sync failed", ex.Message, "OK");
		}
		finally
		{
			SyncButton.IsEnabled = true;
		}
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

		Items.Clear();
		foreach (var item in items)
			Items.Add(item);

		Changes.Clear();
		foreach (var change in changes)
			Changes.Add(change);

		PendingCountLabel.Text = $"Pending changes: {changes.Count}";
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
