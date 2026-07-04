using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Runtime;
using Convai.Runtime.Components;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using UnityEngine;
using UnityEngine.Events;

namespace Convai.Runtime.Actions
{
    public enum ConvaiActionBatchPolicy
    {
        Queue = 0,
        ReplaceCurrent = 1,
        DropIncoming = 2
    }

    public enum ConvaiActionBatchFailurePolicy
    {
        StopBatch = 0,
        ContinueBatch = 1
    }

    [Serializable]
    public sealed class ConvaiActionInvocationUnityEvent : UnityEvent<ConvaiActionInvocation>
    {
    }

    [Serializable]
    public sealed class ConvaiActionStepReportUnityEvent : UnityEvent<ConvaiActionStepReport>
    {
    }

    [AddComponentMenu("Convai/Convai Action Dispatcher")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [RequireComponent(typeof(ConvaiCharacter))]
    public sealed class ConvaiActionDispatcher : MonoBehaviour
    {
        [Header("Dispatch")]
        [SerializeField]
        [Tooltip("How new backend action batches behave while another batch is still executing.")]
        private ConvaiActionBatchPolicy _batchPolicy = ConvaiActionBatchPolicy.Queue;

        [SerializeField]
        [Tooltip("Whether a step failure aborts the remaining batch or allows it to continue.")]
        private ConvaiActionBatchFailurePolicy _failurePolicy = ConvaiActionBatchFailurePolicy.StopBatch;

        [Header("Events")]
        [SerializeField] private UnityEvent _onBatchStarted = new();
        [SerializeField] private ConvaiActionInvocationUnityEvent _onStepStarted = new();
        [SerializeField] private ConvaiActionInvocationUnityEvent _onStepSucceeded = new();
        [SerializeField] private ConvaiActionInvocationUnityEvent _onStepFailed = new();
        [SerializeField] private ConvaiActionInvocationUnityEvent _onStepUnhandled = new();
        [SerializeField] private ConvaiActionStepReportUnityEvent _onStepCompleted = new();
        [SerializeField] private UnityEvent _onBatchCompleted = new();
        [SerializeField] private UnityEvent _onBatchAborted = new();

        private readonly object _queueLock = new();
        private readonly Queue<IReadOnlyList<ConvaiActionCommand>> _pendingBatches = new();
        private CancellationTokenSource _processingCts;
        private ConvaiCharacter _character;
        private bool _isProcessing;
        private int _batchCounter;
        private int _mainThreadId;

        public ConvaiActionBatchPolicy BatchPolicy => _batchPolicy;
        public ConvaiActionBatchFailurePolicy FailurePolicy => _failurePolicy;
        public UnityEvent OnBatchStarted => _onBatchStarted;
        public ConvaiActionInvocationUnityEvent OnStepStarted => _onStepStarted;
        public ConvaiActionInvocationUnityEvent OnStepSucceeded => _onStepSucceeded;
        public ConvaiActionInvocationUnityEvent OnStepFailed => _onStepFailed;
        public ConvaiActionInvocationUnityEvent OnStepUnhandled => _onStepUnhandled;
        public ConvaiActionStepReportUnityEvent OnStepCompleted => _onStepCompleted;
        public UnityEvent OnBatchCompleted => _onBatchCompleted;
        public UnityEvent OnBatchAborted => _onBatchAborted;

        public void EnqueueActions(IReadOnlyList<ConvaiActionCommand> actions) => HandleActionsReceived(actions);

        private void Awake()
        {
            _character = GetComponent<ConvaiCharacter>();
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private void OnEnable()
        {
            if (_character == null)
            {
                enabled = false;
                return;
            }

            _character.OnActionsReceived += HandleActionsReceived;
        }

        private void OnDisable()
        {
            if (_character != null)
                _character.OnActionsReceived -= HandleActionsReceived;

            CancelAllWork();
        }

        private void OnDestroy() => CancelAllWork();

        private void HandleActionsReceived(IReadOnlyList<ConvaiActionCommand> actions)
        {
            if (actions == null || actions.Count == 0)
                return;

            IReadOnlyList<ConvaiActionCommand> batch = CloneBatch(actions);
            if (UnityEngine.Application.isPlaying && !IsOnDispatcherThread())
            {
                UnityScheduler.Post(() => EnqueueBatchOnDispatcherThread(batch));
                return;
            }

            EnqueueBatchOnDispatcherThread(batch);
        }

        private void EnqueueBatchOnDispatcherThread(IReadOnlyList<ConvaiActionCommand> batch)
        {
            if (!EnsureCharacter())
                return;

            if (!isActiveAndEnabled)
                return;

            bool shouldStartProcessing = false;

            lock (_queueLock)
            {
                switch (_batchPolicy)
                {
                    case ConvaiActionBatchPolicy.DropIncoming when _isProcessing || _pendingBatches.Count > 0:
                        return;
                    case ConvaiActionBatchPolicy.ReplaceCurrent:
                        CancelAllWorkLocked();
                        break;
                }

                _pendingBatches.Enqueue(batch);
                if (!_isProcessing)
                {
                    _isProcessing = true;
                    shouldStartProcessing = true;
                }
            }

            if (shouldStartProcessing)
                _ = ProcessQueueAsync();
        }

        private async Task ProcessQueueAsync()
        {
            while (true)
            {
                CancellationTokenSource processingCts;
                bool shouldContinueProcessing = false;

                lock (_queueLock)
                {
                    if (_pendingBatches.Count == 0)
                    {
                        _isProcessing = false;
                        return;
                    }

                    _processingCts = new CancellationTokenSource();
                    processingCts = _processingCts;
                }

                try
                {
                    while (true)
                    {
                        IReadOnlyList<ConvaiActionCommand> batch;
                        int batchIndex;

                        lock (_queueLock)
                        {
                            if (_pendingBatches.Count == 0)
                                break;

                            batch = _pendingBatches.Dequeue();
                            batchIndex = _batchCounter++;
                        }

                        await ExecuteBatchAsync(batch, processingCts.Token, batchIndex);
                        if (processingCts.IsCancellationRequested)
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, this);
                }
                finally
                {
                    lock (_queueLock)
                    {
                        if (ReferenceEquals(_processingCts, processingCts))
                            _processingCts = null;

                        shouldContinueProcessing = _pendingBatches.Count > 0 && isActiveAndEnabled;
                        if (!shouldContinueProcessing)
                            _isProcessing = false;
                    }

                    processingCts.Dispose();
                }

                if (!shouldContinueProcessing)
                    return;
            }
        }

        private async Task ExecuteBatchAsync(
            IReadOnlyList<ConvaiActionCommand> actions,
            CancellationToken batchCt,
            int batchIndex)
        {
            _onBatchStarted?.Invoke();

            ConvaiActionConfig actionConfig = _character.GetRuntimeActionConfig();
            IReadOnlyList<ConvaiActionDefinition> definitions = _character.GetRuntimeActionDefinitions();
            Dictionary<string, ConvaiActionDefinition> lookup = ConvaiActionDefinition.BuildLookup(definitions);

            bool batchAborted = false;

            for (int stepIndex = 0; stepIndex < actions.Count; stepIndex++)
            {
                batchCt.ThrowIfCancellationRequested();

                ConvaiActionCommand command = actions[stepIndex];
                lookup.TryGetValue(command.Name ?? string.Empty, out ConvaiActionDefinition definition);

                ConvaiResolvedActionTarget resolvedTarget =
                    ConvaiResolvedActionTarget.Resolve(command?.Target, actionConfig, definition?.TargetRequirement);

                var invocation = new ConvaiActionInvocation(
                    command, definition, resolvedTarget, _character, batchIndex, stepIndex);

                _onStepStarted?.Invoke(invocation);

                ConvaiActionExecutionResult result;

                if (definition == null)
                {
                    bool willAbort = _failurePolicy == ConvaiActionBatchFailurePolicy.StopBatch;
                    result = ConvaiActionExecutionResult.Failed(
                        BuildMissingDefinitionFailureMessage(command, willAbort));
                    _onStepFailed?.Invoke(invocation);
                }
                else if (definition.Executor is not IConvaiActionExecutor executor)
                {
                    bool willAbort = _failurePolicy == ConvaiActionBatchFailurePolicy.StopBatch;
                    result = ConvaiActionExecutionResult.Failed(
                        BuildMissingExecutorFailureMessage(definition, willAbort));
                    _onStepFailed?.Invoke(invocation);
                }
                else if (!ValidateTargetRequirement(definition.TargetRequirement, resolvedTarget))
                {
                    bool willAbort = _failurePolicy == ConvaiActionBatchFailurePolicy.StopBatch;
                    result = ConvaiActionExecutionResult.Failed(
                        BuildTargetRequirementFailureMessage(command, definition, resolvedTarget, willAbort));
                    _onStepFailed?.Invoke(invocation);
                }
                else
                {
                    result = await ExecuteStepAsync(executor, invocation, definition, batchCt);

                    switch (result.Status)
                    {
                        case ConvaiActionExecutionStatus.Succeeded:
                            _onStepSucceeded?.Invoke(invocation);
                            break;
                        case ConvaiActionExecutionStatus.Unhandled:
                            _onStepUnhandled?.Invoke(invocation);
                            break;
                        default:
                            _onStepFailed?.Invoke(invocation);
                            break;
                        }
                }

                bool stepWillAbort = result.Status != ConvaiActionExecutionStatus.Succeeded &&
                                     _failurePolicy == ConvaiActionBatchFailurePolicy.StopBatch;
                string failureMessage = result.Status == ConvaiActionExecutionStatus.Succeeded
                    ? string.Empty
                    : BuildFailureMessage(result, stepWillAbort);
                _onStepCompleted?.Invoke(new ConvaiActionStepReport(invocation, result, stepWillAbort, failureMessage));

                if (stepWillAbort)
                {
                    batchAborted = true;
                    break;
                }
            }

            if (batchAborted)
                _onBatchAborted?.Invoke();
            else
                _onBatchCompleted?.Invoke();
        }

        private static async Task<ConvaiActionExecutionResult> ExecuteStepAsync(
            IConvaiActionExecutor executor,
            ConvaiActionInvocation invocation,
            ConvaiActionDefinition definition,
            CancellationToken batchCt)
        {
            bool hasTimeout = definition.TimeoutSeconds > 0f;
            CancellationTokenSource stepCts = null;

            try
            {
                CancellationToken stepCt = batchCt;

                if (hasTimeout)
                {
                    stepCts = CancellationTokenSource.CreateLinkedTokenSource(batchCt);
                    stepCts.CancelAfter(TimeSpan.FromSeconds(definition.TimeoutSeconds));
                    stepCt = stepCts.Token;
                }

                return await executor.ExecuteAsync(invocation, stepCt);
            }
            catch (OperationCanceledException) when (!batchCt.IsCancellationRequested)
            {
                return ConvaiActionExecutionResult.TimedOut();
            }
            catch (OperationCanceledException)
            {
                return ConvaiActionExecutionResult.Canceled();
            }
            catch (Exception ex)
            {
                return ConvaiActionExecutionResult.Failed(ex.Message, ex);
            }
            finally
            {
                stepCts?.Dispose();
            }
        }

        private static bool ValidateTargetRequirement(
            ConvaiActionTargetRequirement requirement,
            ConvaiResolvedActionTarget target)
        {
            return requirement switch
            {
                ConvaiActionTargetRequirement.None => true,
                ConvaiActionTargetRequirement.Object => target?.Kind == ConvaiActionTargetKind.Object,
                ConvaiActionTargetRequirement.Character => target?.Kind == ConvaiActionTargetKind.Character,
                ConvaiActionTargetRequirement.Either =>
                    target?.Kind == ConvaiActionTargetKind.Object ||
                    target?.Kind == ConvaiActionTargetKind.Character,
                _ => false
            };
        }

        private static string BuildMissingDefinitionFailureMessage(
            ConvaiActionCommand command,
            bool batchWillAbort)
        {
            string actionName = command?.Name ?? string.Empty;
            return AppendBatchAbortSuffix(
                $"No local action definition found for action '{actionName}'.",
                batchWillAbort);
        }

        private static string BuildMissingExecutorFailureMessage(
            ConvaiActionDefinition definition,
            bool batchWillAbort)
        {
            return AppendBatchAbortSuffix(
                $"Action '{definition.ActionName}' is missing a valid executor.",
                batchWillAbort);
        }

        private static string BuildTargetRequirementFailureMessage(
            ConvaiActionCommand command,
            ConvaiActionDefinition definition,
            ConvaiResolvedActionTarget resolvedTarget,
            bool batchWillAbort)
        {
            string actionName = definition?.ActionName ?? command?.Name ?? string.Empty;
            string targetName = command?.Target ?? string.Empty;
            string resolvedKind = resolvedTarget?.Kind.ToString() ?? "None";
            return AppendBatchAbortSuffix(
                $"Action '{actionName}' target '{targetName}' required {definition.TargetRequirement} but resolved {resolvedKind}.",
                batchWillAbort);
        }

        private static string BuildFailureMessage(
            ConvaiActionExecutionResult result,
            bool batchWillAbort)
        {
            string message = result.Message;
            if (string.IsNullOrWhiteSpace(message) && result.Exception != null)
                message = result.Exception.Message;

            if (string.IsNullOrWhiteSpace(message))
                message = result.Status.ToString();

            if (message.Contains("Remaining batch", StringComparison.OrdinalIgnoreCase))
                return message;

            return AppendBatchAbortSuffix(message, batchWillAbort);
        }

        private static string AppendBatchAbortSuffix(string message, bool batchWillAbort) =>
            batchWillAbort ? $"{message} Remaining batch will abort." : $"{message} Remaining batch will continue.";

        private void CancelAllWork()
        {
            lock (_queueLock)
                CancelAllWorkLocked();
        }

        private void CancelAllWorkLocked()
        {
            _processingCts?.Cancel();
            _pendingBatches.Clear();
        }

        private bool EnsureCharacter()
        {
            if (_character == null)
                _character = GetComponent<ConvaiCharacter>();

            return _character != null;
        }

        private bool IsOnDispatcherThread() => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        private static IReadOnlyList<ConvaiActionCommand> CloneBatch(IReadOnlyList<ConvaiActionCommand> actions)
        {
            var clone = new ConvaiActionCommand[actions.Count];
            for (int i = 0; i < actions.Count; i++)
                clone[i] = actions[i]?.Clone() ?? new ConvaiActionCommand();
            return clone;
        }
    }
}
