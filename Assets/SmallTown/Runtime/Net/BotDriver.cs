using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SmallTown.Crime;
using SmallTown.Economy;
using SmallTown.Interaction;
using SmallTown.Items;
using SmallTown.Jobs;
using SmallTown.Locomotion;
using SmallTown.Npc;
using SmallTown.RigInput;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SmallTown.Networking
{
    /// <summary>
    /// Development-only scripted player for automated co-op checks. Several
    /// copies of the game are launched on one machine, each with a bot; they
    /// connect over real networking, play through a scenario together and each
    /// write a report. A script then compares the reports: if two machines
    /// disagree about money, wanted levels or who is holding what, that is a
    /// replication bug.
    ///
    /// Enabled only by the -stbt-bot command-line switch in development builds.
    /// Bots act through the same hands, buttons and requests a player uses;
    /// they just move by teleporting instead of arm-running.
    /// </summary>
    public sealed class BotDriver : MonoBehaviour
    {
        [Serializable]
        public sealed class Report
        {
            public string role = "";
            public int index;
            public ulong clientId;
            public bool finished;
            public List<string> failures = new List<string>();
            public List<string> log = new List<string>();
            public int playersSeen;
            public int cashStart = -1;
            public int cashAfterJob = -1;
            public int cashAfterRobbery = -1;
            public int cashFinal = -1;
            public int wantedAfterRobbery = -1;
            public int maxWantedSeen;
            public int officersPresent = -1;
            public string storeLabelAfterRobbery = "";
            public string orderLabelAtDelivery = "";
            public int cashBundlesSeen = -1;
            public bool wonConflict;
            public bool wasArrested;
            public bool escaped;
            public bool returnedToMenu;
            public string menuMessage = "";
            // Host only: its view of everyone.
            public List<string> hostView = new List<string>();
            public int playersAfterLeave = -1;
            public int itemsHeldByDeparted = -1;
        }

        enum HandMode
        {
            Rest,
            Bag,
            Aim,
        }

        /// <summary>Directly behind the first house: no line of sight from the street, the alley or the store.</summary>
        static readonly Vector3 HidingSpot = new Vector3(-14f, 0.02f, 29.6f);
        static readonly Vector3 RestLeft = new Vector3(-0.2f, -0.5f, 0.2f);
        static readonly Vector3 RestRight = new Vector3(0.2f, -0.5f, 0.2f);

        readonly Report report = new Report();
        string role = "host";
        string scenario = "coop";
        string address = "127.0.0.1";
        string reportPath = "";
        ushort port = NetLauncher.DefaultPort;
        int expectedPlayers = 2;
        int index;

        GameSession session;
        PlayerRig rig;
        ManualRigInputProvider input;
        HandGrabber rightGrabber;
        NetworkPlayer me;
        HandMode handMode = HandMode.Rest;
        Vector3 aimTarget;

        bool IsHost => role == "host";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-stbt-bot");
            if (at < 0 || !Debug.isDebugBuild) return;

            var host = new GameObject("BotDriver");
            DontDestroyOnLoad(host);
            var bot = host.AddComponent<BotDriver>();
            bot.role = Arg(args, "-stbt-bot", "host");
            bot.scenario = Arg(args, "-stbt-scenario", "coop");
            bot.address = Arg(args, "-stbt-join", "127.0.0.1");
            bot.reportPath = Arg(args, "-stbt-report", "");
            bot.port = ushort.Parse(Arg(args, "-stbt-port", NetLauncher.DefaultPort.ToString()));
            bot.expectedPlayers = int.Parse(Arg(args, "-stbt-players", "2"));
            bot.index = int.Parse(Arg(args, "-stbt-index", "0"));
        }

        static string Arg(string[] args, string name, string fallback)
        {
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
        }

        void Start()
        {
            // Every copy on this machine would otherwise share one save file.
            GameSession.PersistenceEnabled = false;
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            report.role = role;
            report.index = index;
            StartCoroutine(Main());
        }

        // ------------------------------------------------------------------ Plumbing

        void Note(string message)
        {
            report.log.Add(Time.realtimeSinceStartup.ToString("0.0") + " " + message);
            Debug.Log("[Bot " + role + index + "] " + message);
        }

        void Fail(string message)
        {
            report.failures.Add(message);
            Debug.LogWarning("[Bot " + role + index + "] FAIL " + message);
        }

        IEnumerator Until(Func<bool> condition, float timeout, string what)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool ok = false;
                try
                {
                    ok = condition();
                }
                catch (Exception e)
                {
                    Fail(what + " threw " + e.GetType().Name + ": " + e.Message);
                    yield break;
                }
                if (ok)
                {
                    Note("ok: " + what);
                    yield break;
                }
                yield return null;
            }
            Fail("timed out: " + what);
        }

        void WriteReport()
        {
            if (string.IsNullOrEmpty(reportPath)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
                File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            }
            catch (Exception e)
            {
                Debug.LogError("[Bot] could not write report: " + e.Message);
            }
        }

        IEnumerator Finish()
        {
            report.finished = true;
            WriteReport();
            yield return new WaitForSeconds(0.3f);
            Application.Quit();
        }

        static string LabelStartingWith(string prefix)
        {
            foreach (TMP_Text label in FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            {
                if (label.text != null && label.text.Contains(prefix)) return label.text;
            }
            return "";
        }

        static string BeatCopPosition()
        {
            foreach (PoliceOfficer officer in FindObjectsByType<PoliceOfficer>(FindObjectsSortMode.None))
            {
                var body = officer.GetComponent<NpcBody>();
                if (body != null && body.Present) return officer.transform.position.ToString("F1");
            }
            return "none";
        }

        static int CountPlayers() => FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None).Length;

        HudView View => session.View;

        void LateUpdate()
        {
            if (rig == null || input == null) return;
            // Drive the "tracked" poses: head upright, left hand at rest, right hand wherever the bot needs it.
            rig.Head.localPosition = new Vector3(0f, 1.6f, 0f);
            rig.Head.localRotation = Quaternion.identity;
            rig.LeftController.localPosition = rig.Head.localPosition + RestLeft;
            rig.LeftController.localRotation = Quaternion.identity;
            Vector3 right = RestRight;
            Quaternion rightRotation = Quaternion.identity;
            if (handMode == HandMode.Bag && session != null && session.Bag != null) right = session.Bag.bodyOffset;
            if (handMode == HandMode.Aim) right = new Vector3(0.2f, -0.1f, 0.3f);
            rig.RightController.localPosition = rig.Head.localPosition + right;
            if (handMode == HandMode.Aim)
            {
                Vector3 toTarget = aimTarget - rig.RightController.position;
                if (toTarget.sqrMagnitude > 0.01f) rightRotation = Quaternion.Inverse(rig.transform.rotation) * Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            }
            rig.RightController.localRotation = rightRotation;
            if (session != null && me != null) report.maxWantedSeen = Mathf.Max(report.maxWantedSeen, View.wantedLevel);

            // Bots do not arm-run. With the rig frozen the hands are exactly where the script
            // puts them and a teleport lands exactly where asked. (Locomotion itself is
            // covered by its own tests.) An arrest briefly takes the rig over; let it.
            var arrest = FindAnyObjectByType<ArrestController>();
            bool arresting = arrest != null && arrest.InProgress;
            if (!arresting && rig.Mode != MovementMode.Frozen) rig.SetMode(MovementMode.Frozen);
        }

        /// <summary>Teleports so the right hand ends up at a world point.</summary>
        void MoveHandTo(Vector3 worldPoint)
        {
            Vector3 offset = rig.RightController.position - rig.Head.position;
            rig.TeleportHead(worldPoint - offset);
        }

        IEnumerator Grab(GrabbableItem item, string what)
        {
            if (item == null)
            {
                Fail("nothing to grab: " + what);
                yield break;
            }
            // Reach for it first, so it sits in the hand rather than trailing behind at a distance.
            MoveHandTo(item.transform.position);
            yield return null;
            yield return null;
            input.rightGrip = 1f;
            yield return null;
            yield return null;
            if (rightGrabber.Held != item) rightGrabber.TryGrabItem(item);
            yield return Until(() => rightGrabber.Held == item, 4f, "holding " + what);
        }

        IEnumerator Release()
        {
            input.rightGrip = 0f;
            yield return Until(() => rightGrabber.Held == null, 3f, "released");
        }

        // ------------------------------------------------------------------ Scenario

        IEnumerator Main()
        {
            yield return null;
            Note("starting as " + role + " index " + index + " scenario " + scenario);
            if (IsHost)
            {
                if (!NetLauncher.StartHost(port)) Fail("StartHost failed");
            }
            else
            {
                yield return new WaitForSeconds(1.5f + index * 0.4f);
                if (!NetLauncher.StartClient(address, port)) Fail("StartClient failed");
            }

            yield return Until(() => SceneManager.GetActiveScene().name == NetLauncher.TownScene && GameSession.Instance != null, 60f, "town loaded");
            session = GameSession.Instance;
            if (session == null)
            {
                yield return Finish();
                yield break;
            }
            rig = session.Rig;
            input = rig.gameObject.AddComponent<ManualRigInputProvider>();
            rig.SetInputOverride(input);
            rightGrabber = rig.HandTransform(Hand.Right).GetComponent<HandGrabber>();

            yield return Until(() =>
            {
                foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
                {
                    if (player.IsOwner) me = player;
                }
                return me != null;
            }, 20f, "own network player spawned");
            if (me != null) report.clientId = me.OwnerClientId;

            yield return Until(() => CountPlayers() == expectedPlayers, 60f, "all " + expectedPlayers + " players present");
            report.playersSeen = CountPlayers();
            yield return new WaitForSeconds(1.5f);
            report.cashStart = View.cash;

            // Spread out so nobody starts inside anybody else.
            rig.TeleportFeet(new Vector3(-16.5f + index * 1.2f, 0.08f, -8.5f));
            yield return new WaitForSeconds(0.5f);

            if (scenario == "probe")
            {
                yield return ProbePhase();
                yield return Finish();
                yield break;
            }

            yield return JobPhase();
            yield return RobberyPhase();
            yield return OutcomePhase();
            yield return ConflictAndLeavePhase();
        }

        // ---- Diagnostic: does an item a client is holding show up in the right place on the host?

        IEnumerator ProbePhase()
        {
            Topping Find()
            {
                Topping best = null;
                foreach (Topping candidate in FindObjectsByType<Topping>(FindObjectsSortMode.None))
                {
                    if (candidate.toppingId == "mushroom") best = candidate;
                }
                return best;
            }

            Topping topping = null;
            yield return Until(() => (topping = Find()) != null, 15f, "probe topping visible");
            if (topping == null) yield break;
            var networkObject = topping.GetComponent<NetworkObject>();
            var body = topping.GetComponent<Rigidbody>();
            Vector3 origin = topping.transform.position;
            if (!IsHost)
            {
                yield return Grab(topping.GetComponent<GrabbableItem>(), "probe topping");
                MoveHandTo(origin + Vector3.up * 2f);
            }
            for (int i = 0; i < 8; i++)
            {
                yield return new WaitForSeconds(0.5f);
                if (topping == null) break;
                Note("probe t=" + (i * 0.5f).ToString("0.0") + " owner=" + networkObject.OwnerClientId + " isOwner=" + networkObject.IsOwner +
                     " kinematic=" + body.isKinematic + " pos=" + topping.transform.position.ToString("F2") +
                     " state=" + topping.GetComponent<GrabbableItem>().State + " holder=" + topping.GetComponent<GrabbableItem>().HolderId +
                     " | serverTime=" + NetworkManager.Singleton.ServerTime.Time.ToString("0.0") + " cop=" + BeatCopPosition());
            }
            if (IsHost) yield return new WaitForSeconds(2f);
        }

        // ---- Pizza: a client takes the order and adds the topping, the host cooks and boxes, the last client delivers.

        IEnumerator JobPhase()
        {
            int lastClient = expectedPlayers - 1;
            if (!IsHost && index == 1)
            {
                yield return new WaitForSeconds(1f);
                PunchButton order = GameObject.Find("ORDERButton").GetComponent<PunchButton>();
                order.Press();
                Note("pressed ORDER");
            }
            yield return Until(() => LabelStartingWith("1. TOPPINGS").Length > 0, 20f, "everyone sees the order start");

            if (!IsHost && index == 1)
            {
                string label = LabelStartingWith("1. TOPPINGS");
                string[] lines = label.Split('\n');
                string[] needed = lines.Length > 1 ? lines[1].Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
                Pizza pizza = null;
                yield return Until(() => (pizza = FindAnyObjectByType<Pizza>()) != null, 10f, "pizza base visible to client");
                foreach (string id in needed)
                {
                    Topping topping = null;
                    yield return Until(() =>
                    {
                        foreach (Topping candidate in FindObjectsByType<Topping>(FindObjectsSortMode.None))
                        {
                            if (candidate.toppingId == id.Trim()) topping = candidate;
                        }
                        return topping != null;
                    }, 10f, "topping " + id + " visible");
                    if (topping == null || pizza == null) continue;
                    yield return Grab(topping.GetComponent<GrabbableItem>(), "topping " + id);
                    MoveHandTo(pizza.transform.position + Vector3.up * 0.1f);
                    yield return new WaitForSeconds(0.8f);
                    // Pressing it on: a client asks the host, which checks the topping really is there.
                    pizza.TryAdd(topping);
                    yield return Until(() => topping == null, 6f, "host accepted topping " + id);
                    input.rightGrip = 0f;
                    yield return new WaitForSeconds(0.3f);
                }
            }

            if (IsHost)
            {
                var job = FindAnyObjectByType<PizzaDeliveryJob>();
                yield return Until(() => job.Current == PizzaDeliveryJob.Stage.Cook, 40f, "pizza fully topped (by a client)");
                Pizza pizza = job.ActivePizza;
                if (pizza != null)
                {
                    yield return Grab(pizza.Item, "pizza for the oven");
                    MoveHandTo(GameObject.Find("OvenZone").transform.position);
                    yield return new WaitForSeconds(0.6f);
                    yield return Release();
                    yield return Until(() => job.Current == PizzaDeliveryJob.Stage.Box, session.Balance.pizzaCookSeconds + 10f, "pizza cooked");
                    yield return Grab(pizza.Item, "pizza for boxing");
                    MoveHandTo(GameObject.Find("BoxingZone").transform.position + Vector3.up * 0.05f);
                    yield return new WaitForSeconds(0.6f);
                    yield return Release();
                    yield return Until(() => job.Current == PizzaDeliveryJob.Stage.Deliver, 10f, "pizza boxed");
                    rig.TeleportFeet(new Vector3(-16.5f, 0.08f, -8.5f));
                }
            }

            yield return Until(() => LabelStartingWith("4. DELIVER").Length > 0, 90f, "everyone sees the delivery stage");
            report.orderLabelAtDelivery = LabelStartingWith("4. DELIVER");

            if (!IsHost && index == lastClient)
            {
                // Take the boxed pizza the host made (ownership passes host -> client) and carry it to the customer.
                Pizza pizza = FindAnyObjectByType<Pizza>();
                yield return Grab(pizza != null ? pizza.Item : null, "boxed pizza made by the host");
                DeliveryCustomer target = null;
                yield return Until(() =>
                {
                    foreach (DeliveryCustomer customer in FindObjectsByType<DeliveryCustomer>(FindObjectsSortMode.None))
                    {
                        Transform beacon = customer.transform.Find("Beacon");
                        if (beacon != null && beacon.gameObject.activeSelf) target = customer;
                    }
                    return target != null;
                }, 10f, "client sees which customer is waiting");
                if (target != null)
                {
                    MoveHandTo(target.Zone.transform.position);
                    yield return new WaitForSeconds(1f);
                }
            }

            int expected = report.cashStart + session.Balance.pizzaPayout;
            yield return Until(() => View.cash >= expected, 30f, "paid for the delivery");
            report.cashAfterJob = View.cash;
            input.rightGrip = 0f;
            yield return new WaitForSeconds(1f);
        }

        // ---- Robbery: client 1 buys a pistol and holds up the store; the host helps itself to the cash; the rest watch.

        IEnumerator RobberyPhase()
        {
            var site = FindAnyObjectByType<RobberySite>();
            Vector3 store = site.Zone.transform.position;
            Vector3 cashier = GameObject.Find("Cashier").transform.position + Vector3.up * 0.8f;

            if (!IsHost && index == 1)
            {
                me.RequestDevMoney(200);
                yield return Until(() => View.cash >= session.Balance.pistolPrice, 10f, "dev money arrived");
                ShopOffer pistolOffer = null;
                foreach (ShopOffer offer in FindObjectsByType<ShopOffer>(FindObjectsSortMode.None))
                {
                    if (offer.unlockId == GameBalance.PistolUnlock) pistolOffer = offer;
                }
                int before = View.cash;
                pistolOffer.GetComponentInChildren<PunchButton>().Press();
                Pistol pistol = null;
                yield return Until(() => (pistol = FindAnyObjectByType<Pistol>()) != null, 10f, "pistol appeared after buying");
                yield return Until(() => View.cash == before - session.Balance.pistolPrice, 10f, "charged for the pistol exactly once");
                if (pistol != null)
                {
                    yield return Grab(pistol.GetComponent<GrabbableItem>(), "pistol");
                    rig.TeleportFeet(new Vector3(store.x + 0.3f, 0.08f, cashier.z));
                    yield return new WaitForSeconds(0.6f);
                    aimTarget = cashier;
                    handMode = HandMode.Aim;
                }
            }
            else if (IsHost)
            {
                rig.TeleportFeet(new Vector3(store.x - 0.6f, 0.08f, store.z - 2.6f));
            }

            yield return Until(() => LabelStartingWith("HOLD-UP").Length > 0 || LabelStartingWith("CLEANED OUT").Length > 0, 40f, "everyone sees the hold-up");

            if (!IsHost && index == 1)
            {
                handMode = HandMode.Rest;
                input.rightGrip = 0f; // drop the pistol to free the hand
                yield return new WaitForSeconds(0.4f);
                yield return TakeCash(2);
            }
            else if (IsHost)
            {
                // Grab one bundle and slip away behind the houses before the alarm goes off.
                yield return TakeCash(1);
                rig.TeleportFeet(HidingSpot);
            }

            yield return Until(() => LabelStartingWith("CLEANED OUT").Length > 0, 40f, "everyone sees the register cleaned out");
            yield return new WaitForSeconds(2.5f);
            report.storeLabelAfterRobbery = LabelStartingWith("CLEANED OUT").Split('\n')[0];
            report.wantedAfterRobbery = View.wantedLevel;
            report.cashAfterRobbery = View.cash;
            int present = 0;
            foreach (PoliceOfficer officer in FindObjectsByType<PoliceOfficer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var body = officer.GetComponent<NpcBody>();
                if (body != null && body.Present) present++;
            }
            report.officersPresent = present;
            int bundles = 0;
            foreach (LootItem loot in LootItem.All)
            {
                if (loot.isCash) bundles++;
            }
            report.cashBundlesSeen = bundles;
            if (IsHost) RecordHostView("after robbery");
        }

        IEnumerator TakeCash(int count)
        {
            for (int taken = 0; taken < count; taken++)
            {
                LootItem cash = null;
                yield return Until(() =>
                {
                    foreach (LootItem loot in LootItem.All)
                    {
                        if (loot.isCash && loot.Item.State == ItemState.Free && loot.Item.CanGrab(rightGrabber)) cash = loot;
                    }
                    return cash != null;
                }, 20f, "a cash bundle to take");
                if (cash == null) yield break;
                MoveHandTo(cash.transform.position);
                yield return null;
                yield return null;
                input.rightGrip = 1f;
                yield return null;
                yield return null;
                if (rightGrabber.Held != cash.Item) rightGrabber.TryGrabItem(cash.Item);
                float deadline = Time.realtimeSinceStartup + 3f;
                while (rightGrabber.Held != cash.Item && Time.realtimeSinceStartup < deadline) yield return null;
                if (rightGrabber.Held != cash.Item)
                {
                    // Somebody else got that one first. That is the rule working; try another.
                    Note("lost the race for a bundle");
                    input.rightGrip = 0f;
                    taken--;
                    yield return new WaitForSeconds(0.3f);
                    continue;
                }
                handMode = HandMode.Bag;
                yield return new WaitForSeconds(0.4f);
                input.rightGrip = 0f;
                yield return Until(() => cash == null || cash.Item.State == ItemState.Stored, 3f, "bundle bagged");
                handMode = HandMode.Rest;
                yield return new WaitForSeconds(0.3f);
            }
        }

        // ---- Afterwards: the host hides and gets away with it; client 1 stands still and is arrested; bystanders stay clean.

        IEnumerator OutcomePhase()
        {
            if (IsHost)
            {
                yield return Until(() => report.maxWantedSeen > 0, 10f, "host is wanted for taking the cash");
                yield return Until(() => View.wantedLevel == 0, session.Balance.escapeSeconds + 40f, "host escaped by staying out of sight");
                report.escaped = View.wantedLevel == 0;
                yield return new WaitForSeconds(1.5f);
            }
            else if (index == 1)
            {
                Vector3 station = FindAnyObjectByType<ArrestController>().ReleasePoint.position;
                yield return Until(() => View.wantedLevel == 0 && Vector3.Distance(rig.Head.position, station) < 6f, 90f, "robber was arrested and released at the station");
                report.wasArrested = View.wantedLevel == 0 && Vector3.Distance(rig.Head.position, station) < 6f;
                yield return new WaitForSeconds(3.5f);
                if (session.Bag.Items.Count != 0) Fail("stolen cash still in the bag after arrest: " + session.Bag.Items.Count);
            }
            else
            {
                yield return new WaitForSeconds(12f);
                if (report.maxWantedSeen != 0) Fail("an innocent bystander became wanted (level " + report.maxWantedSeen + ")");
            }
            report.cashFinal = View.cash;
        }

        // ---- Two clients grab the same thing at the same moment; then one of them drops off the network mid-carry.

        IEnumerator ConflictAndLeavePhase()
        {
            if (IsHost)
            {
                // Once the robber has been through the station, start a second order. Its
                // appearance on the terminal is the cue for the clients' grab contest.
                yield return Until(() =>
                {
                    foreach (PlayerState player in session.Players)
                    {
                        if (!player.IsLocal && player.Wallet.Profile.timesArrested > 0 && !player.Wanted.IsWanted) return true;
                    }
                    return false;
                }, 120f, "host saw the robber get arrested");
                yield return new WaitForSeconds(4f);
                GameObject.Find("ORDERButton").GetComponent<PunchButton>().Press();
                // Wait for client 1 to vanish, then check nothing it was carrying is stuck.
                yield return Until(() => session.Players.Count == expectedPlayers - 1, 150f, "host noticed a player leave");
                yield return new WaitForSeconds(1.5f);
                report.playersAfterLeave = session.Players.Count;
                int stuck = 0;
                foreach (GrabbableItem item in FindObjectsByType<GrabbableItem>(FindObjectsSortMode.None))
                {
                    bool holderPresent = item.HolderId == GrabbableItem.NoHolder || session.FindPlayer(item.HolderId) != null;
                    if (!holderPresent || (item.State != ItemState.Free && item.HolderId == GrabbableItem.NoHolder)) stuck++;
                }
                report.itemsHeldByDeparted = stuck;
                RecordHostView("at the end");
                yield return new WaitForSeconds(expectedPlayers > 2 ? 6f : 1f);
                yield return Finish();
                yield break;
            }

            // Clients: when the second order shows up, everybody goes for the same mushroom.
            yield return Until(() => LabelStartingWith("1. TOPPINGS").Length > 0, 150f, "cue for the grab contest");
            Topping prize = null;
            yield return Until(() =>
            {
                Topping best = null;
                foreach (Topping candidate in FindObjectsByType<Topping>(FindObjectsSortMode.None))
                {
                    if (candidate.toppingId != "mushroom") continue;
                    var item = candidate.GetComponent<GrabbableItem>();
                    if (item.State != ItemState.Free) continue;
                    var networkObject = candidate.GetComponent<NetworkObject>();
                    if (best == null || networkObject.NetworkObjectId < best.GetComponent<NetworkObject>().NetworkObjectId) best = candidate;
                }
                prize = best;
                return prize != null;
            }, 20f, "a mushroom to fight over");

            if (prize != null)
            {
                input.rightGrip = 1f;
                yield return null;
                yield return null;
                rightGrabber.TryGrabItem(prize.GetComponent<GrabbableItem>());
                yield return new WaitForSeconds(3f);
                report.wonConflict = prize != null && rightGrabber.Held == prize.GetComponent<GrabbableItem>();
                Note(report.wonConflict ? "won the grab" : "lost the grab");
            }

            if (index == 1)
            {
                // Leave abruptly while carrying something: the mushroom if we won it, otherwise a pepperoni.
                if (rightGrabber.Held == null)
                {
                    Topping spare = null;
                    foreach (Topping candidate in FindObjectsByType<Topping>(FindObjectsSortMode.None))
                    {
                        if (candidate.toppingId == "pepperoni" && candidate.GetComponent<GrabbableItem>().State == ItemState.Free) spare = candidate;
                    }
                    if (spare != null) yield return Grab(spare.GetComponent<GrabbableItem>(), "something to be holding when the connection drops");
                }
                yield return new WaitForSeconds(1f);
                report.finished = true;
                WriteReport();
                Application.Quit();
                yield break;
            }

            // Everyone else stays until the host goes, and should land back at the menu with an explanation.
            yield return Until(() => SceneManager.GetActiveScene().name == NetLauncher.MenuScene, 200f, "returned to the menu when the host left");
            report.returnedToMenu = SceneManager.GetActiveScene().name == NetLauncher.MenuScene;
            yield return new WaitForSeconds(0.5f);
            var menu = FindAnyObjectByType<SmallTown.UI.MenuController>();
            report.menuMessage = menu != null ? menu.Status : "";
            yield return Finish();
        }

        void RecordHostView(string when)
        {
            foreach (PlayerState player in session.Players)
            {
                report.hostView.Add(when + "|" + player.ClientId + "|cash=" + player.Wallet.Cash + "|wanted=" + player.Wanted.Level);
            }
        }
    }
}
