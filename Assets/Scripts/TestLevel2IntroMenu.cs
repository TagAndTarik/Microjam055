using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TestLevel2IntroMenu : MonoBehaviour
{
    private const string TargetSceneName = "TestLevel2";
    private const string LogoResourcePath = "NyctophobiaSplash";
    private static readonly Color OverlayColor = new Color(0.02f, 0.02f, 0.03f, 0.42f);
    private static readonly Color AccentColor = new Color(0.82f, 0.16f, 0.12f, 0.95f);
    private static readonly Color PanelColor = new Color(0.04f, 0.04f, 0.05f, 0.78f);
    private static readonly Color ButtonColor = new Color(0.08f, 0.08f, 0.1f, 0.88f);
    private static readonly Color HighlightColor = new Color(0.16f, 0.16f, 0.18f, 0.94f);
    private static readonly Color PressedColor = new Color(0.22f, 0.12f, 0.11f, 0.96f);
    private static readonly Color TitleColor = new Color(0.96f, 0.95f, 0.93f, 1f);
    private static readonly Color BodyColor = new Color(0.88f, 0.88f, 0.87f, 0.96f);

    [TextArea(6, 20)]
    [SerializeField]
    private string creditsText =
        "A Microjam055 project\n\n" +
        "Repository contributors\n" +
        "Tag Hunt\n" +
        "Tarik Campbell\n" +
        "Biostart - Free Wood Door Pack\n"+
        "nappin - House Interior - Free\n"+
        "Powered by Unity";

    private GameObject mainPage;
    private GameObject creditsPage;
    private Button playButton;
    private Button creditsPlayButton;
    private Button backButton;
    private Font uiFont;
    private SimpleFirstPersonController playerController;
    private bool hasStartedGame;
    private bool uiBuilt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (SceneManager.GetActiveScene().name != TargetSceneName)
            return;

        if (FindFirstObjectByType<TestLevel2IntroMenu>() != null)
            return;

        GameObject menuRoot = new GameObject(nameof(TestLevel2IntroMenu), typeof(RectTransform));
        menuRoot.AddComponent<TestLevel2IntroMenu>();
    }

    private void Awake()
    {
        EnsureUiBuilt();
        KeepCursorUnlocked();
    }

    private void Start()
    {
        TryBindPlayerController();
        RefreshPlayButtonState();
    }

    private void Update()
    {
        if (hasStartedGame)
            return;

        EnsureUiBuilt();
        KeepCursorUnlocked();

        if (playerController == null)
        {
            TryBindPlayerController();
            RefreshPlayButtonState();
        }

        if (mainPage == null || creditsPage == null)
            return;

        if (Keyboard.current == null)
            return;

        if (mainPage.activeSelf && Keyboard.current.enterKey.wasPressedThisFrame)
            StartGame();
        else if (creditsPage.activeSelf && Keyboard.current.escapeKey.wasPressedThisFrame)
            ShowMainPage();
    }

    private void BuildUi()
    {
        if (uiBuilt)
            return;

        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (uiFont == null)
            uiFont = Font.CreateDynamicFontFromOSFont("Arial", 16);

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        RectTransform rootRect = gameObject.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        GameObject overlay = CreateUiObject("Overlay", transform);
        Image overlayImage = overlay.AddComponent<Image>();
        overlayImage.color = OverlayColor;
        StretchFull((RectTransform)overlay.transform);

        CreateEdgeGlow(overlay.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -140f), new Vector2(0f, 0f), new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.12f));
        CreateEdgeGlow(overlay.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 160f), new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.06f));

        mainPage = BuildMainPage(overlay.transform);
        creditsPage = BuildCreditsPage(overlay.transform);
        uiBuilt = true;
    }

    private void EnsureUiBuilt()
    {
        if (uiBuilt)
            return;

        BuildUi();

        if (mainPage != null && creditsPage != null)
            ShowMainPage();
    }

    private GameObject BuildMainPage(Transform parent)
    {
        GameObject page = CreateUiObject("MainPage", parent);
        StretchFull((RectTransform)page.transform);

        RectTransform panel = CreatePanel(page.transform, "MenuPanel", new Vector2(0.5f, 0.5f), new Vector2(780f, 620f));
        CreatePanelChrome(panel);

        RectTransform logoArea = CreateChildRect(panel, "LogoArea");
        logoArea.anchorMin = new Vector2(0.5f, 1f);
        logoArea.anchorMax = new Vector2(0.5f, 1f);
        logoArea.pivot = new Vector2(0.5f, 1f);
        logoArea.anchoredPosition = new Vector2(0f, -58f);
        logoArea.sizeDelta = new Vector2(540f, 200f);
        BuildLogo(logoArea);

        Text subtitle = CreateText(panel, "Subtitle", "The Darkness is Coming", 24, BodyColor, TextAnchor.MiddleCenter);
        RectTransform subtitleRect = (RectTransform)subtitle.transform;
        subtitleRect.anchorMin = new Vector2(0.5f, 1f);
        subtitleRect.anchorMax = new Vector2(0.5f, 1f);
        subtitleRect.pivot = new Vector2(0.5f, 1f);
        subtitleRect.anchoredPosition = new Vector2(0f, -250f);
        subtitleRect.sizeDelta = new Vector2(560f, 34f);

        playButton = CreateButton(panel, "PlayButton", "PLAY", new Vector2(0f, -360f), StartGame);
        CreateButton(panel, "CreditsButton", "CREDITS", new Vector2(0f, -438f), ShowCreditsPage);
        return page;
    }

    private GameObject BuildCreditsPage(Transform parent)
    {
        GameObject page = CreateUiObject("CreditsPage", parent);
        StretchFull((RectTransform)page.transform);

        RectTransform panel = CreatePanel(page.transform, "CreditsPanel", new Vector2(0.5f, 0.5f), new Vector2(820f, 660f));
        CreatePanelChrome(panel);

        Text title = CreateText(panel, "CreditsTitle", "CREDITS", 40, TitleColor, TextAnchor.MiddleCenter);
        RectTransform titleRect = (RectTransform)title.transform;
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -48f);
        titleRect.sizeDelta = new Vector2(500f, 48f);

        Text body = CreateText(panel, "CreditsBody", creditsText, 26, BodyColor, TextAnchor.UpperCenter);
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform bodyRect = (RectTransform)body.transform;
        bodyRect.anchorMin = new Vector2(0.5f, 0.5f);
        bodyRect.anchorMax = new Vector2(0.5f, 0.5f);
        bodyRect.pivot = new Vector2(0.5f, 0.5f);
        bodyRect.anchoredPosition = new Vector2(0f, -8f);
        bodyRect.sizeDelta = new Vector2(620f, 360f);

        backButton = CreateButton(panel, "BackButton", "BACK", new Vector2(-170f, -546f), ShowMainPage);
        creditsPlayButton = CreateButton(panel, "CreditsPlayButton", "PLAY", new Vector2(170f, -546f), StartGame);
        return page;
    }

    private void BuildLogo(RectTransform logoArea)
    {
        Texture2D logoTexture = Resources.Load<Texture2D>(LogoResourcePath);
        if (logoTexture != null)
        {
            GameObject logoObject = CreateUiObject("Logo", logoArea);
            RectTransform logoRect = (RectTransform)logoObject.transform;
            StretchFull(logoRect);

            Image logoImage = logoObject.AddComponent<Image>();
            logoImage.sprite = Sprite.Create(
                logoTexture,
                new Rect(0f, 0f, logoTexture.width, logoTexture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            logoImage.preserveAspect = true;
            logoImage.color = new Color(1f, 1f, 1f, 0.98f);
            return;
        }

        Text fallbackTitle = CreateText(logoArea, "LogoFallback", "NYCTOPHOBIA", 58, TitleColor, TextAnchor.MiddleCenter);
        RectTransform fallbackRect = (RectTransform)fallbackTitle.transform;
        StretchFull(fallbackRect);
        Outline outline = fallbackTitle.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.45f);
        outline.effectDistance = new Vector2(2f, -2f);
    }

    private void CreatePanelChrome(RectTransform panel)
    {
        CreateEdgeGlow(panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -18f), new Vector2(-18f, -14f), AccentColor);
        CreateEdgeGlow(panel, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(18f, 14f), new Vector2(-18f, 18f), new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.45f));
    }

    private void ShowMainPage()
    {
        mainPage.SetActive(true);
        creditsPage.SetActive(false);
        SelectButton(playButton);
    }

    private void ShowCreditsPage()
    {
        mainPage.SetActive(false);
        creditsPage.SetActive(true);
        SelectButton(backButton);
    }

    private void StartGame()
    {
        TryBindPlayerController();
        if (playerController == null)
            return;

        hasStartedGame = true;
        playerController.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Destroy(gameObject);
    }

    private void TryBindPlayerController()
    {
        if (playerController == null)
            playerController = FindFirstObjectByType<SimpleFirstPersonController>();

        if (playerController == null)
            return;

        if (uiBuilt && !hasStartedGame && playerController.enabled)
            playerController.enabled = false;
    }

    private void RefreshPlayButtonState()
    {
        if (playButton != null)
            playButton.interactable = playerController != null;

        if (creditsPlayButton != null)
            creditsPlayButton.interactable = playerController != null;
    }

    private static void KeepCursorUnlocked()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private RectTransform CreatePanel(Transform parent, string name, Vector2 anchor, Vector2 size)
    {
        GameObject panelObject = CreateUiObject(name, parent);
        RectTransform rectTransform = (RectTransform)panelObject.transform;
        rectTransform.anchorMin = anchor;
        rectTransform.anchorMax = anchor;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = size;

        Image image = panelObject.AddComponent<Image>();
        image.color = PanelColor;

        Shadow shadow = panelObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        shadow.effectDistance = new Vector2(0f, -18f);
        return rectTransform;
    }

    private RectTransform CreateChildRect(Transform parent, string name)
    {
        return (RectTransform)CreateUiObject(name, parent).transform;
    }

    private Text CreateText(Transform parent, string name, string content, int fontSize, Color color, TextAnchor alignment)
    {
        GameObject textObject = CreateUiObject(name, parent);
        Text text = textObject.AddComponent<Text>();
        text.font = uiFont;
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.supportRichText = true;
        text.raycastTarget = false;
        return text;
    }

    private Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = CreateUiObject(name, parent);
        RectTransform buttonRect = (RectTransform)buttonObject.transform;
        buttonRect.anchorMin = new Vector2(0.5f, 1f);
        buttonRect.anchorMax = new Vector2(0.5f, 1f);
        buttonRect.pivot = new Vector2(0.5f, 1f);
        buttonRect.anchoredPosition = anchoredPosition;
        buttonRect.sizeDelta = new Vector2(320f, 56f);

        Image buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = ButtonColor;

        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.75f);
        outline.effectDistance = new Vector2(1f, -1f);

        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = ButtonColor;
        colors.highlightedColor = HighlightColor;
        colors.selectedColor = HighlightColor;
        colors.pressedColor = PressedColor;
        colors.disabledColor = new Color(0.14f, 0.14f, 0.15f, 0.45f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.targetGraphic = buttonImage;
        button.onClick.AddListener(onClick);

        Text text = CreateText(buttonObject.transform, "Label", label, 24, TitleColor, TextAnchor.MiddleCenter);
        RectTransform textRect = (RectTransform)text.transform;
        StretchFull(textRect);
        return button;
    }

    private static void CreateEdgeGlow(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        GameObject line = CreateUiObject("Accent", parent);
        RectTransform rect = (RectTransform)line.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = line.AddComponent<Image>();
        image.color = color;
    }

    private static void StretchFull(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    private static void SelectButton(Button button)
    {
        if (button == null || EventSystem.current == null)
            return;

        EventSystem.current.SetSelectedGameObject(button.gameObject);
    }
}
