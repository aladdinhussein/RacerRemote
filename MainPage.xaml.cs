using Microsoft.Extensions.DependencyInjection;
using RacerRemote.ViewModels;

namespace RacerRemote;

public partial class MainPage : ContentPage
{
    private readonly MainPageViewModel _viewModel;

    public MainPage() : this(ResolveViewModel())
    {
    }

    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is null)
        {
            return;
        }

        Shell.Current.FlyoutIsPresented = true;
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
