using System;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class LightsOffInteractable : MonoBehaviour, IInteractable
{
    private static readonly Color DefaultHoverMessageColor = new Color(1f, 1f, 1f, 0.95f);
    private const int DefaultHoverMessageFontSize = 18;
    private const float DefaultPostInteractMessageDuration = 4.5f;
    private const string DefaultScaryChimesPrefabPath = "SFXObjects/ScaryChimesSFX";
    private const string DefaultFrontDoorName = "Front Door";
    private const string DefaultFrontDoorWallName = "Front Door Wall";

    [SerializeField] private HouseManager houseManager;
    [SerializeField] private InteractableOutline outline;
    [SerializeField] private GameObject scaryChimesPrefab;
    [SerializeField] private GameObject frontDoorObject;
    [SerializeField] private GameObject frontDoorWallObject;
    [Header("Darkness")]
    [SerializeField] private bool darkenEnvironmentOnInteract = true;
    [SerializeField] private Color targetAmbientSkyColor = new Color(0.032f, 0.035f, 0.044f, 1f);
    [SerializeField] private Color targetAmbientEquatorColor = new Color(0.024f, 0.027f, 0.034f, 1f);
    [SerializeField] private Color targetAmbientGroundColor = new Color(0.007f, 0.006f, 0.008f, 1f);
    [SerializeField, Range(0f, 1f)] private float targetAmbientIntensity = 0.28f;
    [SerializeField, Range(0f, 1f)] private float targetReflectionIntensity = 0.1f;
    [SerializeField] private bool limitPlayerVisibilityOnInteract = true;
    [SerializeField, Min(0.5f)] private float playerVisibilityDistance = 4f;
    [SerializeField, Min(0f)] private float playerVisibilityFogStartDistance = 1.5f;
    [SerializeField] private Color playerVisibilityFogColor = new Color(0.018f, 0.02f, 0.027f, 1f);
    [SerializeField, Range(0f, 1f)] private float playerVisibilityVignetteIntensity = 0.24f;
    [SerializeField, Range(0.01f, 1f)] private float playerVisibilityVignetteSmoothness = 0.82f;
    [SerializeField, TextArea] private string hoverMessage = "Sleep";
    [SerializeField, TextArea] private string postInteractMessage = "My lamp is in the attic";
    [SerializeField] private float postInteractMessageDuration = 4.5f;
    [SerializeField] private Font hoverMessageFont;
    [SerializeField] private int hoverMessageFontSize = 18;
    [SerializeField] private Color hoverMessageColor = new Color(1f, 1f, 1f, 0.95f);

    public static bool HasTriggeredAnyBedInteraction { get; private set; }

    private bool hasTriggered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSessionState()
    {
        HasTriggeredAnyBedInteraction = false;
    }

    private void Awake()
    {
        if (outline == null)
            outline = GetComponent<InteractableOutline>();

        if (hoverMessageFontSize <= 0)
            hoverMessageFontSize = DefaultHoverMessageFontSize;

        if (postInteractMessageDuration <= 0f)
            postInteractMessageDuration = DefaultPostInteractMessageDuration;
    }

    private void OnValidate()
    {
        if (outline == null)
            outline = GetComponent<InteractableOutline>();

        if (hoverMessageFontSize <= 0)
            hoverMessageFontSize = DefaultHoverMessageFontSize;

        if (postInteractMessageDuration <= 0f)
            postInteractMessageDuration = DefaultPostInteractMessageDuration;
    }

    public Font GetHoverMessageFont()
    {
        return hoverMessageFont;
    }

    public int GetHoverMessageFontSize()
    {
        return hoverMessageFontSize > 0 ? hoverMessageFontSize : DefaultHoverMessageFontSize;
    }

    public Color GetHoverMessageColor()
    {
        return IsUnsetColor(hoverMessageColor) ? DefaultHoverMessageColor : hoverMessageColor;
    }

    public string GetHoverMessage()
    {
        return hasTriggered ? string.Empty : hoverMessage;
    }

    public void SetFocused(bool focused)
    {
        outline?.SetOutlined(focused && !hasTriggered);
    }

    public void Interact(Transform interactor)
    {
        if (hasTriggered)
            return;

        HouseManager targetHouseManager = ResolveHouseManager();
        if (targetHouseManager == null)
            return;

        targetHouseManager.TurnOffAllLights();
        DarkenEnvironment();
        LimitPlayerVisibility(interactor);
        PlayScaryChimes();
        ReplaceFrontDoorWithWall();
        ShowPostInteractMessage(interactor);
        HasTriggeredAnyBedInteraction = true;
        hasTriggered = true;
        outline?.SetOutlined(false);
    }

    private HouseManager ResolveHouseManager()
    {
        if (houseManager != null)
            return houseManager;

        if (HouseManager.HouseManagerInstance != null)
        {
            houseManager = HouseManager.HouseManagerInstance;
            return houseManager;
        }

        houseManager = FindObjectOfType<HouseManager>();
        return houseManager;
    }

    private void PlayScaryChimes()
    {
        GameObject prefabToSpawn = ResolveScaryChimesPrefab();
        if (prefabToSpawn == null)
            return;

        Instantiate(prefabToSpawn, transform.position, Quaternion.identity);
    }

    private GameObject ResolveScaryChimesPrefab()
    {
        if (scaryChimesPrefab != null)
            return scaryChimesPrefab;

        scaryChimesPrefab = Resources.Load<GameObject>(DefaultScaryChimesPrefabPath);
        return scaryChimesPrefab;
    }

    private void DarkenEnvironment()
    {
        if (!darkenEnvironmentOnInteract)
            return;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = targetAmbientSkyColor;
        RenderSettings.ambientEquatorColor = targetAmbientEquatorColor;
        RenderSettings.ambientGroundColor = targetAmbientGroundColor;
        RenderSettings.ambientIntensity = Mathf.Clamp01(targetAmbientIntensity);
        RenderSettings.reflectionIntensity = Mathf.Clamp01(targetReflectionIntensity);
        DynamicGI.UpdateEnvironment();
    }

    private void ShowPostInteractMessage(Transform interactor)
    {
        if (string.IsNullOrWhiteSpace(postInteractMessage))
            return;

        SimpleFirstPersonController controller = ResolvePlayerController(interactor);

        if (controller == null)
            return;

        controller.ShowPlayerMessage(postInteractMessage, postInteractMessageDuration);
    }

    private void LimitPlayerVisibility(Transform interactor)
    {
        if (!limitPlayerVisibilityOnInteract)
            return;

        SimpleFirstPersonController controller = ResolvePlayerController(interactor);
        if (controller == null)
            return;

        controller.ApplyVisibilityLimit(
            playerVisibilityDistance,
            playerVisibilityFogStartDistance,
            playerVisibilityFogColor,
            playerVisibilityVignetteIntensity,
            playerVisibilityVignetteSmoothness);
    }

    private void ReplaceFrontDoorWithWall()
    {
        GameObject targetFrontDoor = ResolveFrontDoorObject();
        GameObject targetFrontDoorWall = ResolveFrontDoorWallObject();
        if (targetFrontDoor == null || targetFrontDoorWall == null)
            return;

        DetachWallFromFrontDoor(targetFrontDoor, targetFrontDoorWall);
        targetFrontDoorWall.SetActive(true);
        targetFrontDoor.SetActive(false);
    }

    private static void DetachWallFromFrontDoor(GameObject targetFrontDoor, GameObject targetFrontDoorWall)
    {
        Transform wallTransform = targetFrontDoorWall.transform;
        Transform frontDoorTransform = targetFrontDoor.transform;
        if (!wallTransform.IsChildOf(frontDoorTransform))
            return;

        wallTransform.SetParent(frontDoorTransform.parent, true);
    }

    private GameObject ResolveFrontDoorObject()
    {
        if (frontDoorObject != null)
            return frontDoorObject;

        Transform[] sceneTransforms = FindObjectsOfType<Transform>();
        for (int i = 0; i < sceneTransforms.Length; i++)
        {
            Transform candidate = sceneTransforms[i];
            if (candidate == null)
                continue;

            if (string.Equals(candidate.name, DefaultFrontDoorName, StringComparison.OrdinalIgnoreCase))
            {
                frontDoorObject = candidate.gameObject;
                return frontDoorObject;
            }
        }

        return null;
    }

    private GameObject ResolveFrontDoorWallObject()
    {
        if (frontDoorWallObject != null)
            return frontDoorWallObject;

        Transform[] sceneTransforms = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < sceneTransforms.Length; i++)
        {
            Transform candidate = sceneTransforms[i];
            if (candidate == null)
                continue;

            GameObject candidateObject = candidate.gameObject;
            if (!candidateObject.scene.IsValid())
                continue;

            if (string.Equals(candidate.name, DefaultFrontDoorWallName, StringComparison.OrdinalIgnoreCase))
            {
                frontDoorWallObject = candidateObject;
                return frontDoorWallObject;
            }
        }

        return null;
    }

    private static SimpleFirstPersonController ResolvePlayerController(Transform interactor)
    {
        return interactor != null
            ? interactor.GetComponentInParent<SimpleFirstPersonController>()
            : FindObjectOfType<SimpleFirstPersonController>();
    }

    private static bool IsUnsetColor(Color color)
    {
        return Mathf.Approximately(color.r, 0f) &&
               Mathf.Approximately(color.g, 0f) &&
               Mathf.Approximately(color.b, 0f) &&
               Mathf.Approximately(color.a, 0f);
    }
}
