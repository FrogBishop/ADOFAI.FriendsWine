using System;

namespace FriendsWine
{
    public struct StretchScale
    {
        public readonly float X;
        public readonly float Y;
        public StretchScale(float x, float y) { X = x; Y = y; }
        public static readonly StretchScale Identity = new StretchScale(1, 1);
    }

    // Pure math: independent of Unity, game timeScale, scene state, and frame rate.
    public static class BeatMath
    {
        public const double Bpm = 130.0;
        public const double SecondsPerBeat = 60.0 / Bpm;

        public static StretchScale Evaluate(double elapsed, float vx, float vy, float hx, float hy)
        {
            if (elapsed <= 0 || double.IsNaN(elapsed) || double.IsInfinity(elapsed))
                return StretchScale.Identity;
            double beat = elapsed / SecondsPerBeat;
            double phase = (beat - Math.Floor(beat)) * 4.0;
            bool vertical = phase < 2;
            double half = vertical ? phase : phase - 2;
            float amount = (float)(half <= 1 ? half : 2 - half);
            return new StretchScale(1 + ((vertical ? vx : hx) - 1) * amount,
                                    1 + ((vertical ? vy : hy) - 1) * amount);
        }

        public static bool ValidFactor(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;
        }
    }
}
