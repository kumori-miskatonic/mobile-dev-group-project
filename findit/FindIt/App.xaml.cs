using Microsoft.Extensions.DependencyInjection;

using FindIt.Data;
using FindIt.Services;

namespace FindIt;

public partial class App : Application
{
	private readonly FindItDatabase _database;
	private readonly SyncService _syncService;

	public App(FindItDatabase database, SyncService syncService)
	{
		InitializeComponent();
		_database = database;
		_syncService = syncService;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage(_database, _syncService));
	}
}
