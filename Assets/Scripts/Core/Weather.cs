using System;

namespace Stratum.Core
{
    // Clear spells and showers: rain (snow where it's cold) comes and goes on its own, and some of it is a
    // thunderstorm. The single-player world and the server each run one; the server tells everyone.
    public sealed class WeatherSim
    {
        public const int Clear = 0, Rain = 1, Thunder = 2;
        public int State;
        public float Left = 900;          // seconds until it changes
        readonly Random _r;
        public event Action<int> Changed;

        public WeatherSim(int seed) { _r = new Random(seed); }

        public void Tick(float dt)
        {
            Left -= dt;
            if (Left > 0) return;
            if (State == Clear) Set(_r.NextDouble() < 0.3 ? Thunder : Rain, 300 + (float)_r.NextDouble() * 420);
            else Set(Clear, 600 + (float)_r.NextDouble() * 1200);
        }

        public void Set(int state, float seconds)
        {
            bool changed = state != State;
            State = Math.Clamp(state, 0, 2); Left = seconds;
            if (changed) Changed?.Invoke(State);
        }

        public static string Name(int s) => s == Thunder ? "thunderstorm" : s == Rain ? "rain" : "clear";
        public static int Parse(string s) => s == "rain" ? Rain : s == "thunder" || s == "storm" ? Thunder : s == "clear" || s == "sun" ? Clear : -1;
    }
}
