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

    [Header("Optional Debug Text")]
    [SerializeField] private TMP_Text debugText;

    [Header("Buttons (bind in code, clear Inspector OnClick)")]
    [SerializeField] private Button endDayButton;
    [SerializeField] private Button recruitButton;

    private void Awake()
    {
        I = this;
        AutoWireBindingsIfMissing();
        BindButtonsInCode();
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
        if (endDayButton) endDayButton.interactable = enabled;
        if (recruitButton) recruitButton.interactable = enabled;
    }

    private void OnDisable()
    {
        if (GameController.I != null)
            GameController.I.OnStateChanged -= Refresh;
    }

    void AutoWireBindingsIfMissing()
    {
        // HUD 结构：HUD/Panel/End Day, HUD/Panel/NewsBT, 以及 3 个 TMP 文本（day/money/world panic）
        // 如果你已经手动拖好了，这里不会覆盖。
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

        if (!recruitButton && panel)
        {
            recruitButton = CreateRecruitButton(panel);
        }

        // 文本：如果你不想手动拖，按顺序自动找 HUD/Panel 下的 3 个 TMP_Text
        if ((!dayText || !moneyText || !panicText) && panel)
        {
            var tmps = panel.GetComponentsInChildren<TextMeshProUGUI>(true);
            // 层级里显示为 Text (TMP), Text (TMP) (1), Text (TMP) (2) :contentReference[oaicite:1]{index=1}
            // 我们按出现顺序填充：Day/Money/WorldPanic
            if (!dayText && tmps.Length > 0) dayText = tmps[0];
            if (!moneyText && tmps.Length > 1) moneyText = tmps[1];
            if (!panicText && tmps.Length > 2) panicText = tmps[2];
        }
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

    void OnEndDayClicked()
    {
        if (GameController.I == null) return;
        GameController.I.EndDay();
        StartCoroutine(PlayDispatchNextFrame());
    }

    private IEnumerator PlayDispatchNextFrame()
    {
        yield return null;
        DispatchAnimationSystem.I?.PlayPending();
    }

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

        if (debugText)
        {
            debugText.text = string.Empty;
        }

        // 里程碑0：HUD 只负责展示，不负责“弹窗逻辑”
        // 弹事件交给 UIPanelRoot 监听 OnStateChanged 的统一入口，避免重复弹/抢焦点。
    }

    private Button CreateRecruitButton(Transform panel)
    {
        var go = new GameObject("RecruitBT", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(panel, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(160f, 50f);
        rt.anchoredPosition = new Vector2(20f, 20f);

        var img = go.GetComponent<Image>();
        img.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "Recruit";
        label.fontSize = 24;
        label.alignment = TextAlignmentOptions.Center;

        return go.GetComponent<Button>();
    }
}
