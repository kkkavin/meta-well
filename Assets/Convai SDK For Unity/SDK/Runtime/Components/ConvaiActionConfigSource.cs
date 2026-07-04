using System;
using System.Collections.Generic;
using Convai.Runtime.Actions;
using Convai.Shared.Actions;
using UnityEngine;

namespace Convai.Runtime.Components
{
    /// <summary>
    ///     Explicit action affordance authoring surface for a <see cref="ConvaiCharacter" />.
    /// </summary>
    [AddComponentMenu("Convai/Convai Action Config Source")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ConvaiCharacter))]
    public sealed class ConvaiActionConfigSource : MonoBehaviour
    {
        [Header("Action Definitions")]
        [SerializeField]
        [Tooltip("Typed action definitions that bind backend action names to executor components.")]
        private List<ConvaiActionDefinition> _definitions = new();

        [Header("Actionable Objects")]
        [SerializeField]
        [Tooltip("Explicit object targets the backend may use for action grounding.")]
        private List<ConvaiActionObjectDefinition> _objects = new();

        [Header("Actionable Characters")]
        [SerializeField]
        [Tooltip("Explicit character targets the backend may use for action grounding.")]
        private List<ConvaiActionCharacterDefinition> _characters = new();

        [Header("Initial Attention")]
        [SerializeField]
        [Tooltip("Optional initial object name to seed current_attention_object on connect.")]
        private string _initialAttentionObject;

        public IReadOnlyList<ConvaiActionDefinition> Definitions => _definitions;
        public IReadOnlyList<ConvaiActionObjectDefinition> Objects => _objects;
        public IReadOnlyList<ConvaiActionCharacterDefinition> Characters => _characters;
        public string InitialAttentionObject => _initialAttentionObject;

        public ConvaiActionConfig BuildActionConfig()
        {
            bool hasData = (_definitions?.Count ?? 0) > 0 ||
                           (_objects?.Count ?? 0) > 0 ||
                           (_characters?.Count ?? 0) > 0 ||
                           !string.IsNullOrWhiteSpace(_initialAttentionObject);
            if (!hasData)
                return null;

            var config = new ConvaiActionConfig();

            IReadOnlyList<ConvaiActionDefinition> definitions = GetEffectiveDefinitions(requireExecutable: true);
            if (definitions.Count == 0)
            {
                if ((_objects?.Count ?? 0) > 0 ||
                    (_characters?.Count ?? 0) > 0 ||
                    !string.IsNullOrWhiteSpace(_initialAttentionObject))
                {
                    Debug.LogWarning(
                        $"[ConvaiActionConfigSource] '{name}' has action targets or initial attention but no valid action definitions. Omitting action_config.",
                        this);
                }

                return null;
            }

            for (int i = 0; i < definitions.Count; i++)
                config.Actions.Add(definitions[i].ActionName);

            if (_objects != null)
            {
                foreach (ConvaiActionObjectDefinition actionObject in _objects)
                    config.Objects.Add(actionObject?.Clone() ?? new ConvaiActionObjectDefinition());
            }

            if (_characters != null)
            {
                foreach (ConvaiActionCharacterDefinition character in _characters)
                    config.Characters.Add(character?.Clone() ?? new ConvaiActionCharacterDefinition());
            }

            string initialAttentionObject = NormalizeName(_initialAttentionObject);
            if (!string.IsNullOrEmpty(initialAttentionObject))
            {
                if (TryFindObjectName(config.Objects, initialAttentionObject, out string resolvedObjectName))
                {
                    config.CurrentAttentionObject = resolvedObjectName;
                }
                else
                {
                    Debug.LogWarning(
                        $"[ConvaiActionConfigSource] Initial attention object '{initialAttentionObject}' on '{name}' does not match any authored action object. Omitting current_attention_object.",
                        this);
                }
            }

            return HasConfigData(config) ? config : null;
        }

        internal IReadOnlyList<ConvaiActionDefinition> GetEffectiveDefinitions(
            IReadOnlyList<string> allowedActionNames = null,
            bool requireExecutable = false)
        {
            return ConvaiActionDefinition.FilterAndClone(
                _definitions,
                allowedActionNames,
                actionName => Debug.LogWarning(
                    $"[ConvaiActionConfigSource] Duplicate action definition '{actionName}' on '{name}'. Keeping the first definition only.",
                    this),
                requireExecutable);
        }

        public bool TryResolveObject(string objectName, out ConvaiActionObjectDefinition actionObject)
        {
            actionObject = null;
            if (string.IsNullOrWhiteSpace(objectName) || _objects == null)
                return false;

            for (int i = 0; i < _objects.Count; i++)
            {
                ConvaiActionObjectDefinition candidate = _objects[i];
                if (candidate == null ||
                    !string.Equals(candidate.Name, objectName, StringComparison.OrdinalIgnoreCase))
                    continue;

                actionObject = candidate;
                return true;
            }

            return false;
        }

        private static bool TryFindObjectName(
            IReadOnlyList<ConvaiActionObjectDefinition> objects,
            string objectName,
            out string resolvedObjectName)
        {
            resolvedObjectName = null;
            if (objects == null)
                return false;

            for (int i = 0; i < objects.Count; i++)
            {
                string candidateName = NormalizeName(objects[i]?.Name);
                if (string.IsNullOrEmpty(candidateName) ||
                    !string.Equals(candidateName, objectName, StringComparison.OrdinalIgnoreCase))
                    continue;

                resolvedObjectName = candidateName;
                return true;
            }

            return false;
        }

        private static bool HasConfigData(ConvaiActionConfig config)
        {
            return config != null &&
                   ((config.Actions?.Count ?? 0) > 0 ||
                    (config.Objects?.Count ?? 0) > 0 ||
                    (config.Characters?.Count ?? 0) > 0 ||
                    !string.IsNullOrWhiteSpace(config.CurrentAttentionObject));
        }

        private static string NormalizeName(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
