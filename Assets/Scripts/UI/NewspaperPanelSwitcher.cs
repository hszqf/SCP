using System.Collections.Generic;
using UnityEngine;

public class NewspaperPanelSwitcher : MonoBehaviour
{
    public List<GameObject> Pages = new List<GameObject>();
    public int DefaultIndex = 0;
    
    // Reference to the NewspaperPanelView to call Render
    private UI.NewspaperPanelView _panelView;
    
    // IMPORTANT: Tab order must match Core.NewsConstants.AllMediaProfiles array
    // Tab 0 (Paper1) = FORMAL, Tab 1 (Paper2) = SENSATIONAL, Tab 2 (Paper3) = INVESTIGATIVE

    private void Awake()
    {
        EnsurePanelViewReference();
        ShowPaper(DefaultIndex);
    }

    private void OnEnable()
    {
        EnsurePanelViewReference();
        ShowPaper(DefaultIndex);
    }
    
    /// <summary>
    /// Ensure we have a reference to the NewspaperPanelView component.
    /// Try GetComponentInParent first, then GetComponent as fallback.
    /// </summary>
    private void EnsurePanelViewReference()
    {
        if (_panelView == null)
        {
            _panelView = GetComponentInParent<UI.NewspaperPanelView>();
            if (_panelView == null)
            {
                _panelView = GetComponent<UI.NewspaperPanelView>();
            }
        }
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
        if (_panelView != null)
        {
            if (index >= 0 && index < Core.NewsConstants.AllMediaProfiles.Length)
            {
                string mediaProfileId = Core.NewsConstants.AllMediaProfiles[index];
                Debug.Log($"[NewsUI] SwitchTab index={index} media={mediaProfileId}");
                _panelView.Render(mediaProfileId);
            }
            else
            {
                Debug.LogWarning($"[NewsUI] Invalid tab index {index}, expected 0-{Core.NewsConstants.AllMediaProfiles.Length - 1}");
            }
        }
        else
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
