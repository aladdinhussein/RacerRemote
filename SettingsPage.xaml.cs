using Microsoft.Extensions.DependencyInjection;
using RacerRemote.ViewModels;

namespace RacerRemote;

public partial class SettingsPage : ContentPage
{
    public SettingsPage() : this(ResolveViewModel())
    {
    }

    public SettingsPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private static MainPageViewModel ResolveViewModel()
    {
        var services = Application.Current?.Handler?.MauiContext?.Services;
        if (services is null)
        {
            throw new InvalidOperationException("MAUI services are not available yet.");
        }

        return services.GetRequiredService<MainPageViewModel>();
    }
}
