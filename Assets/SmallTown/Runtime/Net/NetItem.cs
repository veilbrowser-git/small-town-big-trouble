using SmallTown.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace SmallTown.Networking
{
    /// <summary>
    /// Makes a grabbable item a shared object with exactly one holder at a time.
    ///
    /// Rules:
    ///  - The host decides who holds an item. A grab is a request; whichever
    ///    request reaches the host first wins and the other is refused.
    ///  - Whoever holds (or last held) an item simulates it. Ownership moves to
    ///    the grabbing player, so carrying and throwing feel immediate for them
    ///    and everyone else sees the result.
    ///  - Nobody can take an item out of another player's hand or satchel.
    ///  - If the holder disconnects, the item is released where it was and the
    ///    host takes it over. Nothing stays grabbed forever.
    /// </summary>
    [RequireComponent(typeof(GrabbableItem))]
    public sealed class NetItem : NetworkBehaviour, IGrabAuthority
    {
        readonly NetworkVariable<ulong> holder = new NetworkVariable<ulong>(GrabbableItem.NoHolder);
        readonly NetworkVariable<byte> state = new NetworkVariable<byte>((byte)ItemState.Free);
        GrabbableItem item;
        HandGrabber waiting;

        public ulong HolderClientId => holder.Value;
        public ItemState SharedState => (ItemState)state.Value;

        void Awake() => item = GetComponent<GrabbableItem>();

        public override void OnNetworkSpawn()
        {
            item.Authority = this;
            holder.OnValueChanged += OnSharedChanged;
            state.OnValueChanged += OnSharedByteChanged;
            if (IsServer) NetworkManager.OnClientDisconnectCallback += OnClientLeft;
            ApplyShared();
        }

        public override void OnNetworkDespawn()
        {
            holder.OnValueChanged -= OnSharedChanged;
            state.OnValueChanged -= OnSharedByteChanged;
            if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientLeft;
            if (item != null && ReferenceEquals(item.Authority, this)) item.Authority = null;
        }

        void OnSharedChanged(ulong previous, ulong current) => ApplyShared();
        void OnSharedByteChanged(byte previous, byte current) => ApplyShared();

        /// <summary>On machines whose player does not have this item, show what the host says.</summary>
        void ApplyShared()
        {
            bool mine = holder.Value == NetworkManager.LocalClientId;
            if (mine || item.IsHeld) return;
            item.SetRemoteState((ItemState)state.Value, holder.Value);
        }

        bool MayTake(ulong clientId) => holder.Value == GrabbableItem.NoHolder || holder.Value == clientId;

        void Assign(ulong clientId, ItemState newState)
        {
            holder.Value = clientId;
            state.Value = (byte)newState;
            if (clientId != GrabbableItem.NoHolder && OwnerClientId != clientId) NetworkObject.ChangeOwnership(clientId);
        }

        // ------------------------------------------------------------------ IGrabAuthority

        public bool CanGrab(HandGrabber grabber) => !IsSpawned || MayTake(NetworkManager.LocalClientId);

        public GrabDecision RequestGrab(HandGrabber grabber)
        {
            if (!IsSpawned) return GrabDecision.Grant;
            ulong me = NetworkManager.LocalClientId;
            if (IsServer)
            {
                if (!MayTake(me)) return GrabDecision.Deny;
                Assign(me, ItemState.Held);
                return GrabDecision.Grant;
            }
            if (IsOwner && holder.Value == me)
            {
                // Already ours: swapping hands, or taking it back out of our own satchel.
                TakeRpc();
                return GrabDecision.Grant;
            }
            waiting = grabber;
            RequestGrabRpc();
            return GrabDecision.Pending;
        }

        public void OnGrabbed(HandGrabber grabber)
        {
        }

        public void OnReleased()
        {
            if (!IsSpawned) return;
            if (IsServer) Assign(GrabbableItem.NoHolder, ItemState.Free);
            else ReleaseRpc();
        }

        public void OnStored()
        {
            if (!IsSpawned) return;
            if (IsServer) Assign(NetworkManager.LocalClientId, ItemState.Stored);
            else StoreRpc();
        }

        public void OnTakenFromStorage() => OnReleased();

        // ------------------------------------------------------------------ Host side

        [Rpc(SendTo.Server)]
        void RequestGrabRpc(RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (MayTake(sender))
            {
                Assign(sender, ItemState.Held);
                GrantRpc(RpcTarget.Single(sender, RpcTargetUse.Temp));
            }
            else
            {
                DenyRpc(RpcTarget.Single(sender, RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.Server)]
        void ReleaseRpc(RpcParams rpc = default)
        {
            if (holder.Value == rpc.Receive.SenderClientId) Assign(GrabbableItem.NoHolder, ItemState.Free);
        }

        [Rpc(SendTo.Server)]
        void StoreRpc(RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (MayTake(sender)) Assign(sender, ItemState.Stored);
        }

        [Rpc(SendTo.Server)]
        void TakeRpc(RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (MayTake(sender)) Assign(sender, ItemState.Held);
        }

        void OnClientLeft(ulong clientId)
        {
            // Ownership has already come back to the host (the object outlives its owner).
            if (holder.Value == clientId) Assign(GrabbableItem.NoHolder, ItemState.Free);
        }

        // ------------------------------------------------------------------ Requester side

        [Rpc(SendTo.SpecifiedInParams)]
        void GrantRpc(RpcParams rpc)
        {
            HandGrabber grabber = waiting;
            waiting = null;
            // A repeated answer for something already in hand changes nothing.
            if (item.IsHeld) return;
            // If the player let go or grabbed something else while waiting, hand it straight back.
            if (grabber == null || !grabber.IsPending(item) || !grabber.CompleteGrab(item)) ReleaseRpc();
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void DenyRpc(RpcParams rpc)
        {
            waiting = null;
            GameFeedback.Play(FeedbackSound.Reject, transform.position, 0.4f);
        }
    }
}
