using System.Collections;
using Convai.Domain.Logging;
using Convai.Runtime.Components;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Logging;
using Convai.Runtime.Room;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Convai.SampleCommon.UI.DynamicContext
{
    /// <summary>
    ///     Runtime tester for tracked character dynamic context plus advanced raw updates.
    /// </summary>
    public class SampleDynamicContextUI : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Initial connection-request context shown for the selected character.")]
        private TextMeshProUGUI _initialContextText;

        [SerializeField]
        [Tooltip("Optional ConvaiCharacter source. If not set, first character in scene is used.")]
        private ConvaiCharacter _convaiCharacter;

        [Header("Tracked State")]
        [SerializeField]
        [Tooltip("Fallback state name used when no state-name input field is assigned.")]
        private string _stateName = string.Empty;

        [SerializeField]
        [Tooltip("Fallback state value used when no state-value input field is assigned.")]
        private string _stateValue = string.Empty;

        [SerializeField]
        [Tooltip("Input field used to edit state name.")]
        private TMP_InputField _stateNameInputField;

        [SerializeField]
        [Tooltip("Input field used to edit state value.")]
        private TMP_InputField _stateValueInputField;

        [SerializeField]
        [Tooltip("Button that sets one tracked state on the character.")]
        private Button _setStateButton;

        [Header("Tracked Event")]
        [SerializeField]
        [Tooltip("Fallback event text used when no event input field is assigned.")]
        private string _eventText = string.Empty;

        [SerializeField]
        [Tooltip("Input field used to edit event text.")]
        private TMP_InputField _eventInputField;

        [SerializeField]
        [Tooltip("Button that appends one chronological event to tracked context.")]
        private Button _addEventButton;

        [Header("Advanced Raw Update")]
        [SerializeField]
        [TextArea(2, 6)]
        [Tooltip("Fallback raw context text used when no raw input field is assigned.")]
        private string _rawContextText = string.Empty;

        [SerializeField]
        [Tooltip("Fallback raw update mode used when mode toggles are not assigned.")]
        private ConvaiContextUpdateMode _rawMode = ConvaiContextUpdateMode.Append;

        [SerializeField]
        [Tooltip("Fallback reaction mode used when reaction toggles are not assigned. Labels: Let Character Decide / React Immediately / Update Only.")]
        private ConvaiContextReactionMode _reactionMode = ConvaiContextReactionMode.Auto;

        [SerializeField]
        [Tooltip("Input field used to edit raw context text.")]
        private TMP_InputField _contextInputField;

        [SerializeField]
        [Tooltip("Raw update mode toggle: Append.")]
        private Toggle _modeAppendToggle;

        [SerializeField]
        [Tooltip("Raw update mode toggle: Replace.")]
        private Toggle _modeReplaceToggle;

        [SerializeField]
        [Tooltip("Raw update mode toggle: Reset.")]
        private Toggle _modeResetToggle;

        [SerializeField]
        [Tooltip("Reaction toggle: Let Character Decide.")]
        private Toggle _runLlmAutoToggle;

        [SerializeField]
        [Tooltip("Reaction toggle: React Immediately.")]
        private Toggle _runLlmTrueToggle;

        [SerializeField]
        [Tooltip("Reaction toggle: Update Only.")]
        private Toggle _runLlmFalseToggle;

        [SerializeField]
        [Tooltip("Button that sends one advanced raw dynamic context update.")]
        private Button _sendButton;

        [SerializeField]
        [Tooltip("Button that clears tracked character dynamic context.")]
        private Button _resetButton;

        [SerializeField]
        [Tooltip("Microphone toggle.")]
        private Toggle _micToggle;

        private IConvaiRoomAudioService _audioService;

        private IEnumerator Start()
        {
            const float resolveTimeoutSeconds = 30f;
            float deadline = Time.realtimeSinceStartup + resolveTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                ConvaiManager manager = ConvaiManager.ActiveManager;
                if (manager != null && manager.TryGetRoomAudioService(out IConvaiRoomAudioService audio))
                {
                    _audioService = audio;
                    break;
                }

                yield return null;
            }

            ApplyStateToUi();
            UpdateInitialContextLabelFromCharacter();

            if (_micToggle != null)
                _micToggle.isOn = _audioService?.IsMicMuted ?? false;

            if (_setStateButton != null)
                _setStateButton.onClick.AddListener(SetStateContent);
            if (_addEventButton != null)
                _addEventButton.onClick.AddListener(AddEventContent);
            if (_sendButton != null)
                _sendButton.onClick.AddListener(SendContent);
            if (_resetButton != null)
                _resetButton.onClick.AddListener(ResetContent);
            if (_micToggle != null)
                _micToggle.onValueChanged.AddListener(OnMicToggleValueChanged);
        }

        private void OnMicToggleValueChanged(bool isOn)
        {
            if (_audioService == null)
            {
                ConvaiLogger.Warning(
                    "[SampleDynamicContextUI] No IConvaiRoomAudioService; connect to a room first.",
                    LogCategory.SDK);
                return;
            }

            _audioService.SetMicMuted(isOn);
        }

        private void UpdateInitialContextLabelFromCharacter()
        {
            if (_initialContextText == null) return;

            ConvaiCharacter character = ResolveCharacter();
            if (character == null)
            {
                _initialContextText.text = "Initial context not set";
                return;
            }

            bool keepInContext = character.InitialDynamicInfoKeepInContext;
            string initialText = character.InitialDynamicInfoText;

            _initialContextText.text = keepInContext && !string.IsNullOrWhiteSpace(initialText)
                ? initialText
                : "Initial context not set";
        }

        /// <summary>
        ///     Sets one tracked state on the target character.
        /// </summary>
        public void SetStateContent()
        {
            UpdateStateFromUi();

            ConvaiCharacter character = ResolveCharacter();
            if (character == null)
            {
                ConvaiLogger.Warning("[SampleDynamicContextUI] No ConvaiCharacter found for state update.", LogCategory.SDK);
                return;
            }

            character.DynamicContext.SetState(_stateName, _stateValue, _reactionMode);
            ConvaiLogger.Info(
                $"[SampleDynamicContextUI] Set state: {_stateName}={_stateValue} ({GetReactionLabel(_reactionMode)})",
                LogCategory.SDK);
        }

        /// <summary>
        ///     Appends one tracked event to the target character.
        /// </summary>
        public void AddEventContent()
        {
            UpdateStateFromUi();

            ConvaiCharacter character = ResolveCharacter();
            if (character == null)
            {
                ConvaiLogger.Warning("[SampleDynamicContextUI] No ConvaiCharacter found for event update.", LogCategory.SDK);
                return;
            }

            character.DynamicContext.AddEvent(_eventText, _reactionMode);
            ConvaiLogger.Info(
                $"[SampleDynamicContextUI] Added event ({GetReactionLabel(_reactionMode)}): {_eventText}",
                LogCategory.SDK);
        }

        /// <summary>
        ///     Sends one advanced raw update through the character-owned dynamic-context surface.
        /// </summary>
        public void SendContent()
        {
            UpdateStateFromUi();

            ConvaiCharacter character = ResolveCharacter();
            if (character == null)
            {
                ConvaiLogger.Warning("[SampleDynamicContextUI] No ConvaiCharacter found for raw update.", LogCategory.SDK);
                return;
            }

            character.DynamicContext.Apply(new ConvaiDynamicContextUpdate(_rawContextText, _rawMode, _reactionMode));
            ConvaiLogger.Info(
                $"[SampleDynamicContextUI] Sent raw update: mode={_rawMode}, reaction={GetReactionLabel(_reactionMode)}, text=\"{_rawContextText}\"",
                LogCategory.SDK);
        }

        /// <summary>
        ///     Clears tracked character dynamic context and resets local UI values.
        /// </summary>
        public void ResetContent()
        {
            _stateName = string.Empty;
            _stateValue = string.Empty;
            _eventText = string.Empty;
            _rawContextText = string.Empty;
            _rawMode = ConvaiContextUpdateMode.Append;
            _reactionMode = ConvaiContextReactionMode.Auto;
            ApplyStateToUi();

            ConvaiCharacter character = ResolveCharacter();
            if (character == null)
            {
                ConvaiLogger.Warning("[SampleDynamicContextUI] No ConvaiCharacter found for reset.", LogCategory.SDK);
                return;
            }

            character.DynamicContext.Reset();
            ConvaiLogger.Info("[SampleDynamicContextUI] Reset tracked character dynamic context.", LogCategory.SDK);
        }

        private void UpdateStateFromUi()
        {
            if (_stateNameInputField != null)
                _stateName = _stateNameInputField.text;

            if (_stateValueInputField != null)
                _stateValue = _stateValueInputField.text;

            if (_eventInputField != null)
                _eventText = _eventInputField.text;

            if (_contextInputField != null)
                _rawContextText = _contextInputField.text;

            if (_modeAppendToggle != null || _modeReplaceToggle != null || _modeResetToggle != null)
            {
                if (_modeResetToggle != null && _modeResetToggle.isOn)
                    _rawMode = ConvaiContextUpdateMode.Reset;
                else if (_modeReplaceToggle != null && _modeReplaceToggle.isOn)
                    _rawMode = ConvaiContextUpdateMode.Replace;
                else
                    _rawMode = ConvaiContextUpdateMode.Append;
            }

            if (_runLlmAutoToggle != null || _runLlmTrueToggle != null || _runLlmFalseToggle != null)
            {
                if (_runLlmTrueToggle != null && _runLlmTrueToggle.isOn)
                    _reactionMode = ConvaiContextReactionMode.ReactImmediately;
                else if (_runLlmFalseToggle != null && _runLlmFalseToggle.isOn)
                    _reactionMode = ConvaiContextReactionMode.SyncOnly;
                else
                    _reactionMode = ConvaiContextReactionMode.Auto;
            }
        }

        private void ApplyStateToUi()
        {
            if (_stateNameInputField != null)
                _stateNameInputField.text = _stateName;

            if (_stateValueInputField != null)
                _stateValueInputField.text = _stateValue;

            if (_eventInputField != null)
                _eventInputField.text = _eventText;

            if (_contextInputField != null)
                _contextInputField.text = _rawContextText;

            if (_modeAppendToggle != null)
                _modeAppendToggle.isOn = _rawMode == ConvaiContextUpdateMode.Append;
            if (_modeReplaceToggle != null)
                _modeReplaceToggle.isOn = _rawMode == ConvaiContextUpdateMode.Replace;
            if (_modeResetToggle != null)
                _modeResetToggle.isOn = _rawMode == ConvaiContextUpdateMode.Reset;

            if (_runLlmAutoToggle != null)
                _runLlmAutoToggle.isOn = _reactionMode == ConvaiContextReactionMode.Auto;
            if (_runLlmTrueToggle != null)
                _runLlmTrueToggle.isOn = _reactionMode == ConvaiContextReactionMode.ReactImmediately;
            if (_runLlmFalseToggle != null)
                _runLlmFalseToggle.isOn = _reactionMode == ConvaiContextReactionMode.SyncOnly;
        }

        private ConvaiCharacter ResolveCharacter()
        {
            if (_convaiCharacter != null) return _convaiCharacter;

            _convaiCharacter = FindAnyObjectByType<ConvaiCharacter>();
            return _convaiCharacter;
        }

        private static string GetReactionLabel(ConvaiContextReactionMode reaction) => reaction switch
        {
            ConvaiContextReactionMode.ReactImmediately => "React Immediately",
            ConvaiContextReactionMode.SyncOnly => "Update Only",
            _ => "Let Character Decide"
        };
    }
}
