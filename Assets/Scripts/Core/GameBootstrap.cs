using UnityEngine;

namespace TractorSim
{
    /// <summary>
    /// Entry point. The scene can be completely empty: after it loads, this creates the GameManager,
    /// which builds the world, tractor, UI and audio in code. No editor setup is required.
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<GameManager>() != null) return;
            var go = new GameObject("GameManager");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<GameManager>();
        }
    }
}
