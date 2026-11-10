using UnityEngine;

namespace TractorSim
{
    [System.Serializable]
    public class GameSettings
    {
        public bool automatic = false;
        public int steeringMode = 0;          // SteeringMode
        public float steerSensitivity = 1f;   // 0.5..2
        public float controlScale = 1f;       // 0.8..1.3
        public int graphics = 1;              // 0 Low, 1 Medium, 2 High, 3 Ultra
        public int fps = 60;                  // 30 / 60
        public float renderScale = 0.85f;     // 0.5..1
        public int timeOfDay = 0;             // 0 day, 1 evening, 2 night
        public float volume = 0.9f;
        public bool showHints = true;
    }

    [System.Serializable]
    public class SaveData
    {
        public float odometerKm;
        public float fuelL = -1f;
        public float damage;
        public float bestTestLapSec;
        public int sessions;
        public long money = 25000;
        public string tractorId = "golu35";
        public int mapId;
        public int deliveries;
        public float bestMountainSec;
        public long missionMask;              // bit i = mission i completed
        public float[] missionBest = new float[20];
    }

    /// <summary>Local persistence (PlayerPrefs + JsonUtility). Survives app restarts.</summary>
    public static class SaveSystem
    {
        const string SettingsKey = "its_settings_v1";
        const string SaveKey = "its_save_v1";

        public static GameSettings Settings { get; private set; }
        public static SaveData Data { get; private set; }

        public static void Load()
        {
            Settings = Read<GameSettings>(SettingsKey) ?? new GameSettings();
            Data = Read<SaveData>(SaveKey) ?? new SaveData();
        }

        static T Read<T>(string key) where T : class
        {
            if (!PlayerPrefs.HasKey(key)) return null;
            try { return JsonUtility.FromJson<T>(PlayerPrefs.GetString(key)); }
            catch (System.Exception e) { Debug.LogWarning("Save data unreadable, resetting: " + e.Message); return null; }
        }

        public static void SaveSettings()
        {
            PlayerPrefs.SetString(SettingsKey, JsonUtility.ToJson(Settings));
            PlayerPrefs.Save();
        }

        public static void SaveProgress()
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
        }
    }
}
