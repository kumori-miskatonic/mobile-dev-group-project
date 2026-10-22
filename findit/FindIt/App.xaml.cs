using Microsoft.Extensions.DependencyInjection;

using FindIt.Data;

namespace FindIt;

public partial class App : Application
{
	private readonly FindItDatabase _database;

	public App(FindItDatabase database)
	{
		InitializeComponent();
		_database = database;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage(_database));
	}
}
