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
    [SerializeField] private TMP_Text descText;
    [SerializeField] private Transform optionsRoot;
    [SerializeField] private Button optionButtonTemplate;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private Button closeButton;

    private EventInstance _eventInstance;
    private Func<string, string> _onChoose;
    private Action _onClose;
    private readonly List<Button> _spawnedOptionButtons = new();

    private void OnEnable()
    {
        Debug.Log("[EventUI] EventPanel.OnEnable");
    }

    public void Show(EventInstance ev, Func<string, string> onChoose, Action onClose = null)
    {
        Debug.Log($"[EventUI] Show node={ev?.NodeId ?? "<null>"} eventId={ev?.EventId ?? "<null>"} titleLen={(ev?.Title?.Length ?? -1)} descLen={(ev?.Desc?.Length ?? -1)} options={(ev?.Options == null ? -1 : ev.Options.Count)}");
        if (ev == null)
        {
            Debug.LogError("[EventUI] Show called with null EventInstance");
            return;
        }

        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        if (!ValidateRefsOrThrow()) return;

        _eventInstance = ev;
        _onChoose = onChoose;
        _onClose = onClose;

        resultText.text = string.Empty;
        resultText.gameObject.SetActive(false);

        titleText.text = ev.Title;
        descText.text = ev.Desc;

        optionButtonTemplate.onClick.RemoveAllListeners();
        optionButtonTemplate.gameObject.SetActive(false);

        ClearSpawnedOptions();
        BuildOptions(ev.Options);
        BindCloseButton();
        LogShow(ev);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void ClearSpawnedOptions()
    {
        if (!optionsRoot) return;

        _spawnedOptionButtons.Clear();

        var toDestroy = new List<GameObject>();
        for (int i = 0; i < optionsRoot.childCount; i++)
        {
            var child = optionsRoot.GetChild(i);
            if (!child) continue;
            if (optionButtonTemplate && child == optionButtonTemplate.transform) continue;
            toDestroy.Add(child.gameObject);
        }

        foreach (var go in toDestroy)
        {
            Destroy(go);
        }
    }

    private void BuildOptions(List<EventOption> options)
    {
        if (!optionsRoot || !optionButtonTemplate) return;
        options ??= new List<EventOption>();

        foreach (var option in options)
        {
            if (option == null) continue;

            var button = Instantiate(optionButtonTemplate, optionsRoot);
            button.onClick.RemoveAllListeners();
            button.gameObject.SetActive(true);

            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label) label.text = option.Text;

            string optionId = option.OptionId;
            button.onClick.AddListener(() => OnOptionClicked(optionId));

            _spawnedOptionButtons.Add(button);
        }
    }

    private void OnOptionClicked(string optionId)
    {
        LogClick(optionId);
        var result = _onChoose?.Invoke(optionId);
        ShowResult(string.IsNullOrEmpty(result) ? "事件已处理" : result);
    }

    private void ShowResult(string result)
    {
        resultText.text = result;
        resultText.gameObject.SetActive(true);
    }

    private void BindCloseButton()
    {
        if (!closeButton) return;
        closeButton.onClick.RemoveAllListeners();
        closeButton.onClick.AddListener(() =>
        {
            Hide();
            _onClose?.Invoke();
        });
    }

    private bool ValidateRefsOrThrow()
    {
        var missing = new List<string>();
        if (!titleText) missing.Add(nameof(titleText));
        if (!descText) missing.Add(nameof(descText));
        if (!optionsRoot) missing.Add(nameof(optionsRoot));
        if (!optionButtonTemplate) missing.Add(nameof(optionButtonTemplate));
        if (!resultText) missing.Add(nameof(resultText));
        if (!closeButton) missing.Add(nameof(closeButton));

        if (missing.Count == 0) return true;

        Debug.LogError($"[EventUI] Missing refs: {string.Join(", ", missing)}");
        return false;
    }

    private void LogShow(EventInstance ev)
    {
        int pendingCount = 0;
        if (GameController.I != null && !string.IsNullOrEmpty(ev.NodeId))
        {
            var node = GameController.I.GetNode(ev.NodeId);
            pendingCount = node?.PendingEvents?.Count ?? 0;
        }

        int optionCount = ev.Options?.Count ?? 0;
        Debug.Log($"[EventUI] Show node={ev.NodeId} eventId={ev.EventId} options={optionCount} pendingCount={pendingCount}");
    }

    private void LogClick(string optionId)
    {
        if (_eventInstance == null) return;
        Debug.Log($"[EventUI] Click option={optionId} node={_eventInstance.NodeId} eventId={_eventInstance.EventId}");
    }
}
