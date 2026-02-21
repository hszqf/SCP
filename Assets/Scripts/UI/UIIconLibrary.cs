using UnityEngine;

/// <summary>
/// M6: unified icon library for HUD fly icons.
/// Create as an asset and bind in HUDResourceAnimator.
/// </summary>
[CreateAssetMenu(menuName = "UI/Icon Library", fileName = "UIIconLibrary")]
public sealed class UIIconLibrary : ScriptableObject
{
    public Sprite coinSprite;
    public Sprite negEntropySprite;
}
