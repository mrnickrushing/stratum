using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Stratum.Game
{
    // How smoothly the game is running on this computer: every frame's time over the last minute or so, summed up
    // as the average, the slow 1% (what hitches feel like) and the worst frame. F3 shows it; F4 writes a report
    // file that can be sent to whoever looks after the game.
    public static class Perf
    {
        const int Keep = 4096;
        static readonly float[] _ms = new float[Keep];
        static readonly float[] _sorted = new float[Keep];
        static int _n, _at, _stalls;
        public static float EntrySeconds;            // how long the last world took to load

        public static void Reset() { _n = _at = _stalls = 0; }

        public static void Frame(float seconds)
        {
            float ms = seconds * 1000f;
            _ms[_at] = ms; _at = (_at + 1) % Keep; if (_n < Keep) _n++;
            if (ms > 100) _stalls++;
        }

        // Average frames a second, the frame rate of the slowest 1% of frames, and the worst frame in milliseconds.
        public static (float avg, float low1, float worst, int stalls, int frames) Summary()
        {
            if (_n == 0) return (0, 0, 0, 0, 0);
            Array.Copy(_ms, _sorted, _n); Array.Sort(_sorted, 0, _n);
            double sum = 0; for (int i = 0; i < _n; i++) sum += _sorted[i];
            int slow = Mathf.Max(1, _n / 100); double slowSum = 0;
            for (int i = _n - slow; i < _n; i++) slowSum += _sorted[i];
            return ((float)(1000.0 * _n / sum), (float)(1000.0 * slow / slowSum), _sorted[_n - 1], _stalls, _n);
        }

        public static string Line()
        {
            var (avg, low, worst, stalls, _) = Summary();
            return $"{avg:0} fps avg   {low:0} fps slowest 1%   worst frame {worst:0} ms   hitches over 100 ms: {stalls}";
        }

        public static string WriteReport(string extra)
        {
            var (avg, low, worst, stalls, frames) = Summary();
            var sb = new StringBuilder();
            sb.AppendLine($"Stratum {Application.version} performance report, {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"Computer: {SystemInfo.operatingSystem}; {SystemInfo.processorType} ({SystemInfo.processorCount} threads); {SystemInfo.systemMemorySize} MB memory");
            sb.AppendLine($"Graphics: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB), {SystemInfo.graphicsDeviceVersion}");
            sb.AppendLine($"Screen: {Screen.width}x{Screen.height}{(Screen.fullScreen ? " full screen" : " window")}, vsync {(QualitySettings.vSyncCount > 0 ? "on" : "off")}");
            sb.AppendLine($"Settings: view distance {Settings.ViewDistance}, shadows {Settings.Shadows}, field of view {Settings.Fov:0}");
            sb.AppendLine($"Over the last {frames} frames: {avg:0.0} fps average, {low:0.0} fps in the slowest 1%, worst frame {worst:0} ms, {stalls} hitches over 100 ms");
            sb.AppendLine($"World entry took {EntrySeconds:0.0} s");
            sb.AppendLine($"Memory in use: {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)} MB (scripts {GC.GetTotalMemory(false) / (1024 * 1024)} MB)");
            if (!string.IsNullOrEmpty(extra)) sb.AppendLine(extra);
            string file = Path.Combine(Application.persistentDataPath, $"performance-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, sb.ToString());
            return file;
        }
    }
}
