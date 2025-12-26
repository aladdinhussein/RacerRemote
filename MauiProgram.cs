using Microsoft.Extensions.Logging;
using RacerRemote.Services;
using RacerRemote.ViewModels;

namespace RacerRemote;

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

		builder.Services.AddSingleton<RacerBleClient>();
		builder.Services.AddSingleton<ThumbtrollerMixer>();
		builder.Services.AddSingleton<ThubtrollerMixer>();
		builder.Services.AddSingleton<MainPageViewModel>();
		builder.Services.AddSingleton<MainPage>();
		builder.Services.AddSingleton<SettingsPage>();

		builder.Services.AddSingleton<BluetoothConnectionNavigator>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
