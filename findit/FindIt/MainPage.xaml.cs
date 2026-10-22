namespace FindIt;

public partial class MainPage : ContentPage
{
	private readonly Data.FindItDatabase _database;
	private Models.LostItem? _editingItem;

	public System.Collections.ObjectModel.ObservableCollection<Models.LostItem> Items { get; } = [];
	public System.Collections.ObjectModel.ObservableCollection<Models.ItemChange> Changes { get; } = [];

	public MainPage(Data.FindItDatabase database)
	{
		InitializeComponent();
		_database = database;
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
