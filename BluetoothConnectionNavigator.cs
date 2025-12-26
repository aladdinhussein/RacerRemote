using RacerRemote.ViewModels;

namespace RacerRemote;

public sealed class BluetoothConnectionNavigator
{
    private readonly MainPageViewModel _viewModel;

    public BluetoothConnectionNavigator(MainPageViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.ConnectionChanged += OnConnectionChanged;
    }

    private void OnConnectionChanged(bool isConnected)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Shell.Current is null)
            {
                return;
            }

            // Shell sometimes isn't fully ready during startup/DI construction.
            // Wait a tick so navigation doesn't end up on a blank page.
            await Task.Yield();

            if (Shell.Current.CurrentItem is null)
            {
                return;
            }

            var target = isConnected ? "//drive" : "//bluetooth";

            try
            {
                await Shell.Current.GoToAsync(target);
            }
            catch
            {
                // ignore
            }
        });
    }
}
