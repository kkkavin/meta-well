using System;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Runtime.Components;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Facades;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Convai.SampleCommon.UI.DynamicContext
{
    /// <summary>
    ///     Self-building runtime panel for testing dynamic context in connected scenes.
    /// </summary>
    public sealed class DynamicContextDebugPanel : MonoBehaviour
    {
        [SerializeField] private ConvaiCharacter _character;
        [SerializeField] private ConvaiManager _manager;
        [SerializeField] private bool _autoResolve = true;
        [SerializeField] private bool _showOnStart = true;
        [SerializeField] private int _sortingOrder = 650;

        private TMP_InputField _stateNameInput;
        private TMP_InputField _stateValueInput;
        private TMP_InputField _queryStateInput;
        private TMP_InputField _eventInput;
        private TMP_InputField _attentionInput;
        private TMP_InputField _rawTextInput;
        private TMP_InputField _updateIdInput;
        private Toggle _removeStaticToggle;
        private TMP_Text _reactionButtonLabel;
        private TMP_Text _modeButtonLabel;
        private TMP_Text _statusText;
        private TMP_Text _resultText;

        private ConvaiContextReactionMode _reactionMode = ConvaiContextReactionMode.SyncOnly;
        private ConvaiContextUpdateMode _rawMode = ConvaiContextUpdateMode.Append;
        private ConvaiEvents _subscribedEvents;
        private bool _built;

        private void Awake()
        {
            BuildUi();
            gameObject.SetActive(_showOnStart);
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeResults();
            RenderStatus();
        }

        private void OnDisable() => UnsubscribeResults();

        private void Update()
        {
            ResolveReferences();
            SubscribeResults();
            RenderStatus();
        }

        public void SetCharacter(ConvaiCharacter character) => _character = character;

        private void SetState(bool flush)
        {
            if (!TryGetCharacter(out ConvaiCharacter character)) return;

            character.DynamicContext.SetState(_stateNameInput.text, _stateValueInput.text, _reactionMode);
            if (flush) character.DynamicContext.Flush();
            WriteResult($"Queued state: {_stateNameInput.text}={_stateValueInput.text}");
        }

        private void QueryState()
        {
            if (!TryGetCharacter(out ConvaiCharacter character)) return;

            string key = string.IsNullOrWhiteSpace(_queryStateInput.text)
                ? _stateNameInput.text
                : _queryStateInput.text;
            if (character.DynamicContext.TryGetStateValue(key, out string value))
                WriteResult($"Local state: {key}={value}");
            else
                WriteResult($"Local state not found: {key}");
        }

        private void RemoveState(bool flush)
        {
            if (!TryGetCharacter(out ConvaiCharacter character)) return;

            character.DynamicContext.RemoveState(_stateNameInput.text);
            if (flush) character.DynamicContext.Flush();
            WriteResult($"Queued state removal: {_stateNameInput.text}");
        }

        private void AddEvent(bool flush)
        {
            if (!TryGetCharacter(out ConvaiCharacter character)) return;

            character.DynamicContext.AddEvent(_eventInput.text, _reactionMode);
            if (flush) character.DynamicContext.Flush();
            WriteResult($"Queued event: {_eventInput.text}");
        }

        private void SetAttention(bool flush)
        {
            if (!TryGetCharacter(out ConvaiCharacter character)) return;

            character.DynamicContext.SetCurrentAttentionObject(_attentionInput.text, _reactionMode);
            if (flush) character.DynamicContext.Flush();
            WriteResult($"Queued attention object: {_attentionInput.text}");
        }

        private void ClearAttention(bool flush)
        {
            if (!TryGetCharacter(out ConvaiCharacter character)) return;

            character.DynamicContext.ClearCurrentAttentionObject(_reactionMode);
            if (flush) character.DynamicContext.Flush();
            WriteResult("Queued attention clear");
        }

        private void ApplyRaw()
        {
            if (!TryGetCharacter(out ConvaiCharacter character)) return;

            var update = new ConvaiDynamicContextUpdate(
                _rawTextInput.text,
                _rawMode,
                _reactionMode,
                _removeStaticToggle != null && _removeStaticToggle.isOn,
                string.IsNullOrWhiteSpace(_attentionInput.text) ? null : _attentionInput.text,
                string.IsNullOrWhiteSpace(_updateIdInput.text) ? null : _updateIdInput.text);

            character.DynamicContext.Apply(update);
            WriteResult($"Applied advanced update: mode={_rawMode}, reaction={_reactionMode}");
        }

        private void Flush()
        {
            if (!TryGetCharacter(out ConvaiCharacter character)) return;

            character.DynamicContext.Flush();
            WriteResult("Flushed pending dynamic context");
        }

        private void ResetContext(bool removeStatic)
        {
            if (!TryGetCharacter(out ConvaiCharacter character)) return;

            character.DynamicContext.Reset(removeStatic);
            character.DynamicContext.Flush();
            WriteResult(removeStatic ? "Reset runtime + static context" : "Reset runtime context");
        }

        private void CycleReaction()
        {
            _reactionMode = _reactionMode switch
            {
                ConvaiContextReactionMode.SyncOnly => ConvaiContextReactionMode.Auto,
                ConvaiContextReactionMode.Auto => ConvaiContextReactionMode.ReactImmediately,
                _ => ConvaiContextReactionMode.SyncOnly
            };
            RenderStatus();
        }

        private void CycleRawMode()
        {
            _rawMode = _rawMode switch
            {
                ConvaiContextUpdateMode.Append => ConvaiContextUpdateMode.Replace,
                ConvaiContextUpdateMode.Replace => ConvaiContextUpdateMode.Reset,
                _ => ConvaiContextUpdateMode.Append
            };
            RenderStatus();
        }

        private void ResolveReferences()
        {
            if (!_autoResolve) return;

            if (_character == null)
                _character = FindAnyObjectByType<ConvaiCharacter>();
            if (_manager == null)
                _manager = ConvaiManager.ActiveManager != null ? ConvaiManager.ActiveManager : FindAnyObjectByType<ConvaiManager>();
        }

        private bool TryGetCharacter(out ConvaiCharacter character)
        {
            ResolveReferences();
            character = _character;
            if (character != null) return true;

            WriteResult("No ConvaiCharacter resolved");
            return false;
        }

        private void SubscribeResults()
        {
            if (_manager == null) return;

            ConvaiEvents events;
            try
            {
                events = _manager.Events;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (ReferenceEquals(_subscribedEvents, events)) return;

            UnsubscribeResults();
            _subscribedEvents = events;
            _subscribedEvents.OnDynamicContextUpdateResultReceived += HandleDynamicContextResult;
        }

        private void UnsubscribeResults()
        {
            if (_subscribedEvents == null) return;

            _subscribedEvents.OnDynamicContextUpdateResultReceived -= HandleDynamicContextResult;
            _subscribedEvents = null;
        }

        private void HandleDynamicContextResult(DynamicContextUpdateResultReceived result)
        {
            WriteResult(
                $"ACK status={result.Status}\n" +
                $"message={result.Message}\n" +
                $"update_id={result.UpdateId}\n" +
                $"revision={result.ContextRevision} tokens={result.TokenCount} remaining={result.RemainingTokens}\n" +
                $"run_llm requested={result.RequestedRunLlm} actual={result.ActualRunLlm}\n" +
                $"downgrade={result.DowngradeReason} interrupted={result.Interrupted} llm={result.LlmTriggered}");
        }

        private void RenderStatus()
        {
            if (!_built) return;

            if (_reactionButtonLabel != null)
                _reactionButtonLabel.text = $"Reaction: {_reactionMode}";
            if (_modeButtonLabel != null)
                _modeButtonLabel.text = $"Advanced mode: {_rawMode}";

            string characterName = _character != null ? _character.CharacterName : "None";
            string conversation = _character != null && _character.IsInConversation ? "connected" : "not connected";
            string manager = _manager != null ? "ready" : "missing";

            if (_statusText != null)
            {
                _statusText.text =
                    $"Character: {characterName}\n" +
                    $"Room: {conversation}\n" +
                    $"Manager: {manager}\n" +
                    $"Batch: 0.5s, Flush sends now";
            }
        }

        private void WriteResult(string text)
        {
            if (_resultText != null)
                _resultText.text = text;
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
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.045f, 0.048f, 0.055f, 0.92f);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 0f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 0.5f);
            panelRect.offsetMin = new Vector2(-470f, 24f);
            panelRect.offsetMax = new Vector2(-24f, -24f);

            var panelLayout = panel.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(14, 14, 14, 14);
            panelLayout.spacing = 8f;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            AddText(panel.transform, "Dynamic Context", 22f, FontStyles.Bold);
            _statusText = AddText(panel.transform, "Resolving...", 13f, FontStyles.Normal, new Color(0.76f, 0.80f, 0.84f, 1f));

            GameObject scrollRoot = CreateUiObject("Scroll", panel.transform);
            LayoutElement scrollLayout = scrollRoot.AddComponent<LayoutElement>();
            scrollLayout.flexibleHeight = 1f;
            var scrollImage = scrollRoot.AddComponent<Image>();
            scrollImage.color = new Color(0.02f, 0.022f, 0.026f, 0.56f);
            scrollRoot.AddComponent<Mask>().showMaskGraphic = false;
            ScrollRect scrollRect = scrollRoot.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;

            GameObject content = CreateUiObject("Content", scrollRoot.transform);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            var contentLayout = content.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(8, 8, 8, 8);
            contentLayout.spacing = 8f;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.content = contentRect;
            scrollRect.viewport = scrollRoot.GetComponent<RectTransform>();

            BuildControls(content.transform);

            _resultText = AddText(panel.transform, "No dynamic context result yet.", 12f, FontStyles.Normal, new Color(0.68f, 0.74f, 0.78f, 1f));
            LayoutElement resultLayout = _resultText.gameObject.AddComponent<LayoutElement>();
            resultLayout.minHeight = 116f;

            _built = true;
            RenderStatus();
        }

        private void BuildControls(Transform parent)
        {
            _reactionButtonLabel = AddButton(parent, "Reaction: SyncOnly", CycleReaction).GetComponentInChildren<TMP_Text>();
            _modeButtonLabel = AddButton(parent, "Advanced mode: Append", CycleRawMode).GetComponentInChildren<TMP_Text>();

            AddSection(parent, "State");
            _stateNameInput = AddInput(parent, "State name", "Player location");
            _stateValueInput = AddInput(parent, "State value", "Lip Sync Lab");
            AddButtonRow(parent,
                ("Set", () => SetState(false)),
                ("Set + Flush", () => SetState(true)),
                ("Remove", () => RemoveState(true)));
            _queryStateInput = AddInput(parent, "Query state name", "Player location");
            AddButton(parent, "Read Local State", QueryState);

            AddSection(parent, "Event");
            _eventInput = AddInput(parent, "Event text", "The player tested dynamic context from the LipSync scene.");
            AddButtonRow(parent,
                ("Add", () => AddEvent(false)),
                ("Add + Flush", () => AddEvent(true)));

            AddSection(parent, "Attention Object");
            _attentionInput = AddInput(parent, "Action object name", "lever");
            AddButtonRow(parent,
                ("Set", () => SetAttention(false)),
                ("Set + Flush", () => SetAttention(true)),
                ("Clear", () => ClearAttention(true)));

            AddSection(parent, "Advanced Update");
            _rawTextInput = AddInput(parent, "Advanced context text", "Scene state: dynamic context debug panel is active.", true);
            _updateIdInput = AddInput(parent, "Update id", string.Empty);
            _removeStaticToggle = AddToggle(parent, "remove_static on reset");
            AddButton(parent, "Apply Advanced Update", ApplyRaw);

            AddSection(parent, "Session Controls");
            AddButtonRow(parent,
                ("Flush", Flush),
                ("Reset Runtime", () => ResetContext(false)),
                ("Reset + Static", () => ResetContext(true)));
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
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color ?? Color.white;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return label;
        }

        private static void AddSection(Transform parent, string title)
        {
            TMP_Text label = AddText(parent, title, 14f, FontStyles.Bold, new Color(0.55f, 0.83f, 0.62f, 1f));
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 22f;
        }

        private static TMP_InputField AddInput(Transform parent, string placeholder, string value, bool multiline = false)
        {
            GameObject go = CreateUiObject("Input", parent);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.105f, 0.112f, 0.125f, 0.96f);
            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.minHeight = multiline ? 78f : 36f;

            TMP_InputField input = go.AddComponent<TMP_InputField>();
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;

            TMP_Text text = AddText(go.transform, value, 13f, FontStyles.Normal, Color.white);
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 5f);
            textRect.offsetMax = new Vector2(-10f, -5f);
            text.alignment = TextAlignmentOptions.MidlineLeft;

            TMP_Text placeholderText = AddText(go.transform, placeholder, 13f, FontStyles.Italic, new Color(0.48f, 0.52f, 0.56f, 1f));
            RectTransform placeholderRect = placeholderText.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(10f, 5f);
            placeholderRect.offsetMax = new Vector2(-10f, -5f);
            placeholderText.alignment = TextAlignmentOptions.MidlineLeft;

            input.textComponent = text;
            input.placeholder = placeholderText;
            input.targetGraphic = image;
            input.text = value;
            return input;
        }

        private static Toggle AddToggle(Transform parent, string label)
        {
            GameObject row = CreateUiObject("Toggle", parent);
            row.AddComponent<HorizontalLayoutGroup>().spacing = 8f;
            LayoutElement layout = row.AddComponent<LayoutElement>();
            layout.minHeight = 28f;

            GameObject box = CreateUiObject("Box", row.transform);
            Image boxImage = box.AddComponent<Image>();
            boxImage.color = new Color(0.16f, 0.18f, 0.20f, 1f);
            LayoutElement boxLayout = box.AddComponent<LayoutElement>();
            boxLayout.minWidth = 24f;
            boxLayout.preferredWidth = 24f;
            boxLayout.minHeight = 24f;

            GameObject check = CreateUiObject("Checkmark", box.transform);
            Image checkImage = check.AddComponent<Image>();
            checkImage.color = new Color(0.43f, 0.81f, 0.53f, 1f);
            RectTransform checkRect = check.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.22f, 0.22f);
            checkRect.anchorMax = new Vector2(0.78f, 0.78f);
            checkRect.offsetMin = Vector2.zero;
            checkRect.offsetMax = Vector2.zero;

            TMP_Text labelText = AddText(row.transform, label, 13f, FontStyles.Normal, Color.white);
            labelText.alignment = TextAlignmentOptions.MidlineLeft;

            Toggle toggle = row.AddComponent<Toggle>();
            toggle.targetGraphic = boxImage;
            toggle.graphic = checkImage;
            toggle.isOn = false;
            return toggle;
        }

        private static Button AddButton(Transform parent, string text, UnityAction onClick)
        {
            GameObject go = CreateUiObject("Button", parent);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.18f, 0.34f, 0.25f, 1f);
            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.minHeight = 34f;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            TMP_Text label = AddText(go.transform, text, 13f, FontStyles.Bold, Color.white);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            label.alignment = TextAlignmentOptions.Center;

            return button;
        }

        private static void AddButtonRow(Transform parent, params (string Label, UnityAction Action)[] buttons)
        {
            GameObject row = CreateUiObject("ButtonRow", parent);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 34f;

            foreach ((string label, UnityAction action) in buttons)
                AddButton(row.transform, label, action);
        }
    }
}
