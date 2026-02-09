// Map system manager - controls visibility of old and new map systems
// Author: Canvas
// Version: 1.0

using UnityEngine;

namespace UI.Map
{
    public class MapSystemManager : MonoBehaviour
    {
        public static MapSystemManager Instance { get; private set; }

        [Header("Map Systems")]
        [SerializeField] private GameObject oldMapSystem;
        [SerializeField] private GameObject simpleWorldMapPanel;

        [Header("Settings")]
        [SerializeField] private bool useSimpleMap = true;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            InitializeMapSystems();
        }

        private void InitializeMapSystems()
        {
            // Disable old map system if using simple map
            if (useSimpleMap)
            {
                if (oldMapSystem != null)
                {
                    oldMapSystem.SetActive(false);
                    Debug.Log("[MapUI] Old map system disabled");
                }

                if (simpleWorldMapPanel != null)
                {
                    simpleWorldMapPanel.SetActive(true);
                    Debug.Log("[MapUI] Simple world map enabled");
                }
            }
            else
            {
                if (oldMapSystem != null)
                {
                    oldMapSystem.SetActive(true);
                    Debug.Log("[MapUI] Old map system enabled");
                }

                if (simpleWorldMapPanel != null)
                {
                    simpleWorldMapPanel.SetActive(false);
                    Debug.Log("[MapUI] Simple world map disabled");
                }
            }
        }

        public void SwitchToSimpleMap()
        {
            useSimpleMap = true;
            InitializeMapSystems();
        }

        public void SwitchToOldMap()
        {
            useSimpleMap = false;
            InitializeMapSystems();
        }

        public void ToggleMap()
        {
            useSimpleMap = !useSimpleMap;
            InitializeMapSystems();
        }
    }
}
