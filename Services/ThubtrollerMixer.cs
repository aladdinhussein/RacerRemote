namespace RacerRemote.Services;

public enum ThubtrollerDriveMode
{
    /// <summary>
    /// Mimics Thubtroller firmware: buttons select forward/reverse fixed speed; joystick mixes steering.
    /// </summary>
    ClassicButtonsPlusSteering = 0,

    /// <summary>
    /// Continuous throttle + steering (existing Thumbtroller-style behavior).
    /// </summary>
    AnalogDrive = 1,
}

public sealed class ThubtrollerMixer
{
    // Based on Thubtroller firmware/main/main.c
    private const int AdcMin = 0;
    private const int AdcMax = 3280;
    private const int AdcCenter = (AdcMax - AdcMin) / 2;

    private const int DeadzoneCounts = 50;

    // Thubtroller firmware uses MAX_SPEED=20 for the mixer (steering contribution)
    private const int MaxMixer = 20;

    public ThubtrollerDriveMode Mode { get; set; } = ThubtrollerDriveMode.ClassicButtonsPlusSteering;

    // Virtual button behavior from firmware
    public int ForwardSpeed { get; set; } = 50;
    public int ReverseSpeed { get; set; } = -40;
    public int NotMovingSteerBoost { get; set; } = 25;

    // Additional "virtual buttons" (not explicitly implemented in the minimal firmware loop,
    // but common in the hardware/controller semantics and safe to apply on the app side).
    public bool BoostPressed { get; set; }
    public bool BrakePressed { get; set; }

    // Applied when Boost is held/toggled.
    public int BoostDelta { get; set; } = 20;

    // Applied when Brake is held/toggled.
    public int BrakeClampAbs { get; set; } = 20;

    // A small duration value keeps the Racer firmware command timer alive.
    public byte DurationTicks100ms { get; set; } = 2;

    public void Reset()
    {
        // currently stateless
    }

    public byte[] ComputePacket(
        double steeringX,
        bool forwardPressed,
        bool reversePressed,
        double analogThrottleY,
        double maxSpeedScale)
    {
        maxSpeedScale = Math.Clamp(maxSpeedScale, 0, 1);

        // steeringX is -1..+1; map to ADC and then to mixer like firmware
        var adc = ToAdc(steeringX);
        var mixer = MapJoystick(adc);

        var speed = 0;

        if (Mode == ThubtrollerDriveMode.ClassicButtonsPlusSteering)
        {
            if (forwardPressed)
            {
                speed = ForwardSpeed;
            }
            else if (reversePressed)
            {
                speed = ReverseSpeed;
            }
            else
            {
                // Firmware "extra juice" when not moving
                if (mixer > 0) mixer += NotMovingSteerBoost;
                else if (mixer < 0) mixer -= NotMovingSteerBoost;
            }

            if (BoostPressed)
            {
                speed = speed >= 0 ? speed + BoostDelta : speed - BoostDelta;
            }

            if (BrakePressed)
            {
                // Clamp speed magnitude down (lets user safely reduce without removing steering)
                if (speed > BrakeClampAbs) speed = BrakeClampAbs;
                else if (speed < -BrakeClampAbs) speed = -BrakeClampAbs;
            }
        }
        else
        {
            // AnalogDrive: treat analogThrottleY as base speed, steering as mixer.
            // analogThrottleY is -1..+1 (up is +). Positive means forward.
            var scaled = analogThrottleY * maxSpeedScale;
            speed = (int)Math.Round(scaled * 80); // align with existing app feel
        }

        // Apply global scaling to Classic mode too (lets user tame the car)
        speed = (int)Math.Round(speed * maxSpeedScale);
        mixer = (int)Math.Round(mixer * maxSpeedScale);

        var speedA = speed + mixer;
        var speedB = speed - mixer;

        // Racer expects: [speedA, dirA, speedB, dirB, duration, ...]
        // Racer firmware interprets direction 0=forward, 1=backward; existing app uses 1=forward.
        // Keep existing app convention to avoid breaking current behavior.
        // Here we encode forward=1, backward=0 like the existing app does.

        var packet = new byte[8];
        packet[0] = (byte)Math.Abs(ClampPct(speedA));
        packet[1] = (byte)(speedA >= 0 ? 1 : 0);
        packet[2] = (byte)Math.Abs(ClampPct(speedB));
        packet[3] = (byte)(speedB >= 0 ? 1 : 0);
        packet[4] = DurationTicks100ms;
        packet[5] = 0;
        packet[6] = 0;
        packet[7] = 0;
        return packet;
    }

    private static int ClampPct(int v) => Math.Clamp(v, -100, 100);

    private static int MapJoystick(int adcValue)
    {
        if (Math.Abs(adcValue - AdcCenter) < DeadzoneCounts)
        {
            return 0;
        }

        float normalized;
        if (adcValue > AdcCenter)
        {
            normalized = (float)(adcValue - AdcCenter - DeadzoneCounts) / (AdcMax - AdcCenter - DeadzoneCounts);
        }
        else
        {
            normalized = (float)(adcValue - AdcCenter + DeadzoneCounts) / (AdcCenter - AdcMin - DeadzoneCounts);
        }

        const float curveFactor = 1.1f;
        var curved = MathF.Pow(MathF.Abs(normalized), curveFactor) * (normalized < 0 ? -1 : 1);
        return (int)(curved * MaxMixer);
    }

    private static int ToAdc(double normalized)
    {
        normalized = Math.Clamp(normalized, -1, 1);
        var halfRange = (AdcMax - AdcMin) / 2.0;
        return (int)Math.Round(AdcCenter + (normalized * halfRange));
    }
}
