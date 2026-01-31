using System.Collections.Generic;
using UnityEngine;

public class NewspaperPanelSwitcher : MonoBehaviour
{
    public List<GameObject> Pages = new List<GameObject>();
    public int DefaultIndex = 0;

    private void Awake()
    {
        ShowPaper(DefaultIndex);
    }

    private void OnEnable()
    {
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
