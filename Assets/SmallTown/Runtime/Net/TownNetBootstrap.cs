using UnityEngine;

namespace SmallTown.Networking
{
    /// <summary>
    /// Guarantees the town always runs inside a session. Arriving from the menu
    /// as host or client, one already exists. Opened directly (the editor, a
    /// test, the gym's TOWN button) there is none, so a solo session starts
    /// here, before any other component's Start, and everything that spawns
    /// shared objects finds the network ready.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class TownNetBootstrap : MonoBehaviour
    {
        void Start()
        {
            if (!Net.Active) NetLauncher.StartSolo();
            if (Net.Active && Net.IsHost && !NetLauncher.Solo)
            {
                HudToast.Show("Hosting. Friends join at " + NetLauncher.LocalAddress() + " port " + NetLauncher.Port + ".");
            }
        }
    }
}
