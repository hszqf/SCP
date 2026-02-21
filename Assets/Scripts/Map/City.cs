using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;




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

    [Header("Playback FX (optional)")]
    [SerializeField] private Graphic flashGraphic;
    [SerializeField] private float flashSeconds = 0.18f;
    [SerializeField] private float shakePixels = 8f;

    [SerializeField] private TMP_Text popLossTextPrefab; // optional
    [SerializeField] private RectTransform popLossTextLayer; // optional

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
        if (!flashGraphic) flashGraphic = GetComponent<Graphic>();
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

    // -------- M6 playback: CityPopLoss FX --------

    public void PlayPopLossFX(int loss, int afterPop, float durationSeconds)
    {
        StartCoroutine(PopLossCoroutine(loss, durationSeconds));
    }

    private IEnumerator PopLossCoroutine(int loss, float durationSeconds)
    {
        var rt = transform as RectTransform;
        Vector2 basePos = rt != null ? rt.anchoredPosition : Vector2.zero;

        Color baseColor = flashGraphic != null ? flashGraphic.color : Color.white;

        float dur = Mathf.Max(0.05f, durationSeconds);
        float t = 0f;

        // spawn optional floating text
        if (popLossTextPrefab)
            SpawnPopLossText(loss);

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            float flashK = Mathf.Clamp01(t / Mathf.Max(0.01f, flashSeconds));

            if (flashGraphic)
            {
                // red flash then fade back
                var red = new Color(1f, 0.25f, 0.25f, baseColor.a);
                flashGraphic.color = Color.Lerp(red, baseColor, flashK);
            }

            if (rt)
            {
                float s = Mathf.Sin(k * Mathf.PI * 8f);
                rt.anchoredPosition = basePos + new Vector2(s * shakePixels, 0f);
            }

            yield return null;
        }

        if (flashGraphic) flashGraphic.color = baseColor;
        if (rt) rt.anchoredPosition = basePos;
    }

    private void SpawnPopLossText(int loss)
    {
        if (!popLossTextPrefab) return;

        var layer = popLossTextLayer != null ? popLossTextLayer : (transform.parent as RectTransform);
        if (layer == null) return;

        var txt = Instantiate(popLossTextPrefab, layer);
        var txtRt = txt.transform as RectTransform;
        if (txtRt != null)
        {
            // align to this city in screen space
            txtRt.position = transform.position;
        }

        txt.text = $"-{Mathf.Max(0, loss)}";
        StartCoroutine(FloatAndFade(txt));
    }

    private IEnumerator FloatAndFade(TMP_Text txt)
    {
        if (txt == null) yield break;
        var rt = txt.transform as RectTransform;

        float dur = 0.6f;
        float t = 0f;
        var basePos = rt != null ? rt.anchoredPosition : Vector2.zero;
        var baseColor = txt.color;

        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);

            if (rt) rt.anchoredPosition = basePos + Vector2.up * (30f * k);
            txt.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Lerp(baseColor.a, 0f, k));
            yield return null;
        }

        Destroy(txt.gameObject);
    }

}