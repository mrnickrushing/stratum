using UnityEngine;

namespace Stratum.Game
{
    // Player preferences, kept per machine.
    public static class Settings
    {
        public static int ViewDistance { get => PlayerPrefs.GetInt("st_view", 10); set => PlayerPrefs.SetInt("st_view", Mathf.Clamp(value, 4, 20)); }
        public static float Fov { get => PlayerPrefs.GetFloat("st_fov", 75); set => PlayerPrefs.SetFloat("st_fov", Mathf.Clamp(value, 55, 110)); }
        public static float Sensitivity { get => PlayerPrefs.GetFloat("st_sens", 1f); set => PlayerPrefs.SetFloat("st_sens", Mathf.Clamp(value, 0.1f, 4f)); }
        public static bool InvertY { get => PlayerPrefs.GetInt("st_invert", 0) == 1; set => PlayerPrefs.SetInt("st_invert", value ? 1 : 0); }
        public static float Volume { get => PlayerPrefs.GetFloat("st_vol", 0.8f); set => PlayerPrefs.SetFloat("st_vol", Mathf.Clamp01(value)); }
        public static float Music { get => PlayerPrefs.GetFloat("st_music", 0.6f); set => PlayerPrefs.SetFloat("st_music", Mathf.Clamp01(value)); }
        public static int Shadows { get => PlayerPrefs.GetInt("st_shadows", 2); set => PlayerPrefs.SetInt("st_shadows", Mathf.Clamp(value, 0, 2)); }   // off, low, high
        public static bool VSync { get => PlayerPrefs.GetInt("st_vsync", 1) == 1; set => PlayerPrefs.SetInt("st_vsync", value ? 1 : 0); }
        public static bool Fullscreen { get => PlayerPrefs.GetInt("st_full", 1) == 1; set => PlayerPrefs.SetInt("st_full", value ? 1 : 0); }
        public static bool ShowFps { get => PlayerPrefs.GetInt("st_fps", 0) == 1; set => PlayerPrefs.SetInt("st_fps", value ? 1 : 0); }
        public static int VoiceMode { get => PlayerPrefs.GetInt("st_voice", 1); set => PlayerPrefs.SetInt("st_voice", Mathf.Clamp(value, 0, 2)); }   // off, hold V, open mic
        public static float VoiceVolume { get => PlayerPrefs.GetFloat("st_voicevol", 1f); set => PlayerPrefs.SetFloat("st_voicevol", Mathf.Clamp(value, 0, 2)); }
        public static string MicDevice { get => PlayerPrefs.GetString("st_micdev", ""); set => PlayerPrefs.SetString("st_micdev", value ?? ""); }   // "" = the system's default
        public static float MicGain { get => PlayerPrefs.GetFloat("st_micgain", 1f); set => PlayerPrefs.SetFloat("st_micgain", Mathf.Clamp(value, 0.25f, 4)); }
        public static float UiScale { get => PlayerPrefs.GetFloat("st_uiscale", 1f); set => PlayerPrefs.SetFloat("st_uiscale", Mathf.Clamp(value, 0.8f, 1.3f)); }   // menus and HUD size
        public static bool Subtitles { get => PlayerPrefs.GetInt("st_subs", 0) == 1; set => PlayerPrefs.SetInt("st_subs", value ? 1 : 0); }
        public static bool ViewBob { get => PlayerPrefs.GetInt("st_bob", 1) == 1; set => PlayerPrefs.SetInt("st_bob", value ? 1 : 0); }
    }
}
