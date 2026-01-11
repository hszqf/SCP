using TMPro;
using UnityEngine;

public class HUD : MonoBehaviour
{
    [SerializeField] private TMP_Text dayText;
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private TMP_Text panicText;

    private void Start()
    {
        GameController.I.OnStateChanged += Refresh;
        Refresh();
    }

    public void OnEndDayClicked()
    {
        GameController.I.EndDay();
    }

    private void Refresh()
    {
        var s = GameController.I.State;
        dayText.text = $"Day {s.Day}";
        moneyText.text = $"$ {s.Money}";
        panicText.text = $"Panic {s.Panic}%";

        if (GameController.I.State.PendingEvents.Count > 0)
        {
            UIPanelRoot.I.OpenEventIfAny();
        }

        
    }
}
