using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HUD : MonoBehaviour
{
    [Header("Texts")]
    [SerializeField] private TMP_Text dayText;
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private TMP_Text panicText;

    [Header("Optional Debug Text")]
    [SerializeField] private TMP_Text debugText;

    [Header("Buttons (bind in code, clear Inspector OnClick)")]
    [SerializeField] private Button endDayButton;
    [SerializeField] private Button newsButton;

    private void Awake()
    {
        AutoWireBindingsIfMissing();
        BindButtonsInCode();
    }

    private void OnEnable()
    {
        if (GameController.I != null)
            GameController.I.OnStateChanged += Refresh;

        Refresh();
    }

    private void OnDisable()
    {
        if (GameController.I != null)
            GameController.I.OnStateChanged -= Refresh;
    }

    void AutoWireBindingsIfMissing()
    {
        // HUD 结构：HUD/Panel/End Day, HUD/Panel/NewsBT, 以及 3 个 TMP 文本（day/money/panic）
        // 如果你已经手动拖好了，这里不会覆盖。
        var panel = transform.Find("Panel");

        if (!endDayButton && panel)
        {
            var t = panel.Find("End Day");
            if (t) endDayButton = t.GetComponent<Button>();
        }

        if (!newsButton && panel)
        {
            var t = panel.Find("NewsBT");
            if (t) newsButton = t.GetComponent<Button>();
        }

        // 文本：如果你不想手动拖，按顺序自动找 HUD/Panel 下的 3 个 TMP_Text
        if ((!dayText || !moneyText || !panicText) && panel)
        {
            var tmps = panel.GetComponentsInChildren<TextMeshProUGUI>(true);
            // 层级里显示为 Text (TMP), Text (TMP) (1), Text (TMP) (2) :contentReference[oaicite:1]{index=1}
            // 我们按出现顺序填充：Day/Money/Panic
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

        if (newsButton)
        {
            newsButton.onClick.RemoveAllListeners();
            newsButton.onClick.AddListener(OnNewsClicked);
        }
    }

    void OnEndDayClicked()
    {
        if (GameController.I == null) return;
        GameController.I.EndDay();
    }

    void OnNewsClicked()
    {
        if (UIPanelRoot.I == null) return;
        UIPanelRoot.I.OpenNews();
    }

    void Refresh()
    {
        if (GameController.I == null) return;

        var s = GameController.I.State;
        if (dayText) dayText.text = $"Day {s.Day}";
        if (moneyText) moneyText.text = $"$ {s.Money}";
        if (panicText) panicText.text = $"Panic {s.Panic}%";

        if (debugText)
        {
            int ev = 0;
            if (s.Nodes != null)
            {
                foreach (var node in s.Nodes)
                    ev += node?.PendingEvents?.Count ?? 0;
            }
            debugText.text = $"Events: {ev}";
        }

        // 里程碑0：HUD 只负责展示，不负责“弹窗逻辑”
        // 弹事件交给 UIPanelRoot 监听 OnStateChanged 的统一入口，避免重复弹/抢焦点。
    }
}
