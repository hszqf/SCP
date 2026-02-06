using System.Collections.Generic;
using UnityEngine;

public class NewspaperPanelSwitcher : MonoBehaviour
{
    public List<GameObject> Pages = new List<GameObject>();
    public int DefaultIndex = 0;
    
    // Reference to the NewspaperPanelView to call Render
    private UI.NewspaperPanelView _panelView;
    
    // Mapping of page index to media profile
    private static readonly string[] MediaProfileIds = { "FORMAL", "SENSATIONAL", "INVESTIGATIVE" };

    private void Awake()
    {
        _panelView = GetComponentInParent<UI.NewspaperPanelView>();
        if (_panelView == null)
        {
            // Try GetComponent for same object, or find in scene
            _panelView = GetComponent<UI.NewspaperPanelView>();
        }
        ShowPaper(DefaultIndex);
    }

    private void OnEnable()
    {
        if (_panelView == null)
        {
            _panelView = GetComponentInParent<UI.NewspaperPanelView>();
            if (_panelView == null)
            {
                _panelView = GetComponent<UI.NewspaperPanelView>();
            }
        }
        ShowPaper(DefaultIndex);
    }

    public void ShowPaper(int index)
    {
        if (Pages == null || Pages.Count == 0)
        {
            return;
        }

        if (index < 0 || index >= Pages.Count)
        {
            return;
        }

        for (int i = 0; i < Pages.Count; i++)
        {
            if (Pages[i] != null)
            {
                Pages[i].SetActive(i == index);
            }
        }
        
        // Refresh content with the appropriate media profile
        if (_panelView != null && index < MediaProfileIds.Length)
        {
            string mediaProfileId = MediaProfileIds[index];
            Debug.Log($"[NewsUI] SwitchTab index={index} media={mediaProfileId}");
            _panelView.Render(mediaProfileId);
        }
        else if (_panelView == null)
        {
            Debug.LogWarning("[NewsUI] NewspaperPanelView not found - tabs will show pages but not filter content");
        }
    }

    public void ShowPaper0()
    {
        ShowPaper(0);
    }

    public void ShowPaper1()
    {
        ShowPaper(1);
    }

    public void ShowPaper2()
    {
        ShowPaper(2);
    }
}
