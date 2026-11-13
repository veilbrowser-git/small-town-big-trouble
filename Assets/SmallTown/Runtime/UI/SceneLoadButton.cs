using SmallTown.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SmallTown.UI
{
    /// <summary>
    /// Loads another scene, or returns to the menu. Wire a PunchButton to Load.
    /// Saves first if there is a session. Scenes other than the town are
    /// single-player, so leaving for one ends the session cleanly.
    /// </summary>
    public sealed class SceneLoadButton : MonoBehaviour
    {
        public string sceneName = "Town";
        [Tooltip("Only allow this while playing alone (for example the trip to the movement gym).")]
        public bool soloOnly;

        public void Load()
        {
            if (GameSession.Instance != null) GameSession.Instance.SaveNow();

            if (soloOnly && Net.Active && !NetLauncher.Solo)
            {
                HudToast.Show("That is single-player only. Leave the co-op game first.");
                return;
            }
            if (sceneName == NetLauncher.MenuScene)
            {
                NetLauncher.Leave();
                return;
            }
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                HudToast.Show("Scene '" + sceneName + "' is not in the build.");
                return;
            }
            // Leaving the town ends its session; the town starts a fresh one when you come back.
            if (Net.Active) NetLauncher.ShutdownNow();
            SceneManager.LoadScene(sceneName);
        }
    }
}
