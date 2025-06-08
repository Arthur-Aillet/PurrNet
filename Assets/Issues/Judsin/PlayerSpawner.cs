using System;
using System.Collections;
using PurrNet;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellBound.Core
{
    public class PlayerSpawner : NetworkIdentity
    {
        [SerializeField] private GameObject prefab;
        [Header("Default Spawn Location")]
        [SerializeField] private Transform defaultSpawnLocation;
        // Maybe bed, maybe a saved transform location

        public event Action OnPlayerSpawned;

        // Should be flipped from the world load finishing.
        private bool _spawnReady = false;
        private Scene _sceneToSpawnIn;

        private void Awake() {
            Debug.Log("PlayerSpawner Awake.");
        }

        protected override void OnSpawned() {
            base.OnSpawned();

            Debug.Log("Player Spawner OnSpawned.");
            StartCoroutine(WaitForSpawnReady());
        }

        private IEnumerator WaitForSpawnReady() {
            while (!_spawnReady) {
                Debug.Log("Inside While Loop.");
                yield return new WaitForSeconds(10f);
            }
            Debug.Log("Exited while loop.");
            SpawnPlayer(_sceneToSpawnIn);
        }

        private void SpawnPlayer(Scene sceneName, Transform spawnLocation = null) {
            // Temporary
            defaultSpawnLocation = new GameObject("DefaultSpawnLocation").transform;
            defaultSpawnLocation.position = new Vector3(0,1,0);
            defaultSpawnLocation.rotation = Quaternion.identity;

            if (spawnLocation == null) spawnLocation = defaultSpawnLocation;

            // Find the prefab we want to instantiate.

            // Instantiation and configurations
            var go = UnityProxy.Instantiate(prefab, spawnLocation.position, spawnLocation.rotation, sceneName);

            // Ownership handoff.
            if (go.TryGetComponent(out NetworkIdentity identity)) {
                identity.GiveOwnership(NetworkManager.main.localPlayer);
            }

            OnPlayerSpawned?.Invoke();
        }

        public void SetSpawnReady(bool spawnReady) {
            _spawnReady = spawnReady;
            Debug.Log("SpawnReady is true.");
        }

        public void SetSpawnDetails( Scene sceneName) {
            _sceneToSpawnIn = sceneName;
        }
    }
}
