using SmallTown.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SmallTown.UI
{
    /// <summary>
    /// The front door: play alone, host for friends on the same network, or
    /// join a friend by typing their address on the keypad. Everything is a
    /// slap button, like the rest of the game.
    /// </summary>
    public sealed class MenuController : MonoBehaviour
    {
        const string AddressKey = "smalltown.join.address";

        [SerializeField] TMP_Text addressLabel;
        [SerializeField] TMP_Text statusLabel;
        string address = "";
        bool busy;

        public string Address => address;
        /// <summary>The line of status text currently shown on the board.</summary>
        public string Status { get; private set; } = "";

        public void Configure(TMP_Text addressText, TMP_Text statusText)
        {
            addressLabel = addressText;
            statusLabel = statusText;
        }

        void Start()
        {
            address = PlayerPrefs.GetString(AddressKey, "192.168.1.");
            Refresh();
            if (!string.IsNullOrEmpty(NetLauncher.LastMessage))
            {
                SetStatus(NetLauncher.LastMessage);
                NetLauncher.LastMessage = null;
            }
            else
            {
                SetStatus("Your address: " + NetLauncher.LocalAddress());
            }
        }

        public void PlaySolo()
        {
            if (busy) return;
            busy = true;
            SetStatus("Loading the town...");
            SceneManager.LoadScene(NetLauncher.TownScene);
        }

        public void Host()
        {
            if (busy) return;
            busy = true;
            SetStatus("Starting a game for friends...");
            if (!NetLauncher.StartHost())
            {
                busy = false;
                SetStatus(NetLauncher.LastMessage ?? "Could not host.");
                NetLauncher.LastMessage = null;
            }
        }

        public void Join()
        {
            if (busy) return;
            string target = address.Trim('.');
            if (target.Split('.').Length != 4)
            {
                SetStatus("Type the host's address first, like 192.168.1.20");
                return;
            }
            PlayerPrefs.SetString(AddressKey, address);
            PlayerPrefs.Save();
            busy = true;
            SetStatus("Joining " + target + "...");
            if (!NetLauncher.StartClient(target))
            {
                busy = false;
                SetStatus("Could not start joining.");
            }
        }

        /// <summary>Keypad: appends a digit or a dot.</summary>
        public void Type(string character)
        {
            if (busy || address.Length >= 15) return;
            address += character;
            Refresh();
        }

        public void Backspace()
        {
            if (busy || address.Length == 0) return;
            address = address.Substring(0, address.Length - 1);
            Refresh();
        }

        void Refresh()
        {
            if (addressLabel != null) addressLabel.text = address.Length > 0 ? address : "...";
        }

        void SetStatus(string message)
        {
            Status = message;
            if (statusLabel != null) statusLabel.text = message;
        }
    }
}
