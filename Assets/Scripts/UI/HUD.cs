using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HUD : MonoBehaviour
{
    public static HUD I { get; private set; }
    [Header("Texts")]
    [SerializeField] private TMP_Text dayText;
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private TMP_Text panicText;
    [SerializeField] private TMP_Text negEntropyText;

    [Header("Optional Debug Text")]
    [SerializeField] private TMP_Text debugText;

    [Header("Buttons (bind in code, clear Inspector OnClick)")]
    [SerializeField] private Button endDayButton;
    [SerializeField] private Button recruitButton;

    private void Awake()
    {
        I = this;
        AutoWireButtonsIfMissing();
        BindButtonsInCode();
        RequireBindings();
    }

    private void OnDestroy()
    {
        if (I == this) I = null;
    }

    private void OnEnable()
    {
        if (GameController.I != null)
            GameController.I.OnStateChanged += Refresh;

        Refresh();
    }

    public void SetControlsInteractable(bool enabled)
    {
        if (endDayButton)
        {
            endDayButton.interactable = enabled;
            endDayButton.gameObject.SetActive(enabled);
        }

        if (recruitButton)
        {
            recruitButton.interactable = enabled;
            recruitButton.gameObject.SetActive(enabled);
        }
    }

    private void OnDisable()
    {
        if (GameController.I != null)
            GameController.I.OnStateChanged -= Refresh;
    }

    void AutoWireButtonsIfMissing()
    {
        // Buttons only. Texts/anchors are strong bindings (no fallback).
        var panel = transform.Find("Panel");

        if (!endDayButton && panel)
        {
            var t = panel.Find("End Day");
            if (t) endDayButton = t.GetComponent<Button>();
        }

        if (!recruitButton && panel)
        {
            var t = panel.Find("RecruitBT");
            if (t) recruitButton = t.GetComponent<Button>();
        }

    }

    void RequireBindings()
    {
        if (!dayText) Debug.LogError("[HUD] Missing binding: dayText", this);
        if (!moneyText) Debug.LogError("[HUD] Missing binding: moneyText", this);
        if (!panicText) Debug.LogError("[HUD] Missing binding: panicText", this);
        if (!negEntropyText) Debug.LogError("[HUD] Missing binding: negEntropyText", this);
    }

    void BindButtonsInCode()
    {
        if (!endDayButton)
        {
            Debug.LogError("[HUD] endDayButton not bound (expected HUD/Panel/End Day)", this);
        }
        else
        {
            endDayButton.onClick.RemoveAllListeners();
            endDayButton.onClick.AddListener(OnEndDayClicked);
        }

        if (recruitButton)
        {
            recruitButton.onClick.RemoveAllListeners();
            recruitButton.onClick.AddListener(OnRecruitClicked);
        }
    }

    // ===== BEGIN M5: EndDay click => Play or Skip =====
    void OnEndDayClicked()
    {
        if (GameController.I == null) return;

        // 播放中：不允许 Skip，不允许任何点击触发逻辑
        if (DayPlaybackDirector.I != null && DayPlaybackDirector.I.IsPlaying)
        {
            Debug.Log("[HUD] EndDay click blocked: playback running");
            return;
        }

        if (!GameController.I.CanEndDay(out var reason))
        {
            Debug.LogWarning($"[Day] Blocked: {reason}");
            return;
        }

        GameController.I.EndDay();
    }
    // ===== END M5: EndDay click => Play or Skip =====


    void OnRecruitClicked()
    {
        if (UIPanelRoot.I == null) return;
        UIPanelRoot.I.OpenRosterPanel();
    }

    void Refresh()
    {
        if (GameController.I == null) return;

        var s = GameController.I.State;
        if (dayText) dayText.text = $"Day {s.Day}";
        if (moneyText) moneyText.text = $"$ {s.Money}";
        if (panicText) panicText.text = $"WorldPanic {s.WorldPanic:0.##}";
        if (negEntropyText) negEntropyText.text = $"NE {s.NegEntropy}";

        if (debugText)
        {
            debugText.text = string.Empty;
        }

        // 里程碑0：HUD 只负责展示，不负责“弹窗逻辑”
        // 弹事件交给 UIPanelRoot 监听 OnStateChanged 的统一入口，避免重复弹/抢焦点。
    }

}
