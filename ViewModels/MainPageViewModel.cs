using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Storage;
using Plugin.BLE.Abstractions.Contracts;
using RacerRemote.Services;

namespace RacerRemote.ViewModels;

public sealed class MainPageViewModel : INotifyPropertyChanged
{
    private const string LastDeviceIdPreferenceKey = "last_device_id";
    private const string MaxSpeedPreferenceKey = "max_speed";
    private const string DriveModePreferenceKey = "drive_mode";
    private const string SteeringSensitivityPreferenceKey = "steering_sensitivity";

    private readonly RacerBleClient _ble;
    private readonly ThumbtrollerMixer _thumbMixer;
    private readonly ThubtrollerMixer _thubMixer;

    private string _status = "Not connected";
    private bool _isBusy;
    private bool _isConnected;
    private BleDeviceItem? _selectedDevice;

    private double _throttle; // -1..+1 (up is +)
    private double _turn;     // -1..+1 (left is -)
    private double _maxSpeed = 1.0; // 0..1

    private bool _forwardPressed;
    private bool _reversePressed;
    private bool _boostPressed;
    private bool _brakePressed;
    private bool _lightsOn;

    private ThubtrollerDriveMode _driveMode = ThubtrollerDriveMode.ClassicButtonsPlusSteering;

    private int _headlightR = 255;
    private int _headlightG = 255;
    private int _headlightB = 255;

    private CancellationTokenSource? _sendLoopCts;

    private double _steeringSensitivity = 1.0;

    public ObservableCollection<BleDeviceItem> Devices { get; } = new();

