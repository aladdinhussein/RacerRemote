# RacerRemote (.NET MAUI)

Mobile joystick controller for the Racer https://github.com/StuckAtPrototype/Racer.

## UI / controls

The main screen provides:

- **Drive joystick**
  - X axis: `Turn`
  - Y axis: `Throttle`
  - Includes configurable **dead-zone** and optional **12-bit quantization** (see `Controls/JoystickView.cs`).
- **Speed slider (top)**: `MaxSpeed` (limits throttle output)
- **Steer slider (bottom)**: `SteeringSensitivity` (scales turning)


## BLE protocol 
Payload format:

- `byte 0`: Motor A speed (0–100)
- `byte 1`: Motor A direction (0 = forward, 1 = backward)
- `byte 2`: Motor B speed (0–100)
- `byte 3`: Motor B direction (0 = forward, 1 = backward)
- `byte 4`: Duration ticks (firmware currently multiplies by 100ms)

## Joystick mapping

- Joystick (vertical): **Throttle** (forward/back)
- Joystick (horizontal): **Turn**

Differential drive mixing:

- `left = throttle + turn` → Motor A
- `right = throttle - turn` → Motor B

## Run / build

### Windows (dev sanity)

This builds the shared code and the Windows target:

- `dotnet build -f net10.0-windows10.0.19041.0`

### Android (phone)

You need an Android SDK installation (Android Studio or command line tools). If `dotnet build -f net10.0-android` complains about missing Android SDK, set `AndroidSdkDirectory` or install Android Studio.

Typical:

- Install Android Studio
- Install SDK Platform + Build Tools + Platform Tools
- Ensure `ANDROID_SDK_ROOT` is set, or set MSBuild property `AndroidSdkDirectory`

Then:

- `dotnet build -f net10.0-android`

## Screenshot

![Screenshot](./Resources/Images/screenshot.jpg)

