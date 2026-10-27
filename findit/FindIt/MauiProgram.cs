using Microsoft.Extensions.Logging;

using FindIt.Data;
using FindIt.Services;

namespace FindIt;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		builder.Services.AddSingleton<FindItDatabase>();
		builder.Services.AddSingleton(new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(30)
		});
		builder.Services.AddSingleton<SyncService>();

		return builder.Build();
	}
}
