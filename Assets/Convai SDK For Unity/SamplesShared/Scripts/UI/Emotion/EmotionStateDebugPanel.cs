using System;
using Convai.Modules.Emotion.Components;
using Convai.Runtime.Components;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Convai.SampleCommon.UI.Emotion
{
    /// <summary>
    ///     Small runtime overlay for recording the current backend and resolved emotion state in sample scenes.
    /// </summary>
    public sealed class EmotionStateDebugPanel : MonoBehaviour
    {
        [SerializeField] private ConvaiCharacter _character;
        [SerializeField] private ConvaiEmotionController _emotionController;
        [SerializeField] private bool _autoResolve = true;
        [SerializeField] private bool _showOnStart = true;
        [SerializeField] private int _sortingOrder = 660;

        private TMP_Text _statusText;
        private TMP_Text _configText;
        private TMP_Text _rawEmotionText;
        private TMP_Text _resolvedEmotionText;
        private TMP_Text _flowText;
        private TMP_Text _hintText;
        private bool _built;

        private void Awake()
        {
            BuildUi();
            gameObject.SetActive(_showOnStart);
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (_character != null)
                _character.OnEmotionChanged += HandleEmotionChanged;
            Render();
        }

        private void OnDisable()
        {
            if (_character != null)
                _character.OnEmotionChanged -= HandleEmotionChanged;
        }

        private void Update()
        {
            ResolveReferences();
            Render();
        }

        public void SetCharacter(ConvaiCharacter character)
        {
            if (_character == character) return;

            if (isActiveAndEnabled && _character != null)
                _character.OnEmotionChanged -= HandleEmotionChanged;

            _character = character;
            if (!IsControllerForCharacter(_emotionController, _character))
                _emotionController = null;

            if (isActiveAndEnabled && _character != null)
                _character.OnEmotionChanged += HandleEmotionChanged;

            ResolveEmotionController();
            Render();
        }

        public void SetEmotionController(ConvaiEmotionController emotionController)
        {
            if (_character == null && emotionController != null)
                _character = emotionController.GetComponentInParent<ConvaiCharacter>(true);

            _emotionController = IsControllerForCharacter(emotionController, _character)
                ? emotionController
                : null;
            Render();
        }

        private void HandleEmotionChanged(string emotion, int intensity) => Render();

        private void ResolveReferences()
        {
            if (!_autoResolve) return;

            if (_character == null)
                _character = FindAnyObjectByType<ConvaiCharacter>();

            ResolveEmotionController();
        }

        private void ResolveEmotionController()
        {
            if (_character == null)
            {
                _emotionController = null;
                return;
            }

            if (IsControllerForCharacter(_emotionController, _character)) return;

            _emotionController = _character.GetComponentInChildren<ConvaiEmotionController>(true) ??
                                 _character.GetComponentInParent<ConvaiEmotionController>(true);
        }

        private static bool IsControllerForCharacter(
            ConvaiEmotionController emotionController,
            ConvaiCharacter character)
        {
            if (emotionController == null || character == null) return false;

            return emotionController.GetComponentInParent<ConvaiCharacter>(true) == character ||
                   character.GetComponentInParent<ConvaiEmotionController>(true) == emotionController;
        }

        private void Render()
        {
            if (!_built) return;

            string characterName = _character != null ? _character.CharacterName : "None";
            string roomState = _character != null ? _character.SessionState.ToString() : "missing";
            string detectionMeaning = ResolveDetectionMeaning();

            if (_statusText != null)
                _statusText.text = $"Character: {characterName}\nRoom state: {roomState}";

            if (_configText != null)
                _configText.text =
                    $"Detection config: {ResolveDetectionSource()}\n{detectionMeaning}";

            string rawEmotion = _character != null && !string.IsNullOrWhiteSpace(_character.CurrentEmotion)
                ? _character.CurrentEmotion
                : "neutral";
            int rawIntensity = _character != null ? _character.CurrentEmotionIntensity : 0;
            float rawNormalized = NormalizeIntensity(rawIntensity);

            if (_rawEmotionText != null)
                _rawEmotionText.text =
                    "1. Backend signal\n" +
                    $"Label: {rawEmotion}\n" +
                    $"Scale: {FormatScale(rawIntensity)}\n" +
                    $"Raw intensity: {rawNormalized:0.00}\n" +
                    "Source: ConvaiCharacter";

            string resolvedEmotion = _emotionController != null
                ? _emotionController.CurrentResolvedEmotion
                : "neutral";
            float resolvedIntensity = _emotionController != null
                ? _emotionController.CurrentNormalizedIntensity
                : 0f;

            if (_resolvedEmotionText != null)
                _resolvedEmotionText.text =
                    "2. Controller output\n" +
                    $"Face channel: {resolvedEmotion}\n" +
                    $"Smoothed weight: {resolvedIntensity:0.00}\n" +
                    "Output: blendshapes / Animator\n" +
                    "Source: ConvaiEmotionController";

            if (_flowText != null)
                _flowText.text =
                    "Flow: backend label -> taxonomy -> smoothing -> profile binding -> face output";

            if (_hintText != null)
                _hintText.text = ResolveHint(rawEmotion, rawIntensity, resolvedEmotion, resolvedIntensity);
        }

        private string ResolveDetectionMeaning()
        {
            if (_character == null)
                return "Assign a ConvaiCharacter to inspect runtime emotion state.";

            return "Connect uses backend character details. This panel shows received bot-emotion events and face output.";
        }

        private string ResolveDetectionSource()
        {
            if (_character == null) return "Missing ConvaiCharacter";

            return "Backend character details";
        }

        private string ResolveHint(
            string rawEmotion,
            int rawIntensity,
            string resolvedEmotion,
            float resolvedIntensity)
        {
            if (_character == null)
                return "Assign a ConvaiCharacter to show backend emotion state.";

            if (_emotionController == null)
                return "Assign a ConvaiEmotionController to show face output state.";

            if (rawIntensity <= 0)
                return "Waiting for backend bot-emotion event. Confirm emotion detection is enabled for this character.";

            if (!IsNeutral(rawEmotion) && IsNeutral(resolvedEmotion) && resolvedIntensity <= 0f)
                return "Backend sent an emotion, but controller is neutral. Check taxonomy labels and Console warnings.";

            return "Raw backend label is for logging/UI. Controller output is the smoothed value driving the face.";
        }

        private static bool IsNeutral(string emotion) =>
            string.Equals(emotion, "neutral", StringComparison.OrdinalIgnoreCase);

        private static float NormalizeIntensity(int scale) => Mathf.Clamp01(scale / 3f);

        private static string FormatScale(int scale)
        {
            if (scale <= 0) return "none yet";

            string meaning;
            switch (scale)
            {
                case 1:
                    meaning = "subtle";
                    break;
                case 2:
                    meaning = "normal";
                    break;
                case 3:
                    meaning = "strong";
                    break;
                default:
                    meaning = "clamped";
                    break;
            }

            return $"{scale} / 3 ({meaning})";
        }

        private void BuildUi()
        {
            if (_built) return;

            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = _sortingOrder;

            if (GetComponent<CanvasScaler>() == null)
            {
                CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            GameObject panel = CreateUiObject("Panel", transform);
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.045f, 0.048f, 0.055f, 0.9f);

            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(24f, -24f);
            panelRect.sizeDelta = new Vector2(620f, 332f);

            VerticalLayoutGroup panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(14, 14, 12, 12);
            panelLayout.spacing = 8f;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            AddText(panel.transform, "Emotion Debug: Backend -> Face Output", 20f, FontStyles.Bold);
            _statusText = AddText(panel.transform, "Resolving...", 13f, FontStyles.Normal, new Color(0.76f, 0.80f, 0.84f, 1f));
            _configText = AddText(panel.transform, string.Empty, 13f, FontStyles.Normal, new Color(0.88f, 0.90f, 0.74f, 1f));

            GameObject row = CreateUiObject("Readings", panel.transform);
            HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            LayoutElement rowElement = row.AddComponent<LayoutElement>();
            rowElement.minHeight = 124f;

            _rawEmotionText = AddReading(row.transform);
            _resolvedEmotionText = AddReading(row.transform);
            _flowText = AddText(panel.transform, string.Empty, 12f, FontStyles.Bold, new Color(0.84f, 0.88f, 0.92f, 1f));
            _hintText = AddText(panel.transform, string.Empty, 12f, FontStyles.Normal, new Color(0.68f, 0.74f, 0.78f, 1f));

            _built = true;
            Render();
        }

        private static TMP_Text AddReading(Transform parent)
        {
            GameObject card = CreateUiObject("Reading", parent);
            Image image = card.AddComponent<Image>();
            image.color = new Color(0.02f, 0.022f, 0.026f, 0.62f);
            LayoutElement layout = card.AddComponent<LayoutElement>();
            layout.minHeight = 124f;
            layout.flexibleWidth = 1f;

            TMP_Text text = AddText(card.transform, string.Empty, 12f, FontStyles.Normal, Color.white);
            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10f, 8f);
            rect.offsetMax = new Vector2(-10f, -8f);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            return text;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static TMP_Text AddText(
            Transform parent,
            string text,
            float size,
            FontStyles style = FontStyles.Normal,
            Color? color = null)
        {
            GameObject go = CreateUiObject("Text", parent);
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color ?? Color.white;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return label;
        }
    }
}
