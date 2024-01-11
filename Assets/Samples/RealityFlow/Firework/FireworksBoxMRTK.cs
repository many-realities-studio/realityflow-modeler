using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ubiq.XR;
using UnityEngine;
using Ubiq.Spawning;
using UnityEngine.XR.Interaction.Toolkit;
using Microsoft.MixedReality.Toolkit;

namespace Ubiq.Samples
{
    public interface IFireworkNew
    {
        void Attach(IXRInteractor hand);
    }

    /// <summary>
    /// The Fireworks Box is a basic interactive object. This object uses the NetworkSpawner
    /// to create shared objects (fireworks).
    /// The Box can be grasped and moved around, but note that the Box itself is *not* network
    /// enabled, and each player has their own copy.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class FireworksBoxMRTK : MonoBehaviour
    {
        public GameObject FireworkPrefab;

        private Rigidbody body;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        public void Deactivate(Hand controller)
        {
        }

        public void Activate(IXRInteractor interactor)
        {
            var go = NetworkSpawnManager.Find(this).SpawnWithPeerScope(FireworkPrefab);
            var firework = go.GetComponent<FireworkNew>();
            firework.owner = true;
            if (firework != null)
            {
                firework.Attach(interactor);
            }
        }
    }
}