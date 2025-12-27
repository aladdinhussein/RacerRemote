namespace RacerRemote.Services;

public sealed class ThumbtrollerMixer
{
    // Thumbtroller constants (from Thumbtroller firmware game_logic.h)
    private const int AdcMin = 0;
    private const int AdcMax = 3280;
    private const int AdcCenter = (AdcMax - AdcMin) / 2; // 1640
    private const int DeadzoneCounts = 150;
    private const int MaxSpeed = 80;
    private const int MinMovingSpeed = 42;

    private int _prevMixerX;
    private int _prevMixerY;

    private readonly int[] _adcHistoryX = new int[3];
    private readonly int[] _adcHistoryY = new int[3];
    private int _historyIndex;

    public void Reset()
    {
        _prevMixerX = 0;
        _prevMixerY = 0;
        Array.Clear(_adcHistoryX);
        Array.Clear(_adcHistoryY);
        _historyIndex = 0;
    }

    // Returns the 8-byte packet Thumbtroller sends to the Racer:
    // [speedA, dirA, speedB, dirB, duration, 0,0,0]
    public byte[] ComputePacket(double throttleStickY, double turnStickX)
    {
        // Thumbtroller mode 0 uses ADC X for speed and ADC Y for turning.
        // Its physical stick reports forward as ADC < center (negative mixer_x).
        // Our UI uses forward as +Y, so invert to match: forward => negative mixer_x.
        var adcX = ToAdc(-throttleStickY);
        var adcY = ToAdc(+turnStickX);

        // Thumbtroller uses a single shared history index for both axes.
        var smoothedX = SmoothAdcWithSharedIndex(adcX, _adcHistoryX);
        var smoothedY = SmoothAdcWithSharedIndex(adcY, _adcHistoryY);

        var rawMixerX = MapSpeed(smoothedX, MaxSpeed);

        var speedFactor = Math.Abs(rawMixerX) / (float)MaxSpeed; // 0..1

        // More turn authority at low speed + slightly stronger at high speed.
        // This makes it easier to steering-correct without having to go fast.
        var turnGain = 1.15f + (1.0f - speedFactor) * 0.45f; // ~1.6 at 0 speed -> ~1.15 at max

        var curveMax = (int)((speedFactor * speedFactor * 28f) + 15f);
        if (curveMax > 65) curveMax = 65;

        var rawMixerY = MapJoystick(smoothedY, curveMax, 1.0f);
        rawMixerY = (int)(rawMixerY * turnGain);

        // Exponential smoothing: keep throttle stable, make steering a bit snappier.
        // When the stick returns to center, stop immediately (avoid "coast" from smoothing).
        var mixerX = rawMixerX == 0 ? 0 : SmoothMixer(rawMixerX, _prevMixerX, 0.85f);
        var mixerY = SmoothMixer(rawMixerY, _prevMixerY, 0.72f);

        // Progressive turning limits: allow more at low speed.
        var maxTurn = (int)(22 + (speedFactor * 38)); // 22..60
        if (mixerY > maxTurn) mixerY = maxTurn;
        if (mixerY < -maxTurn) mixerY = -maxTurn;

        _prevMixerX = rawMixerX == 0 ? 0 : mixerX;
        _prevMixerY = mixerY;

        // Build speed + direction (Thumbtroller encodes 1=forward, 0=reverse)
        byte direction;
        int speed;
        int speedA;
        int speedB;

        if (mixerX < 0)
        {
            // Forward
            speed = Math.Abs(mixerX) + 6; // Thumbtroller hack
            direction = 1;
            speedA = speed + mixerY;
            speedB = speed - mixerY;
        }
        else
        {
            // Reverse
            speed = Math.Abs(mixerX) + 6; // Thumbtroller hack
            direction = 0;
            speedA = speed + mixerY;
            speedB = speed - mixerY;
        }

        // Racer won't move reliably below a certain PWM. If we're commanding movement,
        // enforce a minimum per-wheel speed, while preserving steering differential.
        // Only apply this when attempting to drive straight; when steering we allow lower
        // per-wheel values so the differential steering doesn't get overridden.
        if (mixerX != 0 && mixerY == 0)
        {
            speedA = ApplyMinMovingSpeed(speedA);
            speedB = ApplyMinMovingSpeed(speedB);
        }

        // Sanitize (Thumbtroller only caps upper bound)
        if (speedA > 100) speedA = 100;
        if (speedB > 100) speedB = 100;

        // Thumbtroller sends abs() speeds and shared direction in mode 0
        var packet = new byte[8];
        packet[0] = (byte)Math.Abs(speedA);
        packet[1] = direction;
        packet[2] = (byte)Math.Abs(speedB);
        packet[3] = direction;
        packet[4] = 2; // duration ticks; racer firmware treats as *100ms
        packet[5] = 0;
        packet[6] = 0;
        packet[7] = 0;

        return packet;
    }

    private int SmoothAdcWithSharedIndex(int newReading, int[] history)
    {
        history[_historyIndex] = newReading;
        _historyIndex = (_historyIndex + 1) % 3;

        var sum = 0;
        for (var i = 0; i < 3; i++) sum += history[i];
        return sum / 3;
    }

    private static int SmoothMixer(int newValue, int prevValue, float smoothingFactor)
    {
        return (int)(prevValue * smoothingFactor + newValue * (1.0f - smoothingFactor));
    }

    private static int MapSpeed(int adcValue, int maxValue)
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

        const float curveFactor = 0.6f;
        var curved = MathF.Pow(MathF.Abs(normalized), curveFactor) * (normalized < 0 ? -1 : 1);

        curved = curved / 0.85f;
        if (MathF.Abs(curved) > 1.0f) curved = curved > 0 ? 1.0f : -1.0f;

        return (int)(curved * maxValue);
    }

    private static int MapJoystick(int adcValue, int maxValue, float curveFactor)
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

        var curved = MathF.Pow(MathF.Abs(normalized), curveFactor) * (normalized < 0 ? -1 : 1);
        return (int)(curved * maxValue);
    }

    private static int ToAdc(double normalized)
    {
        normalized = Math.Clamp(normalized, -1, 1);
        var halfRange = (AdcMax - AdcMin) / 2.0;
        return (int)Math.Round(AdcCenter + (normalized * halfRange));
    }

    private static int ApplyMinMovingSpeed(int wheelSpeed)
    {
        var abs = Math.Abs(wheelSpeed);
        if (abs == 0)
        {
            return 0;
        }

        if (abs < MinMovingSpeed)
        {
            return wheelSpeed < 0 ? -MinMovingSpeed : MinMovingSpeed;
        }

        return wheelSpeed;
    }
}
