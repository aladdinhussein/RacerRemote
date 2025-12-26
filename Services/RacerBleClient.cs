using System.Collections.Concurrent;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace RacerRemote.Services;

public sealed class RacerBleClient
{
    public static readonly Guid OtaServiceUuid = Guid.Parse("d6f1d96d-594c-4c53-b1c6-244a1dfde6d8");
    public static readonly Guid MotorCommandCharacteristicUuid = Guid.Parse("23408888-1f40-4cd8-9b89-ca8d45f8a5b0");
    public static readonly Guid ColorCommandCharacteristicUuid = Guid.Parse("20408888-1f40-4cd8-9b89-ca8d45f8a5b0");

    private readonly IBluetoothLE _ble;
    private readonly IAdapter _adapter;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private IDevice? _device;
    private ICharacteristic? _motorCharacteristic;
    private ICharacteristic? _colorCharacteristic;

    public bool IsBluetoothOn => _ble.State == BluetoothState.On;
    public bool IsConnected => _device is not null && _adapter.ConnectedDevices.Contains(_device);

    public RacerBleClient()
    {
        _ble = CrossBluetoothLE.Current;
        _adapter = CrossBluetoothLE.Current.Adapter;
    }

    public async Task<IReadOnlyList<IDevice>> ScanAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        if (!IsBluetoothOn)
        {
            return Array.Empty<IDevice>();
        }

        var found = new ConcurrentDictionary<Guid, IDevice>();

        void Handler(object? _, DeviceEventArgs args)
        {
            if (args.Device is null)
            {
                return;
            }

            found.TryAdd(args.Device.Id, args.Device);
        }

        _adapter.DeviceDiscovered += Handler;
        try
        {
            using var timeoutCts = new CancellationTokenSource(duration);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // Some platform implementations scan until cancellation.
            await _adapter.StartScanningForDevicesAsync(cancellationToken: linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        finally
        {
            _adapter.DeviceDiscovered -= Handler;

            if (_adapter.IsScanning)
            {
                try { await _adapter.StopScanningForDevicesAsync(); }
                catch { /* ignore */ }
            }
        }

        return found.Values
            .OrderBy(d => string.IsNullOrWhiteSpace(d.Name) ? 1 : 0)
            .ThenBy(d => d.Name)
            .ToArray();
    }

    public async Task ConnectAsync(IDevice device, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_device is not null)
            {
                await DisconnectAsync(CancellationToken.None);
            }

            await _adapter.ConnectToDeviceAsync(device, cancellationToken: cancellationToken);
            _device = device;

            _motorCharacteristic = await FindCharacteristicAsync(device, MotorCommandCharacteristicUuid, cancellationToken);
            if (_motorCharacteristic is null)
            {
                await DisconnectAsync(CancellationToken.None);
                throw new InvalidOperationException("Motor command characteristic not found on device.");
            }

            // Optional on older targets; present on Racer firmware.
            _colorCharacteristic = await FindCharacteristicAsync(device, ColorCommandCharacteristicUuid, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_device is null)
            {
                return;
            }

            try
            {
                await _adapter.DisconnectDeviceAsync(_device);
            }
            finally
            {
                _motorCharacteristic = null;
                _colorCharacteristic = null;
                _device = null;
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SendMotorCommandAsync(int motorASpeed, int motorADirection, int motorBSpeed, int motorBDirection, byte durationTicks100ms, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_motorCharacteristic is null)
            {
                throw new InvalidOperationException("Not connected.");
            }

            motorASpeed = Math.Clamp(motorASpeed, 0, 100);
            motorBSpeed = Math.Clamp(motorBSpeed, 0, 100);
            motorADirection = motorADirection == 0 ? 0 : 1;
            motorBDirection = motorBDirection == 0 ? 0 : 1;

            // Convenience wrapper for the Thumbtroller-compatible 8-byte packet.
            // Thumbtroller uses 1=forward, 0=reverse for direction.
            var payload = new byte[8];
            payload[0] = (byte)motorASpeed;
            payload[1] = (byte)motorADirection;
            payload[2] = (byte)motorBSpeed;
            payload[3] = (byte)motorBDirection;
            payload[4] = durationTicks100ms;
            payload[5] = 0;
            payload[6] = 0;
            payload[7] = 0;

            await _motorCharacteristic.WriteAsync(payload);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SendMotorPacketAsync(byte[] payload, CancellationToken cancellationToken)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (payload.Length == 0)
        {
            throw new ArgumentException("Payload must not be empty.", nameof(payload));
        }

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_motorCharacteristic is null)
            {
                throw new InvalidOperationException("Not connected.");
            }

            await _motorCharacteristic.WriteAsync(payload);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SendColorAsync(byte r, byte g, byte b, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (_colorCharacteristic is null)
            {
                throw new InvalidOperationException("Color characteristic not available on this device.");
            }

            // Racer firmware expects exactly 3 bytes: [R, G, B]
            var payload = new byte[3] { r, g, b };
            await _colorCharacteristic.WriteAsync(payload);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private static async Task<ICharacteristic?> FindCharacteristicAsync(IDevice device, Guid characteristicId, CancellationToken cancellationToken)
    {
        var services = await device.GetServicesAsync();

        foreach (var service in services)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<ICharacteristic> characteristics;
            try
            {
                characteristics = await service.GetCharacteristicsAsync();
            }
            catch
            {
                continue;
            }

            foreach (var chr in characteristics)
            {
                if (chr.Id == characteristicId)
                {
                    return chr;
                }
            }
        }

        return null;
    }

    private static Task<ICharacteristic?> FindMotorCharacteristicAsync(IDevice device, CancellationToken cancellationToken)
        => FindCharacteristicAsync(device, MotorCommandCharacteristicUuid, cancellationToken);
}
