using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Runtime.Components;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using UnityEngine;

namespace Convai.Runtime.Actions
{
    public enum ConvaiActionTargetKind
    {
        None = 0,
        Object = 1,
        Character = 2
    }

    public enum ConvaiActionTargetRequirement
    {
        None = 0,
        Object = 1,
        Character = 2,
        Either = 3
    }

    [Serializable]
    public sealed class ConvaiResolvedActionTarget
    {
        public ConvaiActionTargetKind Kind { get; private set; }
        public string Name { get; private set; }
        public ConvaiActionObjectDefinition ObjectBinding { get; private set; }
        public ConvaiActionCharacterDefinition CharacterBinding { get; private set; }

        public GameObject GameObjectReference => Kind switch
        {
            ConvaiActionTargetKind.Object => ObjectBinding?.GameObjectReference,
            ConvaiActionTargetKind.Character => CharacterBinding?.GameObjectReference,
            _ => null
        };

        internal static ConvaiResolvedActionTarget FromObject(ConvaiActionObjectDefinition actionObject) =>
            new()
            {
                Kind = ConvaiActionTargetKind.Object,
                Name = actionObject?.Name ?? string.Empty,
                ObjectBinding = actionObject
            };

        internal static ConvaiResolvedActionTarget FromCharacter(ConvaiActionCharacterDefinition character) =>
            new()
            {
                Kind = ConvaiActionTargetKind.Character,
                Name = character?.Name ?? string.Empty,
                CharacterBinding = character
            };

        internal static ConvaiResolvedActionTarget Resolve(string targetName, ConvaiActionConfig actionConfig) =>
            Resolve(targetName, actionConfig, null);

        internal static ConvaiResolvedActionTarget Resolve(
            string targetName,
            ConvaiActionConfig actionConfig,
            ConvaiActionTargetRequirement? targetRequirement)
        {
            if (string.IsNullOrWhiteSpace(targetName) || actionConfig == null)
                return null;

            if (targetRequirement == ConvaiActionTargetRequirement.Character)
                return ResolveCharacter(targetName, actionConfig) ?? ResolveObject(targetName, actionConfig);

            if (targetRequirement == ConvaiActionTargetRequirement.Object)
                return ResolveObject(targetName, actionConfig) ?? ResolveCharacter(targetName, actionConfig);

            return ResolveObject(targetName, actionConfig) ?? ResolveCharacter(targetName, actionConfig);
        }

