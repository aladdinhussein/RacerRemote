using Microsoft.Maui.ApplicationModel;

namespace RacerRemote.Permissions;

#if ANDROID
public sealed class BluetoothScanAndConnectPermission : global::Microsoft.Maui.ApplicationModel.Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        (global::Android.Manifest.Permission.BluetoothScan, true),
        (global::Android.Manifest.Permission.BluetoothConnect, true),
        (global::Android.Manifest.Permission.AccessFineLocation, true)
    ];
}
#endif
