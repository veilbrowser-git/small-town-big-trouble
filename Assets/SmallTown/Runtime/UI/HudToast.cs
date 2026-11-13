using System.Collections.Generic;
using SmallTown.Locomotion;
using TMPro;
using UnityEngine;

namespace SmallTown
{
    /// <summary>
    /// Short messages that float in front of the player ("+$60 Pizza delivered").
    /// World-space and gently following, never locked to the head.
    /// </summary>
    public sealed class HudToast : MonoBehaviour
    {
        static HudToast instance;

        /// <summary>Everything shown this session, newest last. Tests read this.</summary>
        public static readonly List<string> History = new List<string>();

        [SerializeField] PlayerRig rig;
        [SerializeField] TMP_Text label;
        public float distance = 1.1f;
        public float drop = 0.18f;
        public float secondsPerMessage = 3.2f;

        readonly Queue<string> queue = new Queue<string>();
        float hideAt;
        bool placed;

        public void Configure(PlayerRig owner, TMP_Text text)
        {
            rig = owner;
            label = text;
        }

        public static void Show(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            History.Add(message);
            if (History.Count > 64) History.RemoveAt(0);
            if (instance != null) instance.queue.Enqueue(message);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
            History.Clear();
        }

        void OnEnable()
        {
            instance = this;
            if (label != null) label.text = "";
        }

        void OnDisable()
        {
            if (instance == this) instance = null;
        }

        void LateUpdate()
        {
            if (rig == null || rig.Head == null || label == null) return;

            if (Time.time >= hideAt)
            {
                if (queue.Count > 0)
                {
                    label.text = queue.Dequeue();
                    hideAt = Time.time + secondsPerMessage;
                    placed = false;
                }
                else if (label.text.Length > 0)
                {
                    label.text = "";
                }
            }
            if (label.text.Length == 0) return;

            Vector3 target = rig.Head.position + rig.BodyYaw * new Vector3(0f, -drop, distance);
            transform.position = placed ? Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-6f * Time.deltaTime)) : target;
            placed = true;
            Vector3 away = transform.position - rig.Head.position;
            if (away.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
    }
}