        private static ConvaiResolvedActionTarget ResolveObject(string targetName, ConvaiActionConfig actionConfig)
        {
            IReadOnlyList<ConvaiActionObjectDefinition> objects = actionConfig.Objects;
            if (objects != null)
            {
                for (int i = 0; i < objects.Count; i++)
                {
                    ConvaiActionObjectDefinition actionObject = objects[i];
                    if (actionObject == null ||
                        !string.Equals(actionObject.Name, targetName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return FromObject(actionObject);
                }
            }

            return null;
        }

        private static ConvaiResolvedActionTarget ResolveCharacter(string targetName, ConvaiActionConfig actionConfig)
        {
            IReadOnlyList<ConvaiActionCharacterDefinition> characters = actionConfig.Characters;
            if (characters != null)
            {
                for (int i = 0; i < characters.Count; i++)
                {
                    ConvaiActionCharacterDefinition character = characters[i];
                    if (character == null ||
                        !string.Equals(character.Name, targetName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return FromCharacter(character);
                }
            }

            return null;
        }
    }

    [Serializable]
    public sealed class ConvaiActionDefinition
    {
        public string ActionName;
        public ConvaiActionTargetRequirement TargetRequirement;
        public MonoBehaviour Executor;
        public float TimeoutSeconds;

        public ConvaiActionDefinition Clone() =>
            new()
            {
                ActionName = NormalizeActionName(ActionName),
                TargetRequirement = TargetRequirement,
                Executor = Executor,
                TimeoutSeconds = TimeoutSeconds
            };

        public static List<ConvaiActionDefinition> CloneList(IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            var clone = new List<ConvaiActionDefinition>(definitions?.Count ?? 0);
            if (definitions == null)
                return clone;

            for (int i = 0; i < definitions.Count; i++)
                clone.Add(definitions[i]?.Clone());

            return clone;
        }

        internal static List<ConvaiActionDefinition> FilterAndClone(
            IReadOnlyList<ConvaiActionDefinition> definitions,
            IReadOnlyList<string> allowedActionNames = null,
            Action<string> onDuplicate = null,
            bool requireExecutable = false)
        {
            if (definitions == null || definitions.Count == 0)
                return new List<ConvaiActionDefinition>();

            HashSet<string> allowed = BuildAllowedNameSet(allowedActionNames);
            var filtered = new List<ConvaiActionDefinition>(definitions.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                string actionName = NormalizeActionName(definition?.ActionName);
                if (string.IsNullOrEmpty(actionName))
                    continue;

                if (allowed != null && !allowed.Contains(actionName))
                    continue;

                if (requireExecutable && !ConvaiActionConfigValidator.IsExecutableDefinition(definition))
                    continue;

                if (!seen.Add(actionName))
                {
                    onDuplicate?.Invoke(actionName);
                    continue;
                }

                filtered.Add(definition.Clone());
            }

            return filtered;
        }

        internal static Dictionary<string, ConvaiActionDefinition> BuildLookup(
            IReadOnlyList<ConvaiActionDefinition> definitions)
        {
            var lookup = new Dictionary<string, ConvaiActionDefinition>(StringComparer.OrdinalIgnoreCase);
            if (definitions == null)
                return lookup;

            for (int i = 0; i < definitions.Count; i++)
            {
                ConvaiActionDefinition definition = definitions[i];
                string actionName = NormalizeActionName(definition?.ActionName);
                if (string.IsNullOrEmpty(actionName) || lookup.ContainsKey(actionName))
                    continue;

                lookup[actionName] = definition;
            }

            return lookup;
        }

        internal static string NormalizeActionName(string actionName) =>
            string.IsNullOrWhiteSpace(actionName) ? string.Empty : actionName.Trim();

        private static HashSet<string> BuildAllowedNameSet(IReadOnlyList<string> allowedActionNames)
        {
            if (allowedActionNames == null || allowedActionNames.Count == 0)
                return null;

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < allowedActionNames.Count; i++)
            {
                string actionName = NormalizeActionName(allowedActionNames[i]);
                if (!string.IsNullOrEmpty(actionName))
                    allowed.Add(actionName);
            }

            return allowed;
        }
    }

    public enum ConvaiActionExecutionStatus
    {
        Succeeded = 0,
        Failed = 1,
        Canceled = 2,
        TimedOut = 3,
        Unhandled = 4
    }

    public readonly struct ConvaiActionExecutionResult
    {
        public ConvaiActionExecutionStatus Status { get; }
        public string Message { get; }
        public Exception Exception { get; }

        private ConvaiActionExecutionResult(
            ConvaiActionExecutionStatus status,
            string message = null,
            Exception exception = null)
        {
            Status = status;
            Message = message;
            Exception = exception;
        }

        public static ConvaiActionExecutionResult Succeeded() =>
            new(ConvaiActionExecutionStatus.Succeeded);

        public static ConvaiActionExecutionResult Failed(string message = null, Exception exception = null) =>
            new(ConvaiActionExecutionStatus.Failed, message, exception);

        public static ConvaiActionExecutionResult Canceled() =>
            new(ConvaiActionExecutionStatus.Canceled);

        public static ConvaiActionExecutionResult TimedOut() =>
            new(ConvaiActionExecutionStatus.TimedOut);

        public static ConvaiActionExecutionResult Unhandled(string message = null) =>
            new(ConvaiActionExecutionStatus.Unhandled, message);

        public override string ToString() => Exception != null
            ? $"{Status}: {Message ?? Exception.Message}"
            : string.IsNullOrEmpty(Message) ? Status.ToString() : $"{Status}: {Message}";
    }

    [Serializable]
    public sealed class ConvaiActionStepReport
    {
        public ConvaiActionInvocation Invocation { get; }
        public ConvaiActionExecutionResult Result { get; }
        public bool BatchAborted { get; }
        public string FailureMessage { get; }

        internal ConvaiActionStepReport(
            ConvaiActionInvocation invocation,
            ConvaiActionExecutionResult result,
            bool batchAborted,
            string failureMessage)
        {
            Invocation = invocation;
            Result = result;
            BatchAborted = batchAborted;
            FailureMessage = failureMessage ?? string.Empty;
        }
    }

    public sealed class ConvaiActionInvocation
    {
        public ConvaiActionCommand Command { get; }
        public ConvaiActionDefinition Definition { get; }
        public ConvaiResolvedActionTarget ResolvedTarget { get; }
        public ConvaiCharacter Character { get; }
        public int BatchIndex { get; }
        public int StepIndex { get; }

        internal ConvaiActionInvocation(
            ConvaiActionCommand command,
            ConvaiActionDefinition definition,
            ConvaiResolvedActionTarget resolvedTarget,
            ConvaiCharacter character,
            int batchIndex,
            int stepIndex)
        {
            Command = command;
            Definition = definition;
            ResolvedTarget = resolvedTarget;
            Character = character;
            BatchIndex = batchIndex;
            StepIndex = stepIndex;
        }

        public override string ToString() =>
            $"[{BatchIndex}:{StepIndex}] {Command} (def={Definition?.ActionName ?? "?"})";
    }

    public interface IConvaiActionExecutor
    {
        Task<ConvaiActionExecutionResult> ExecuteAsync(
            ConvaiActionInvocation invocation,
            CancellationToken cancellationToken);
    }

}
