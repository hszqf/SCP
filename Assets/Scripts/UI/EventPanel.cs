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
    [SerializeField] private Button closeButton;

    private EventInstance _eventInstance;
    private Action<string> _onChoose;
    private Action _onClose;
    private readonly List<Button> _spawnedOptionButtons = new();

    public void Show(EventInstance ev, Action<string> onChoose, Action onClose = null)
    {
        if (ev == null) return;

        Debug.Log($"[EventUI] Show node={ev.NodeId} eventId={ev.EventId} titleLen={ev.Title?.Length} descLen={ev.Desc?.Length} options={ev.Options?.Count}");

        _eventInstance = ev;
        _onChoose = onChoose;
        _onClose = onClose;

        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        if (titleText) titleText.text = ev.Title;
        if (descText) descText.text = ev.Desc;

        if (optionButtonTemplate)
        {
            optionButtonTemplate.onClick.RemoveAllListeners();
            optionButtonTemplate.gameObject.SetActive(false);
        }

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
            button.onClick.AddListener(() =>
            {
                LogClick(optionId);
                _onChoose?.Invoke(optionId);
                Hide();
            });

            _spawnedOptionButtons.Add(button);
        }
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
