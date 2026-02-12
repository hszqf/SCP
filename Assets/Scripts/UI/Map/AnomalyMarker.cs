using Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AnomalyMarker : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private Button button;
    [SerializeField] private Image progressBar;
    [SerializeField] private Image progressBackground;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private Transform nameRoot;
    [SerializeField] private TMP_Text nameText;
    private float _progressWidth;
    private bool _progressWidthCached;

    private string _nodeId;
    private string _anomalyId;
    private string _managedAnomalyId;
    private bool _isKnown;
    private bool _isContained;

    public void Bind(string nodeId, string anomalyId, string managedAnomalyId, Sprite sprite, bool isKnown, bool displayKnown, bool isContained, float progress01, string progressPrefix, string nameSuffix, bool hideNameWhileProgress)
    {
        _nodeId = nodeId;
        _anomalyId = anomalyId;
        _managedAnomalyId = managedAnomalyId;
        _isKnown = isKnown;
        _isContained = isContained;

        if (!icon)
            icon = GetComponentInChildren<Image>(true);
        if (icon)
        {
            icon.raycastTarget = true;
            icon.sprite = sprite;
        }

        if (!button)
            button = GetComponent<Button>() ?? GetComponentInChildren<Button>(true) ?? gameObject.AddComponent<Button>();
        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
        }

        if (progressBar)
        {
            CacheProgressWidth();

            var clamped = Mathf.Clamp01(progress01);
            if (progressBar.type == Image.Type.Filled)
            {
                progressBar.fillMethod = Image.FillMethod.Horizontal;
                progressBar.fillOrigin = (int)Image.OriginHorizontal.Left;
                progressBar.fillAmount = clamped;
            }
            else if (_progressWidthCached)
            {
                progressBar.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _progressWidth * clamped);
            }

            progressBar.gameObject.SetActive(clamped > 0f);
        }

        if (progressBackground)
            progressBackground.gameObject.SetActive(progress01 > 0f);

        if (progressText)
        {
            var percentText = $"{Mathf.RoundToInt(Mathf.Clamp01(progress01) * 100f)}%";
            var prefix = progressPrefix ?? string.Empty;
            progressText.text = string.IsNullOrEmpty(prefix) ? percentText : $"{prefix}{percentText}";
            progressText.gameObject.SetActive(progress01 > 0f);
        }

        if (nameText)
        {
            nameText.text = displayKnown ? ResolveAnomalyName(anomalyId) + (nameSuffix ?? string.Empty) : string.Empty;
        }

        if (nameRoot)
            nameRoot.gameObject.SetActive(displayKnown && !(hideNameWhileProgress && progress01 > 0f));
    }

    private void HandleClick()
    {
        if (DispatchAnimationSystem.I != null && DispatchAnimationSystem.I.IsInteractionLocked)
            return;
        var root = UIPanelRoot.I;
        if (root == null || string.IsNullOrEmpty(_nodeId)) return;

        if (_isContained)
        {
            root.OpenManage(_nodeId, _managedAnomalyId);
            return;
        }

        if (_isKnown)
        {
            root.OpenContainAssignPanelForNode(_nodeId);
            return;
        }

        root.OpenInvestigateAssignPanelForNode(_nodeId, _anomalyId);
    }

    private void CacheProgressWidth()
    {
        float width = progressBar ? progressBar.rectTransform.rect.width : 0f;
        if (progressBackground)
        {
            float bgWidth = progressBackground.rectTransform.rect.width;
            if (bgWidth > 0.01f) width = bgWidth;
        }

        if (width > 0.01f && (!_progressWidthCached || Mathf.Abs(_progressWidth - width) > 0.5f))
        {
            _progressWidth = width;
            _progressWidthCached = true;
        }
    }

    private static string ResolveAnomalyName(string anomalyId)
    {
        if (string.IsNullOrEmpty(anomalyId)) return string.Empty;
        var registry = DataRegistry.Instance;
        if (registry != null && registry.AnomaliesById.TryGetValue(anomalyId, out var def) && def != null && !string.IsNullOrEmpty(def.name))
            return def.name;
        return anomalyId;
    }
}
