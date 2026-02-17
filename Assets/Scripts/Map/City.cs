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

    public void SetCityId(string id, bool force = false)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!force && !string.IsNullOrEmpty(cityId)) return;

        cityId = id;

        var rt = transform as RectTransform;
        if (rt != null && DispatchAnimationSystem.I != null)
            DispatchAnimationSystem.I.RegisterNode(cityId, rt);
    }

    private void Awake()
    {

    }

    private void OnEnable()
    {

        var rt = transform as RectTransform;
        if (rt == null || string.IsNullOrEmpty(cityId)) return;


        if (DispatchAnimationSystem.I != null)
            DispatchAnimationSystem.I.RegisterNode(cityId, rt);

        // register with map entity registry for quick lookup
        MapEntityRegistry.I?.RegisterCity(this);
    }

    private void OnDisable()
    {
        // unregister from registry
        MapEntityRegistry.I?.UnregisterCity(this);
    }

}