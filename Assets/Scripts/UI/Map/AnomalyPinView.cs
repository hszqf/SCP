// Anomaly pin view - displays anomaly state indicator on map
// Author: Canvas
// Version: 1.0

using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Map
{
    public enum AnomalyPinState
    {
        Unknown,      // "?" - not yet investigated
        Discovered,   // Generic anomaly icon - discovered but not contained
        Contained,    // Lock/cage icon - contained but not managed
        Managed       // Dropper/battery icon - actively managed
    }

    public class AnomalyPinView : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private TMP_Text iconText;
        [SerializeField] private Image iconImage;

        [Header("Icon Settings")]
        [SerializeField] private Color unknownColor = new Color(1f, 1f, 0f, 1f);      // Yellow
        [SerializeField] private Color discoveredColor = new Color(1f, 0.3f, 0.3f, 1f); // Red
        [SerializeField] private Color containedColor = new Color(0.3f, 1f, 0.3f, 1f);  // Green
        [SerializeField] private Color managedColor = new Color(0.3f, 0.7f, 1f, 1f);    // Blue

        private string _nodeId;
        private string _anomalyId;
        private AnomalyPinState _state;
        private Button _clickButton;

        private void Awake()
        {
            _clickButton = GetComponent<Button>();
            if (_clickButton == null)
                _clickButton = gameObject.AddComponent<Button>();

            _clickButton.onClick.AddListener(OnClick);
        }

        public void Initialize(string nodeId, string anomalyId, AnomalyPinState state)
        {
            _nodeId = nodeId;
            _anomalyId = anomalyId;
            _state = state;
            
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            string icon;
            Color color;

            switch (_state)
            {
                case AnomalyPinState.Unknown:
                    icon = "?";
                    color = unknownColor;
                    break;
                case AnomalyPinState.Discovered:
                    icon = "⚠";
                    color = discoveredColor;
                    break;
                case AnomalyPinState.Contained:
                    icon = "🔒";
                    color = containedColor;
                    break;
                case AnomalyPinState.Managed:
                    icon = "⚡";
                    color = managedColor;
                    break;
                default:
                    icon = "?";
                    color = unknownColor;
                    break;
            }

            if (iconText != null)
            {
                iconText.text = icon;
                iconText.color = color;
            }

            if (iconImage != null)
            {
                iconImage.color = color;
            }
        }

        private void OnClick()
        {
            if (string.IsNullOrEmpty(_nodeId))
                return;

            Debug.Log($"[MapUI] Anomaly pin clicked: node={_nodeId} anomaly={_anomalyId} state={_state}");

            if (UIPanelRoot.I == null)
                return;

            switch (_state)
            {
                case AnomalyPinState.Unknown:
                    // Open investigate panel and focus on unknown anomalies
                    UIPanelRoot.I.OpenNode(_nodeId);
                    break;

                case AnomalyPinState.Discovered:
                    // Open contain panel (using the existing node panel which has contain button)
                    UIPanelRoot.I.OpenNode(_nodeId);
                    break;

                case AnomalyPinState.Contained:
                case AnomalyPinState.Managed:
                    // Open manage panel
                    UIPanelRoot.I.OpenManage(_nodeId);
                    break;
            }
        }
    }
}