    public BleDeviceItem? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (Set(ref _selectedDevice, value))
            {
                ((Command)ConnectCommand).ChangeCanExecute();

                if (value is not null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ConnectAsync();
                        }
                        catch
                        {
                            // ignore
                        }
                    });
                }
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                ((Command)ScanCommand).ChangeCanExecute();
                ((Command)ConnectCommand).ChangeCanExecute();
                ((Command)DisconnectCommand).ChangeCanExecute();
                ((Command)SendHeadlightColorCommand).ChangeCanExecute();
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (Set(ref _isConnected, value))
            {
                ((Command)ConnectCommand).ChangeCanExecute();
                ((Command)DisconnectCommand).ChangeCanExecute();
                ((Command)SendHeadlightColorCommand).ChangeCanExecute();
                OnPropertyChanged(nameof(IsDisconnected));

                ThubLedConnection = value;
                _ = UpdateThubLedEffectsAsync();

                ConnectionChanged?.Invoke(value);
            }
        }
    }

    public bool IsDisconnected => !IsConnected;

    public double Throttle
    {
        get => _throttle;
        set => Set(ref _throttle, Math.Clamp(value, -1, 1));
    }

    public double Turn
    {
        get => _turn;
        set => Set(ref _turn, Math.Clamp(value, -1, 1));
    }

    public double MaxSpeed
    {
        get => _maxSpeed;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Set(ref _maxSpeed, value))
            {
                Preferences.Default.Set(MaxSpeedPreferenceKey, value);
            }
        }
    }

    public double SteeringSensitivity
    {
        get => _steeringSensitivity;
        set
        {
            value = Math.Clamp(value, 0.5, 2.5);
            if (Set(ref _steeringSensitivity, value))
            {
                Preferences.Default.Set(SteeringSensitivityPreferenceKey, value);
            }
        }
    }

    public bool ForwardActive => ForwardPressed && DriveMode == ThubtrollerDriveMode.ClassicButtonsPlusSteering;
    public bool ReverseActive => ReversePressed && DriveMode == ThubtrollerDriveMode.ClassicButtonsPlusSteering;

    public bool ForwardPressed
    {
        get => _forwardPressed;
        set
        {
            if (Set(ref _forwardPressed, value))
            {
                OnPropertyChanged(nameof(ForwardActive));

                if (value)
                {
                    // emulate physical buttons: pressing forward cancels reverse
                    ReversePressed = false;
                }
            }
        }
    }

    public bool ReversePressed
    {
        get => _reversePressed;
        set
        {
            if (Set(ref _reversePressed, value))
            {
                OnPropertyChanged(nameof(ReverseActive));

                if (value)
                {
                    ForwardPressed = false;
                }
            }
        }
    }

    public bool BoostPressed
    {
        get => _boostPressed;
        set
        {
            if (Set(ref _boostPressed, value))
            {
                _thubMixer.BoostPressed = value;
            }
        }
    }

    public bool BrakePressed
    {
        get => _brakePressed;
        set
        {
            if (Set(ref _brakePressed, value))
            {
                _thubMixer.BrakePressed = value;
            }
        }
    }

    public bool LightsOn
    {
        get => _lightsOn;
        set
        {
            if (Set(ref _lightsOn, value))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (!IsConnected)
                        {
                            return;
                        }

                        if (value)
                        {
                            await _ble.SendColorAsync((byte)HeadlightR, (byte)HeadlightG, (byte)HeadlightB, CancellationToken.None);
                        }
                        else
                        {
                            await _ble.SendColorAsync(0, 0, 0, CancellationToken.None);
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                });
            }
        }
    }

    public ThubtrollerDriveMode DriveMode
    {
        get => _driveMode;
        set
        {
            if (Set(ref _driveMode, value))
            {
                Preferences.Default.Set(DriveModePreferenceKey, (int)value);
                _thubMixer.Mode = value;

                // When leaving Classic mode, ensure we don't keep sending fixed forward/reverse.
                if (value != ThubtrollerDriveMode.ClassicButtonsPlusSteering)
                {
                    ForwardPressed = false;
                    ReversePressed = false;
                }

                OnPropertyChanged(nameof(ForwardActive));
                OnPropertyChanged(nameof(ReverseActive));
            }
        }
    }

    public bool IsClassicMode
    {
        get => DriveMode == ThubtrollerDriveMode.ClassicButtonsPlusSteering;
        set
        {
            if (value)
            {
                DriveMode = ThubtrollerDriveMode.ClassicButtonsPlusSteering;
            }
            else
            {
                DriveMode = ThubtrollerDriveMode.AnalogDrive;
            }

            OnPropertyChanged();
        }
    }

    public int HeadlightR { get => _headlightR; set => Set(ref _headlightR, Math.Clamp(value, 0, 255)); }
    public int HeadlightG { get => _headlightG; set => Set(ref _headlightG, Math.Clamp(value, 0, 255)); }
    public int HeadlightB { get => _headlightB; set => Set(ref _headlightB, Math.Clamp(value, 0, 255)); }

    private bool _thubLedConnection;
    private bool _thubLedLowBattery;
    private bool _thubLedMisc;

    public bool ThubLedConnection
    {
        get => _thubLedConnection;
        private set => Set(ref _thubLedConnection, value);
    }

    public bool ThubLedLowBattery
    {
        get => _thubLedLowBattery;
        set
        {
            if (Set(ref _thubLedLowBattery, value))
            {
                _ = UpdateThubLedEffectsAsync();
            }
        }
    }

    public bool ThubLedMisc
    {
        get => _thubLedMisc;
        set
        {
            if (Set(ref _thubLedMisc, value))
            {
                _ = UpdateThubLedEffectsAsync();
            }
        }
    }

    public ICommand ScanCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }

    public ICommand ForwardDownCommand { get; }
    public ICommand ForwardUpCommand { get; }
    public ICommand ReverseDownCommand { get; }
    public ICommand ReverseUpCommand { get; }

    public ICommand SendHeadlightColorCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<bool>? ConnectionChanged;

    public MainPageViewModel(RacerBleClient ble, ThumbtrollerMixer mixer, ThubtrollerMixer thubMixer)
    {
        _ble = ble;
        _thumbMixer = mixer;
        _thubMixer = thubMixer;

        _maxSpeed = Math.Clamp(Preferences.Default.Get(MaxSpeedPreferenceKey, 1.0), 0, 1);
        _steeringSensitivity = Math.Clamp(Preferences.Default.Get(SteeringSensitivityPreferenceKey, 1.0), 0.5, 2.5);

        var modeValue = Preferences.Default.Get(DriveModePreferenceKey, (int)ThubtrollerDriveMode.ClassicButtonsPlusSteering);
        if (!Enum.IsDefined(typeof(ThubtrollerDriveMode), modeValue)) modeValue = (int)ThubtrollerDriveMode.ClassicButtonsPlusSteering;
        _driveMode = (ThubtrollerDriveMode)modeValue;
        _thubMixer.Mode = _driveMode;

        ScanCommand = new Command(async () => await ScanAsync(), () => !IsBusy);
        ConnectCommand = new Command(async () => await ConnectAsync(), () => !IsBusy && !IsConnected && SelectedDevice is not null);
        DisconnectCommand = new Command(async () => await DisconnectAsync(), () => !IsBusy && IsConnected);

        ForwardDownCommand = new Command(() =>
        {
            if (DriveMode == ThubtrollerDriveMode.ClassicButtonsPlusSteering)
                ForwardPressed = true;
        });

        ForwardUpCommand = new Command(() =>
        {
            ForwardPressed = false;
        });

        ReverseDownCommand = new Command(() =>
        {
            if (DriveMode == ThubtrollerDriveMode.ClassicButtonsPlusSteering)
                ReversePressed = true;
        });

        ReverseUpCommand = new Command(() =>
        {
            ReversePressed = false;
        });

        SendHeadlightColorCommand = new Command(async () => await SendHeadlightAsync(), () => !IsBusy && IsConnected);
    }

    public async Task InitializeAsync()
    {
        // Best-effort permissions; app still loads even if denied.
        try
        {
#if ANDROID
            await Microsoft.Maui.ApplicationModel.Permissions.RequestAsync<RacerRemote.Permissions.BluetoothScanAndConnectPermission>();
#else
            await Microsoft.Maui.ApplicationModel.Permissions.RequestAsync<Microsoft.Maui.ApplicationModel.Permissions.LocationWhenInUse>();
#endif
        }
        catch
        {
            // ignore
        }

        // Always scan once on startup so the picker is populated.
        _ = Task.Run(async () =>
        {
            try
            {
                await ScanAsync();
            }
            catch
            {
                // ignore
            }
        });

        // Best-effort: auto-connect to the last used racer.
        _ = Task.Run(async () =>
        {
            try
            {
                await AutoConnectLastDeviceAsync();
            }
            catch
            {
                // ignore
            }
            finally
            {
                // Drive navigation from the final connection state.
                ConnectionChanged?.Invoke(IsConnected);
            }
        });

        // If not connected now, show Bluetooth Connection screen.
        ConnectionChanged?.Invoke(IsConnected);
    }

    private async Task AutoConnectLastDeviceAsync()
    {
        if (IsBusy || IsConnected)
        {
            return;
        }

        var lastId = Preferences.Default.Get(LastDeviceIdPreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(lastId) || !Guid.TryParse(lastId, out var lastGuid))
        {
            return;
        }

        // Scan first (keeps the UX predictable + refreshes Devices list)
        await ScanAsync();

        var match = Devices.FirstOrDefault(d => d.Device.Id == lastGuid);
        if (match is null)
        {
            return;
        }

        SelectedDevice = match;
        await ConnectAsync();
    }

    private bool _isScanning;

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (Set(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(IsNotScanning));
            }
        }
    }

    public bool IsNotScanning => !IsScanning;

    public int DeviceCount => Devices.Count;

    private async Task ScanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsScanning = true;
        Status = _ble.IsBluetoothOn ? "Scanning…" : "Bluetooth is off";

        try
        {
            Devices.Clear();
            OnPropertyChanged(nameof(DeviceCount));

            var results = await _ble.ScanAsync(TimeSpan.FromSeconds(6), CancellationToken.None);

            foreach (var device in results)
            {
                Devices.Add(new BleDeviceItem(device));
            }

            OnPropertyChanged(nameof(DeviceCount));
            Status = Devices.Count == 0 ? "No devices found" : "Select a device to connect";
        }
        catch (Exception ex)
        {
            Status = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            IsBusy = false;
        }
    }

    private async Task ConnectAsync()
    {
        if (IsBusy || SelectedDevice is null)
        {
            return;
        }

        // If we are already connected to something else, disconnect first.
        if (IsConnected)
        {
            await DisconnectAsync();
        }

        IsBusy = true;
        Status = "Connecting…";

        try
        {
            await _ble.ConnectAsync(SelectedDevice.Device, CancellationToken.None);
            IsConnected = true;
            Status = "Connected";

            Preferences.Default.Set(LastDeviceIdPreferenceKey, SelectedDevice.Device.Id.ToString());

            _thumbMixer.Reset();
            _thubMixer.Reset();

            StartSendLoop();
        }
        catch (Exception ex)
        {
            IsConnected = false;
            Status = $"Connect failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DisconnectAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Status = "Disconnecting…";

        try
        {
            StopSendLoop();
            await _ble.DisconnectAsync(CancellationToken.None);

            _thumbMixer.Reset();
            _thubMixer.Reset();

            IsConnected = false;
            Status = "Not connected";
        }
        catch (Exception ex)
        {
            Status = $"Disconnect failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartSendLoop()
    {
        StopSendLoop();

        _sendLoopCts = new CancellationTokenSource();
        var token = _sendLoopCts.Token;

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50)); // 20 Hz

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await timer.WaitForNextTickAsync(token);

                    var max = MaxSpeed;

                    // UI currently reports forward/back inverted vs expected by the mixer/device.
                    // Invert here so pushing stick up drives forward.
                    var throttle = -Throttle;
                    var turn = Turn;

                    // Improve steering: more turn authority at low speed and allow pivot turns.
                    var speedFactor = Math.Abs(throttle); // 0..1
                    var turnBoost = 1.15 + (1.0 - speedFactor) * 0.65;

                    // Slight non-linear curve for turn so small inputs still register.
                    var turnCurved = Math.Sign(turn) * Math.Pow(Math.Abs(turn), 0.75);

                    var throttleScaled = throttle * max;
                    var turnScaled = turnCurved * max * turnBoost * SteeringSensitivity;

                    // If nearly stopped, keep turning strong (pivot)
                    if (Math.Abs(throttleScaled) < 0.10)
                    {
                        throttleScaled = 0;
                        turnScaled = Math.Clamp(turnScaled * 1.25, -1.0, 1.0);
                    }

                    var packet = _thumbMixer.ComputePacket(throttleScaled, turnScaled);
                    await _ble.SendMotorPacketAsync(packet, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // ignore transient errors
                }
            }
        }, token);
    }

    private void StopSendLoop()
    {
        try
        {
            _sendLoopCts?.Cancel();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _sendLoopCts?.Dispose();
            _sendLoopCts = null;
        }
    }

    private async Task UpdateThubLedEffectsAsync()
    {
        // Thubtroller firmware has 3 discrete GPIO LEDs:
        // - LED1: connection
        // - LED2: low battery
        // - LED3: misc
        // The mobile app cannot drive the physical Thubtroller LEDs, so we emulate them by
        // driving Racer headlights color as a visible indicator.
        try
        {
            if (!IsConnected)
            {
                return;
            }

            if (!LightsOn)
            {
                return;
            }

            // Priority: low battery (red) > misc (blue) > normal (user-selected)
            if (ThubLedLowBattery)
            {
                await _ble.SendColorAsync(255, 0, 0, CancellationToken.None);
                return;
            }

            if (ThubLedMisc)
            {
                await _ble.SendColorAsync(0, 80, 255, CancellationToken.None);
                return;
            }

            await _ble.SendColorAsync((byte)HeadlightR, (byte)HeadlightG, (byte)HeadlightB, CancellationToken.None);
        }
        catch
        {
            // ignore
        }
    }

    private async Task SendHeadlightAsync()
    {
        try
        {
            await _ble.SendColorAsync((byte)HeadlightR, (byte)HeadlightG, (byte)HeadlightB, CancellationToken.None);
            await UpdateThubLedEffectsAsync();
        }
        catch (Exception ex)
        {
            Status = $"LED send failed: {ex.Message}";
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public sealed class BleDeviceItem
    {
        public IDevice Device { get; }
        public string DisplayName => string.IsNullOrWhiteSpace(Device.Name) ? Device.Id.ToString() : Device.Name;

        public BleDeviceItem(IDevice device)
        {
            Device = device;
        }

        public override string ToString() => DisplayName;
    }
}
