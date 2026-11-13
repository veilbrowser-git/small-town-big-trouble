using UnityEngine;
using UnityEngine.SceneManagement;

namespace SmallTown.UI
{
    /// <summary>Loads another scene. Wire a PunchButton to Load. Saves first if there is a session.</summary>
    public sealed class SceneLoadButton : MonoBehaviour
    {
        public string sceneName = "Town";

        public void Load()
        {
            if (GameSession.Instance != null) GameSession.Instance.SaveNow();
            if (Application.CanStreamedLevelBeLoaded(sceneName)) SceneManager.LoadScene(sceneName);
            else HudToast.Show("Scene '" + sceneName + "' is not in the build.");
        }
    }
}
