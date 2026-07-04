using System.Collections.Generic;
using Convai.Domain.Logging;
using Convai.Runtime.Behaviors;
using Convai.Runtime.Components;
using Convai.Runtime.Logging;
using Convai.Runtime.Presentation.Presenters;
using Convai.Runtime.Presentation.Services;
using Convai.Runtime.Presentation.Services.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Convai.Runtime.Presentation.Views.Transcript
{
    /// <summary>
    ///     Sample chat-style transcript UI that displays a scrollable message history.
    ///     This is a reference implementation showing how to implement ITranscriptUI.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Reference implementation of <see cref="ITranscriptUI" />.
    ///         Message aggregation is handled by
    ///         <see cref="Convai.Runtime.Presentation.Strategies.ChatPresentationStrategy" />.
    ///     </para>
    /// </remarks>
    public class ChatTranscriptUI : MonoBehaviour, ITranscriptUI
    {
        [Header("UI References")] [SerializeField]
        private ScrollRect scrollRect;

        [SerializeField] private RectTransform chatContainer;
        [SerializeField] private GameObject characterMessagePrefab;
        [SerializeField] private GameObject playerMessagePrefab;
        [SerializeField] private TMP_InputField chatInputField;

        [Header("Fade Settings")] [SerializeField]
        private CanvasFader canvasFader;

        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private float fadeDuration = 0.5f;

        private readonly Dictionary<string, GameObject> _activeMessages = new();
        private IAgentRegistry _agentRegistry;
        private bool _isActive;
        private bool _isInjected;
        private IPlayerInputService _playerInput;

        private void Awake()
        {
            ConvaiManager.ActiveManager?.RegisterTranscriptUI(this);
            if (canvasFader == null)
                canvasFader = GetComponentInChildren<CanvasFader>();
            if (canvasGroup == null)
                canvasGroup = GetComponentInChildren<CanvasGroup>();

            if (chatContainer == null)
            {
                ConvaiLogger.Warning("[ChatTranscriptUI] chatContainer is not assigned - messages will not display",
                    LogCategory.UI);
            }

            if (scrollRect == null)
            {
                ConvaiLogger.Warning("[ChatTranscriptUI] scrollRect is not assigned - auto-scroll will not work",
                    LogCategory.UI);
            }
        }

        private void Start()
        {
            TryResolveDependencies();
            if (!_isInjected)
            {
                ConvaiLogger.Warning(
                    "[ChatTranscriptUI] Dependencies not injected - ensure ConvaiManager is present in scene",
                    LogCategory.UI);
            }
        }

        private void OnEnable()
        {
            TryResolveDependencies();
            if (chatInputField != null) chatInputField.onSubmit.AddListener(OnChatInputSubmit);
        }

        private void OnDisable()
        {
            if (chatInputField != null) chatInputField.onSubmit.RemoveListener(OnChatInputSubmit);
        }

        private void Update()
        {
            if (!IsActive || chatInputField == null) return;

            if (!chatInputField.isFocused && IsEnterKeyPressed())
            {
                chatInputField.ActivateInputField();
            }
        }

        private bool IsEnterKeyPressed()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#elif ENABLE_INPUT_SYSTEM
            return IsInputSystemKeyPressedThisFrame("Enter", "NumpadEnter");
#else
            try
            {
                return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            }
            catch
            {
                return false;
            }
#endif
        }

        private static bool _inputSystemReflectionChecked;
        private static System.Type _keyboardType;
        private static System.Type _keyType;
        private static System.Reflection.PropertyInfo _currentKeyboardProp;
        private static System.Reflection.PropertyInfo _indexerProp;
        private static System.Reflection.PropertyInfo _wasPressedProperty;
        private static readonly Dictionary<string, object> _inputSystemKeyCache = new();

        private static bool IsInputSystemKeyPressedThisFrame(params string[] keyNames)
        {
            EnsureInputSystemReflection();

            if (_keyboardType == null || _keyType == null || _currentKeyboardProp == null || _indexerProp == null)
                return false;

            object keyboard = _currentKeyboardProp.GetValue(null);
            if (keyboard == null)
                return false;

            for (int i = 0; i < keyNames.Length; i++)
            {
                if (IsInputSystemKeyPressedThisFrame(keyboard, keyNames[i]))
                    return true;
            }

            return false;
        }

        private static void EnsureInputSystemReflection()
        {
            if (_inputSystemReflectionChecked)
                return;

            _inputSystemReflectionChecked = true;
            try
            {
                _keyboardType = System.Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
                _keyType = System.Type.GetType("UnityEngine.InputSystem.Key, Unity.InputSystem");
                if (_keyboardType != null && _keyType != null)
                {
                    _currentKeyboardProp = _keyboardType.GetProperty(
                        "current",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    _indexerProp = _keyboardType.GetProperty("Item", new[] { _keyType });
                }
            }
            catch
            {
                // Input System may be absent from host project assemblies.
            }
        }

        private static bool IsInputSystemKeyPressedThisFrame(object keyboard, string keyName)
        {
            if (!TryResolveInputSystemKey(keyName, out object key))
                return false;

            object keyControl = _indexerProp.GetValue(keyboard, new[] { key });
            if (keyControl == null)
                return false;

            _wasPressedProperty ??= keyControl.GetType().GetProperty(
                "wasPressedThisFrame",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            return _wasPressedProperty?.GetValue(keyControl) is bool wasPressed && wasPressed;
        }

        private static bool TryResolveInputSystemKey(string keyName, out object key)
        {
            if (_inputSystemKeyCache.TryGetValue(keyName, out key))
                return true;

            try
            {
                key = System.Enum.Parse(_keyType, keyName, false);
                _inputSystemKeyCache[keyName] = key;
                return true;
            }
            catch
            {
                key = null;
                return false;
            }
        }

        private void OnDestroy() => ConvaiManager.ActiveManager?.UnregisterTranscriptUI(this);

        /// <summary>
        ///     Gets the unique identifier for this Chat UI instance.
        ///     Must match TranscriptUIMode.Chat for mode-based activation.
        /// </summary>
        public string Identifier => "Chat";

        /// <summary>
        ///     Gets whether this UI is currently active and visible.
        /// </summary>
        public bool IsActive => _isActive && gameObject.activeInHierarchy;

        public void Inject(IAgentRegistry agentRegistry, IPlayerInputService playerInput)
        {
            _agentRegistry = agentRegistry;
            _playerInput = playerInput;
            _isInjected = true;

            if (_agentRegistry == null)
            {
                ConvaiLogger.Warning(
                    "[ChatTranscriptUI] IAgentRegistry not available - character lookups will fail",
                    LogCategory.UI);
            }

            if (_playerInput == null)
            {
                ConvaiLogger.Warning("[ChatTranscriptUI] IPlayerInputService not available - text input will not work",
                    LogCategory.UI);
            }

            ConvaiLogger.Info("[ChatTranscriptUI] Dependencies injected via explicit initialization", LogCategory.UI);
        }

        private void TryResolveDependencies()
        {
            if (_isInjected) return;

            ConvaiManager manager = ConvaiManager.ActiveManager;
            if (manager == null) return;

            manager.TryGetAgentRegistry(out IAgentRegistry agentRegistry);
            manager.TryGetPlayerInputService(out IPlayerInputService playerInput);
            Inject(agentRegistry, playerInput);
        }

        private void OnChatInputSubmit(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            if (!_isInjected)
            {
                ConvaiLogger.Warning("[ChatTranscriptUI] Cannot send message - dependencies not injected",
                    LogCategory.UI);
                return;
            }

            if (_playerInput == null || !_playerInput.HasPlayer)
            {
                ConvaiLogger.Info("[ChatTranscriptUI] No player found", LogCategory.UI);
                return;
            }

            chatInputField.SetTextWithoutNotify(string.Empty);
            _playerInput.Player.SendTextMessage(text);

            chatInputField.ActivateInputField();
        }

        private void UpdateMessageBubble(GameObject messageObj, TranscriptViewModel viewModel)
        {
            var bubble = messageObj.GetComponent<ChatMessageBubble>();
            if (bubble != null)
            {
                bubble.SetSender(viewModel.DisplayName);
                bubble.SetMessage(viewModel.Text);

                if (viewModel.Speaker == TranscriptSpeaker.Character &&
                    _agentRegistry != null &&
                    _agentRegistry.TryGetCharacter(viewModel.PlayerOrCharacterId, out IConvaiCharacterAgent character))
                    bubble.SetSenderColor(character.NameTagColor);
            }
            else
            {
                var textComponent = messageObj.GetComponentInChildren<TMP_Text>();
                if (textComponent != null) textComponent.text = $"{viewModel.DisplayName}: {viewModel.Text}";
            }
        }

        private void ScrollToBottom()
        {
            if (scrollRect == null) return;

            Canvas.ForceUpdateCanvases();

            if (chatContainer != null) LayoutRebuilder.ForceRebuildLayoutImmediate(chatContainer);

            scrollRect.verticalNormalizedPosition = 0;
        }

        #region ITranscriptUI Implementation

        /// <summary>
        ///     Displays or updates a transcript message.
        ///     Receives pre-aggregated view models from ChatPresentationStrategy.
        /// </summary>
        public void DisplayMessage(TranscriptViewModel viewModel)
        {
            bool hadEntry = _activeMessages.TryGetValue(viewModel.MessageId, out GameObject messageObj);

            if (!hadEntry)
            {
                if (string.IsNullOrEmpty(viewModel.Text)) return;

                GameObject prefab = viewModel.Speaker == TranscriptSpeaker.Character
                    ? characterMessagePrefab
                    : playerMessagePrefab;
                messageObj = Instantiate(prefab, chatContainer);
                messageObj.SetActive(true);
                _activeMessages.Add(viewModel.MessageId, messageObj);

                var bubble = messageObj.GetComponent<ChatMessageBubble>();
                if (bubble != null)
                {
                    bubble.Identifier = viewModel.MessageId;
                    bubble.SetAgentRegistry(_agentRegistry);
                }
            }

            UpdateMessageBubble(messageObj, viewModel);
            ScrollToBottom();
        }

        /// <summary>
        ///     Marks a message as completed.
        ///     Called by TranscriptUIController when ChatPresentationStrategy signals completion.
        /// </summary>
        public void CompleteMessage(string messageId)
        {
            if (_activeMessages.TryGetValue(messageId, out GameObject messageObj))
            {
                var bubble = messageObj.GetComponent<ChatMessageBubble>();
                if (bubble != null) bubble.IsCompleted = true;
            }
        }

        /// <summary>
        ///     Clears all displayed messages.
        /// </summary>
        public void ClearAll()
        {
            if (chatContainer != null)
            {
                foreach (Transform child in chatContainer.transform)
                {
                    if (child.gameObject != characterMessagePrefab &&
                        child.gameObject != playerMessagePrefab)
                        Destroy(child.gameObject);
                }
            }

            _activeMessages.Clear();
            ConvaiLogger.Info("[ChatTranscriptUI] Chat reset - all messages cleared", LogCategory.UI);
        }

        /// <summary>
        ///     Sets the active/visible state of this UI.
        /// </summary>
        public void SetActive(bool active)
        {
            _isActive = active;
            gameObject.SetActive(active);

            if (active && canvasFader != null && canvasGroup != null)
                canvasFader.StartFadeIn(canvasGroup, fadeDuration);
        }

        /// <summary>
        ///     Completes all active player messages.
        ///     Called when player turn ends - strategy handles aggregation, this just logs.
        /// </summary>
        public void CompletePlayerTurn() =>
            ConvaiLogger.Debug("[ChatTranscriptUI] Player turn completed", LogCategory.UI);

        #endregion
    }
}
