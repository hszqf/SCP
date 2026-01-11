// <EXPORT_BLOCK>
using System;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EventPanel : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descText;
    [SerializeField] private Transform optionRoot;
    [SerializeField] private Button optionPrefab;
    [SerializeField] private TMP_Text resultText;

    private string _eventId;

    public void Show(PendingEvent ev)
    {
        gameObject.SetActive(true);
        _eventId = ev.Id;

        titleText.text = ev.Title;
        descText.text = ev.Desc;
        if (resultText) resultText.text = "";

        foreach (Transform c in optionRoot) Destroy(c.gameObject);

        foreach (var opt in ev.Options)
        {
            var b = Instantiate(optionPrefab, optionRoot);
            b.GetComponentInChildren<TMP_Text>().text = opt.Text;
            b.onClick.AddListener(() => OnPick(opt.Id));
        }
    }

    void OnPick(string optionId)
    {
        var (success, text) = GameController.I.ResolveEvent(_eventId, optionId);
        if (resultText) resultText.text = text;

        // 简单做法：选完立即关闭
        // 注意：这里不再直接调用 UIPanelRoot.CloseEvent，而是通过 callback 或者直接隐藏
        // 为了解耦，我们暂时直接隐藏自己，Manager 会感知到
        gameObject.SetActive(false);
        
        // 通知 Manager 刷新 (可选，如果 Manager 监听了 StateChange 则不需要这行)
        UIPanelRoot.I?.RefreshNodePanel();


    }
}

// </EXPORT_BLOCK>

