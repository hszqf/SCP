using System;
using System.Collections.Generic;
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

    private EventInstance _evt;
    private readonly List<Button> _optionButtons = new();
    private readonly List<TMP_Text> _optionTexts = new();

    private void Awake()
    {
        _optionButtons.Clear();
        _optionTexts.Clear();

        if (optionAButton) _optionButtons.Add(optionAButton);
        if (optionBButton) _optionButtons.Add(optionBButton);
        if (optionAText) _optionTexts.Add(optionAText);
        if (optionBText) _optionTexts.Add(optionBText);
    }

    public void Show(EventInstance evt)
    {
        _evt = evt;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        // 绑定蒙版关闭
        if (backgroundButton)
        {
            backgroundButton.onClick.RemoveAllListeners();
            backgroundButton.onClick.AddListener(() => gameObject.SetActive(false));
        }

        if (titleText) titleText.text = evt.Title;
        if (bodyText) bodyText.text = evt.Desc;

        RefreshOptions(evt.Options);
    }

    private void RefreshOptions(List<EventOption> options)
    {
        if (options == null) options = new List<EventOption>();

        for (int i = 0; i < _optionButtons.Count; i++)
        {
            var btn = _optionButtons[i];
            if (!btn) continue;

            btn.onClick.RemoveAllListeners();

            bool hasOption = i < options.Count && options[i] != null;
            btn.gameObject.SetActive(hasOption);
            if (!hasOption) continue;

            int capturedIndex = i;
            btn.onClick.AddListener(() => OnOptionSelected(capturedIndex));

            if (i < _optionTexts.Count && _optionTexts[i])
            {
                _optionTexts[i].text = options[i].Text;
            }
        }
    }

    void OnOptionSelected(int index)
    {
        if (_evt == null || _evt.Options == null || index < 0 || index >= _evt.Options.Count) return;

        var opt = _evt.Options[index];
        if (opt == null) return;

        GameController.I.ResolveEvent(_evt.NodeId, _evt.EventId, opt.OptionId);
        gameObject.SetActive(false);
    }
}
