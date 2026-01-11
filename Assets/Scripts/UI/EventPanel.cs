using System;
using Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EventPanel : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Button optionAButton;
    [SerializeField] private Button optionBButton;
    [SerializeField] private TMP_Text optionAText;
    [SerializeField] private TMP_Text optionBText;
    [SerializeField] private Button backgroundButton; // 蒙版按钮

    private PendingEvent _evt;

    public void Show(PendingEvent evt)
    {
        _evt = evt;
        gameObject.SetActive(true);

        // 绑定蒙版关闭
        if (backgroundButton) 
        {
            backgroundButton.onClick.RemoveAllListeners();
            backgroundButton.onClick.AddListener(() => gameObject.SetActive(false));
        }

        if (titleText) titleText.text = evt.Title;
        if (bodyText) bodyText.text = evt.Desc;

        // Option A
        if (optionAButton)
        {
            optionAButton.onClick.RemoveAllListeners();
            optionAButton.onClick.AddListener(() => OnOptionSelected(0));
            if (optionAText) optionAText.text = evt.Options.Count > 0 ? evt.Options[0].Text : "Option A";
        }

        // Option B
        if (optionBButton)
        {
            optionBButton.onClick.RemoveAllListeners();
            if (evt.Options.Count > 1)
            {
                optionBButton.gameObject.SetActive(true);
                optionBButton.onClick.AddListener(() => OnOptionSelected(1));
                if (optionBText) optionBText.text = evt.Options[1].Text;
            }
            else
            {
                optionBButton.gameObject.SetActive(false);
            }
        }
    }

    void OnOptionSelected(int index)
    {
        if (_evt == null || index < 0 || index >= _evt.Options.Count) return;
        
        // 修正：使用 PendingEvent 里的 ID 调用 GameController
        var opt = _evt.Options[index];
        GameController.I.ResolveEvent(_evt.Id, opt.Id);
        
        gameObject.SetActive(false);
    }
}