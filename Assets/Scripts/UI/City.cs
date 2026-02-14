using UnityEngine;

[DisallowMultipleComponent]
public class City : MonoBehaviour
{
    [SerializeField] private string cityId;
    [SerializeField] private string cityName;
    [SerializeField] private int cityType = 1;
    [SerializeField] private int population = 10;
    [SerializeField] private bool unlocked = true;

    public string CityId => cityId;
    public string CityName => string.IsNullOrEmpty(cityName) ? cityId : cityName;
    public int CityType => cityType;
    public int Population => population;
    public bool Unlocked => unlocked;

    private void Awake()
    {
        LogPosition();
    }

    private void LogPosition()
    {
        var rt = transform as RectTransform;
        if (rt != null)
        {
            Debug.Log($"[CityPos] id={cityId} name={CityName} anchored=({rt.anchoredPosition.x:0.##},{rt.anchoredPosition.y:0.##}) local=({rt.localPosition.x:0.##},{rt.localPosition.y:0.##}) world=({rt.position.x:0.##},{rt.position.y:0.##})");
        }
        else
        {
            Debug.Log($"[CityPos] id={cityId} name={CityName} local=({transform.localPosition.x:0.##},{transform.localPosition.y:0.##}) world=({transform.position.x:0.##},{transform.position.y:0.##})");
        }
    }
}
