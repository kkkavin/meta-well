using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.Abstractions;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.Errors;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Models;
using Convai.Runtime.Actions;
using Convai.Runtime.Adapters.Networking;
using Convai.Runtime.Components;
using Convai.Runtime.Networking.Media;
using Convai.Runtime.Room;
using Convai.Shared.Actions;
using Convai.Shared.Types;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Convai.Tests.EditMode.Runtime
{
    [TestFixture]
    public class ActionSystemTests
    {
        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (gameObject != null && gameObject.name.StartsWith("ActionSystemTests_", StringComparison.Ordinal))
                    UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        // ── Resolution via dispatcher (ConvaiResolvedAction is internal) ─────────────────

        [Test]
        public async Task ResolvedAction_ResolvesObjectTarget_ByExactName()
        {
            var fixture = CreateDispatcherFixtureWithObjects(
                actionNames: new[] { "Move To" },
                objectNames: new[] { "cube" });

            ConvaiActionInvocation captured = null;
            fixture.Dispatcher.OnStepStarted.AddListener(inv => captured = inv);
            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await WaitUntilAsync(() => captured != null);

            Assert.That(captured.ResolvedTarget?.Kind, Is.EqualTo(ConvaiActionTargetKind.Object));
            Assert.That(captured.ResolvedTarget?.ObjectBinding?.Name, Is.EqualTo("cube"));
            Assert.That(captured.Command.Name, Is.EqualTo("Move To"));
        }

        [Test]
        public async Task ResolvedAction_HandlesMissingTarget()
        {
            var fixture = CreateDispatcherFixtureWithObjects(actionNames: new[] { "Dance" });

            ConvaiActionInvocation captured = null;
            fixture.Dispatcher.OnStepStarted.AddListener(inv => captured = inv);
            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Dance") });
            await WaitUntilAsync(() => captured != null);

            Assert.That(captured.ResolvedTarget, Is.Null);
            Assert.That(captured.Command.Name, Is.EqualTo("Dance"));
        }

        [Test]
        public async Task ResolvedAction_LeavesUnresolvedTarget_WhenUnknown()
        {
            var fixture = CreateDispatcherFixtureWithObjects(
                actionNames: new[] { "Move To" },
                objectNames: new[] { "cube" });

            ConvaiActionInvocation captured = null;
            fixture.Dispatcher.OnStepStarted.AddListener(inv => captured = inv);
            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "unknown") });
            await WaitUntilAsync(() => captured != null);

            Assert.That(captured.ResolvedTarget, Is.Null);
            Assert.That(captured.Command.Target, Is.EqualTo("unknown"));
        }

        [Test]
        public async Task ResolvedAction_ResolvesCharacterTarget_ByExactName()
        {
            var fixture = CreateDispatcherFixtureWithCharacters(
                actionNames: new[] { "Follow" },
                characterNames: new[] { "Player" });

            ConvaiActionInvocation captured = null;
            fixture.Dispatcher.OnStepStarted.AddListener(inv => captured = inv);
            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Follow", "Player") });
            await WaitUntilAsync(() => captured != null);

            Assert.That(captured.ResolvedTarget?.Kind, Is.EqualTo(ConvaiActionTargetKind.Character));
            Assert.That(captured.ResolvedTarget?.CharacterBinding?.Name, Is.EqualTo("Player"));
        }

        [Test]
        public async Task ResolvedAction_UsesRequiredCharacterKind_WhenObjectAndCharacterNamesCollide()
        {
            var fixture = CreateDispatcherFixtureWithMixedTargets(
                requirement: ConvaiActionTargetRequirement.Character,
                targetName: "SharedTarget",
                addObject: true,
                addCharacter: true);

            ConvaiActionInvocation captured = null;
            fixture.Dispatcher.OnStepStarted.AddListener(inv => captured = inv);
            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "SharedTarget") });
            await WaitUntilAsync(() => captured != null);

            Assert.That(captured.ResolvedTarget?.Kind, Is.EqualTo(ConvaiActionTargetKind.Character));
            Assert.That(captured.ResolvedTarget?.CharacterBinding?.Name, Is.EqualTo("SharedTarget"));
        }

        [Test]
        public async Task ResolvedAction_UsesRequiredObjectKind_WhenObjectAndCharacterNamesCollide()
        {
            var fixture = CreateDispatcherFixtureWithMixedTargets(
                requirement: ConvaiActionTargetRequirement.Object,
                targetName: "SharedTarget",
                addObject: true,
                addCharacter: true);

            ConvaiActionInvocation captured = null;
            fixture.Dispatcher.OnStepStarted.AddListener(inv => captured = inv);
            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "SharedTarget") });
            await WaitUntilAsync(() => captured != null);

            Assert.That(captured.ResolvedTarget?.Kind, Is.EqualTo(ConvaiActionTargetKind.Object));
            Assert.That(captured.ResolvedTarget?.ObjectBinding?.Name, Is.EqualTo("SharedTarget"));
        }

        // ── Queue / policy / cancellation ─────────────────────────────────────────────────

        [Test]
        public async Task Dispatcher_ExecutesBatchesSequentially_WithQueuePolicy()
        {
            var fixture = CreateDispatcherFixture(batchPolicy: ConvaiActionBatchPolicy.Queue);
            RecordingActionExecutor executor = fixture.Executor;
            executor.DelayMs = 15;

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube"), CreateAction("Pick Up", "cube") });
            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Drop", "cube") });
            await WaitUntilAsync(() => executor.ExecutedActions.Count == 3);

            CollectionAssert.AreEqual(
                new[] { "Move To cube", "Pick Up cube", "Drop cube" },
                executor.ExecutedActions);
        }

        [Test]
        public async Task Dispatcher_DropIncomingPolicy_IgnoresSecondBatchWhileBusy()
        {
            var fixture = CreateDispatcherFixture(batchPolicy: ConvaiActionBatchPolicy.DropIncoming);
            RecordingActionExecutor executor = fixture.Executor;
            executor.DelayMs = 25;

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Drop", "cube") });
            await WaitUntilAsync(() => executor.ExecutedActions.Count == 1);

            CollectionAssert.AreEqual(new[] { "Move To cube" }, executor.ExecutedActions);
        }

        [Test]
        public async Task Dispatcher_ReplaceCurrentPolicy_RunsReplacementBatchAfterCancellingActiveBatch()
        {
            var fixture = CreateDispatcherFixture(batchPolicy: ConvaiActionBatchPolicy.ReplaceCurrent);
            RecordingActionExecutor executor = fixture.Executor;
            executor.DelayMs = 100;

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await WaitUntilAsync(() => executor.ExecutedActions.Contains("Move To cube"), timeoutMs: 2000);
            await Task.Delay(20);
            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Drop", "cube") });
            await WaitUntilAsync(() => executor.ExecutedActions.Contains("Drop cube"), timeoutMs: 2000);

            Assert.That(executor.CancellationObserved, Is.True,
                "ReplaceCurrent should cancel the in-flight step before running the replacement batch.");
            CollectionAssert.AreEqual(new[] { "Move To cube", "Drop cube" }, executor.ExecutedActions);
        }

        [Test]
        public async Task Dispatcher_CancelsRunningBatch_WhenDisabled()
        {
            var fixture = CreateDispatcherFixture(batchPolicy: ConvaiActionBatchPolicy.Queue);
            RecordingActionExecutor executor = fixture.Executor;
            executor.DelayMs = 250;

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await Task.Delay(40);
            fixture.GameObject.SetActive(false);
            await Task.Delay(40);

            Assert.That(executor.CancellationObserved, Is.True);
        }

        [Test]
        public async Task Dispatcher_FailsStep_WhenNoDefinitionMatchesAction()
        {
            var fixture = CreateDispatcherFixture(batchPolicy: ConvaiActionBatchPolicy.Queue);
            ConvaiActionInvocation captured = null;
            fixture.Dispatcher.OnStepFailed.AddListener(inv => captured = inv);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Unknown Action", "cube") });
            await WaitUntilAsync(() => captured != null);

            Assert.That(captured.Command.Name, Is.EqualTo("Unknown Action"));
        }

        // ── Definition lookup ─────────────────────────────────────────────────────────────

        [Test]
        public async Task Dispatcher_FiresFailed_WhenDefinitionNotInSource()
        {
            var fixture = CreateDispatcherFixture(batchPolicy: ConvaiActionBatchPolicy.Queue);
            bool failedFired = false;
            fixture.Dispatcher.OnStepFailed.AddListener(_ => failedFired = true);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Nonexistent") });
            await WaitUntilAsync(() => failedFired);

            Assert.That(failedFired, Is.True);
        }

        [Test]
        public async Task Dispatcher_FiresFailed_WhenExecutorIsNull()
        {
            var fixture = CreateDispatcherFixtureWithNullExecutor(actionName: "Dance");
            bool failedFired = false;
            fixture.Dispatcher.OnStepFailed.AddListener(_ => failedFired = true);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Dance") });
            await WaitUntilAsync(() => failedFired);

            Assert.That(failedFired, Is.True);
        }

        [Test]
        public async Task Dispatcher_FiresFailed_WhenExecutorDoesNotImplementInterface()
        {
            var fixture = CreateDispatcherFixtureWithNonExecutorMonoBehaviour(actionName: "Dance");
            bool failedFired = false;
            fixture.Dispatcher.OnStepFailed.AddListener(_ => failedFired = true);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Dance") });
            await WaitUntilAsync(() => failedFired);

            Assert.That(failedFired, Is.True);
        }

        [Test]
        public async Task Dispatcher_LooksUpDefinition_CaseInsensitive()
        {
            var fixture = CreateDispatcherFixture(batchPolicy: ConvaiActionBatchPolicy.Queue);
            ConvaiActionInvocation captured = null;
            fixture.Dispatcher.OnStepSucceeded.AddListener(inv => captured = inv);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("MOVE TO", "cube") });
            await WaitUntilAsync(() => captured != null);

            Assert.That(captured.Definition?.ActionName, Is.EqualTo("Move To"));
        }

        // ── Target validation ─────────────────────────────────────────────────────────────

        [Test]
        public async Task Dispatcher_FiresFailed_WhenObjectRequiredButNoTarget()
        {
            var fixture = CreateDispatcherFixtureWithRequirement(
                ConvaiActionTargetRequirement.Object,
                includeObjectInConfig: false);
            bool failedFired = false;
            fixture.Dispatcher.OnStepFailed.AddListener(_ => failedFired = true);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To") });
            await WaitUntilAsync(() => failedFired);

            Assert.That(failedFired, Is.True);
        }

        [Test]
        public async Task Dispatcher_FiresFailed_WhenObjectRequiredButCharacterResolved()
        {
            var fixture = CreateDispatcherFixtureWithMixedTargets(
                requirement: ConvaiActionTargetRequirement.Object,
                targetName: "Player",
                addObject: false,
                addCharacter: true);
            bool failedFired = false;
            fixture.Dispatcher.OnStepFailed.AddListener(_ => failedFired = true);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "Player") });
            await WaitUntilAsync(() => failedFired);

            Assert.That(failedFired, Is.True);
        }

        [Test]
        public async Task Dispatcher_Succeeds_WhenNoneRequirementAndNoTarget()
        {
            var fixture = CreateDispatcherFixtureWithRequirement(
                ConvaiActionTargetRequirement.None,
                includeObjectInConfig: false);
            bool succeededFired = false;
            fixture.Dispatcher.OnStepSucceeded.AddListener(_ => succeededFired = true);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Dance") });
            await WaitUntilAsync(() => succeededFired);

            Assert.That(succeededFired, Is.True);
        }

        [Test]
        public async Task Dispatcher_Succeeds_WhenEitherRequirementAndObjectTarget()
        {
            var fixture = CreateDispatcherFixtureWithMixedTargets(
                requirement: ConvaiActionTargetRequirement.Either,
                targetName: "cube",
                addObject: true,
                addCharacter: false);
            bool succeededFired = false;
            fixture.Dispatcher.OnStepSucceeded.AddListener(_ => succeededFired = true);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await WaitUntilAsync(() => succeededFired);

            Assert.That(succeededFired, Is.True);
        }

        [Test]
        public async Task Dispatcher_Succeeds_WhenEitherRequirementAndCharacterTarget()
        {
            var fixture = CreateDispatcherFixtureWithMixedTargets(
                requirement: ConvaiActionTargetRequirement.Either,
                targetName: "Player",
                addObject: false,
                addCharacter: true);
            bool succeededFired = false;
            fixture.Dispatcher.OnStepSucceeded.AddListener(_ => succeededFired = true);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "Player") });
            await WaitUntilAsync(() => succeededFired);

            Assert.That(succeededFired, Is.True);
        }

        [Test]
        public async Task Dispatcher_EmitsStepCompletedReport_WhenTargetRequirementFails()
        {
            var fixture = CreateDispatcherFixtureWithMixedTargets(
                requirement: ConvaiActionTargetRequirement.Object,
                targetName: "Player",
                addObject: false,
                addCharacter: true);
            SetPrivateField(fixture.Dispatcher, "_failurePolicy", ConvaiActionBatchFailurePolicy.StopBatch);
            ConvaiActionStepReport completedReport = null;
            fixture.Dispatcher.OnStepCompleted.AddListener(report => completedReport = report);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "Player") });
            await WaitUntilAsync(() => completedReport != null);

            Assert.That(completedReport.Invocation.Command.Name, Is.EqualTo("Move To"));
            Assert.That(completedReport.Invocation.Command.Target, Is.EqualTo("Player"));
            Assert.That(completedReport.Invocation.ResolvedTarget.Kind, Is.EqualTo(ConvaiActionTargetKind.Character));
            Assert.That(completedReport.Result.Status, Is.EqualTo(ConvaiActionExecutionStatus.Failed));
            Assert.That(completedReport.BatchAborted, Is.True);
            StringAssert.Contains("Action 'Move To'", completedReport.FailureMessage);
            StringAssert.Contains("target 'Player'", completedReport.FailureMessage);
            StringAssert.Contains("required Object", completedReport.FailureMessage);
            StringAssert.Contains("resolved Character", completedReport.FailureMessage);
            StringAssert.Contains("batch will abort", completedReport.FailureMessage);
        }

        [Test]
        public async Task DebugProbe_RecordsFailureReason_WhenTargetRequirementFails()
        {
            var fixture = CreateDispatcherFixtureWithMixedTargets(
                requirement: ConvaiActionTargetRequirement.Object,
                targetName: "Player",
                addObject: false,
                addCharacter: true);
            SetPrivateField(fixture.Dispatcher, "_failurePolicy", ConvaiActionBatchFailurePolicy.StopBatch);
            ConvaiActionDebugProbe probe = fixture.GameObject.AddComponent<ConvaiActionDebugProbe>();
            SetPrivateField(probe, "_logToConsole", false);
            SetPrivateField(probe, "_character", fixture.GameObject.GetComponent<ConvaiCharacter>());
            SetPrivateField(probe, "_dispatcher", fixture.Dispatcher);
            InvokePrivateMethod(probe, "OnEnable");

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "Player") });
            await WaitUntilAsync(() => GetPrivateField<string>(probe, "_lastFailureReason")?.Length > 0);

            string lastFailureReason = GetPrivateField<string>(probe, "_lastFailureReason");
            StringAssert.Contains("Action 'Move To'", lastFailureReason);
            StringAssert.Contains("target 'Player'", lastFailureReason);
            StringAssert.Contains("required Object", lastFailureReason);
            StringAssert.Contains("resolved Character", lastFailureReason);
        }

        // ── Failure policy ────────────────────────────────────────────────────────────────

        [Test]
        public async Task Dispatcher_StopBatch_AbortsBatchOnStepFailed()
        {
            var fixture = CreateDispatcherFixtureWithFailure(
                ConvaiActionBatchPolicy.Queue,
                ConvaiActionBatchFailurePolicy.StopBatch);
            bool batchAborted = false;
            fixture.Dispatcher.OnBatchAborted.AddListener(() => batchAborted = true);

            fixture.Dispatcher.EnqueueActions(new[]
            {
                CreateAction("Move To", "cube"),
                CreateAction("Pick Up", "cube")
            });
            await WaitUntilAsync(() => batchAborted);

            Assert.That(batchAborted, Is.True);
            Assert.That(fixture.Executor.ExecutedActions.Count, Is.EqualTo(1),
                "Only first step should run before abort");
        }

        [Test]
        public async Task Dispatcher_ContinueBatch_ContinuesAfterStepFailed()
        {
            var fixture = CreateDispatcherFixtureWithFailure(
                ConvaiActionBatchPolicy.Queue,
                ConvaiActionBatchFailurePolicy.ContinueBatch);
            bool batchCompleted = false;
            fixture.Dispatcher.OnBatchCompleted.AddListener(() => batchCompleted = true);

            fixture.Dispatcher.EnqueueActions(new[]
            {
                CreateAction("Move To", "cube"),
                CreateAction("Pick Up", "cube")
            });
            await WaitUntilAsync(() => batchCompleted);

            Assert.That(batchCompleted, Is.True);
            Assert.That(fixture.Executor.ExecutedActions.Count, Is.EqualTo(2),
                "Both steps should run under ContinueBatch");
        }

        [Test]
        public async Task Dispatcher_StopBatch_AbortsOnUnhandledResult()
        {
            var fixture = CreateDispatcherFixture(
                batchPolicy: ConvaiActionBatchPolicy.Queue,
                failurePolicy: ConvaiActionBatchFailurePolicy.StopBatch);
            fixture.Executor.ResultToReturn = ConvaiActionExecutionResult.Unhandled("test unhandled");
            bool batchAborted = false;
            fixture.Dispatcher.OnBatchAborted.AddListener(() => batchAborted = true);

            fixture.Dispatcher.EnqueueActions(new[]
            {
                CreateAction("Move To", "cube"),
                CreateAction("Move To", "cube")
            });
            await WaitUntilAsync(() => batchAborted);

            Assert.That(batchAborted, Is.True,
                "Unhandled executor result under StopBatch should abort the batch");
        }

        // ── Timeout ───────────────────────────────────────────────────────────────────────

        [Test]
        public async Task Dispatcher_FiresTimedOut_WhenStepExceedsTimeout()
        {
            var fixture = CreateDispatcherFixtureWithTimeout(
                timeoutSeconds: 0.05f,
                executorDelayMs: 200);
            ConvaiActionInvocation failedInvocation = null;
            fixture.Dispatcher.OnStepFailed.AddListener(inv => failedInvocation = inv);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await WaitUntilAsync(() => failedInvocation != null, timeoutMs: 2000);

            Assert.That(failedInvocation, Is.Not.Null);
        }

        [Test]
        public async Task Dispatcher_TimedOut_TriggersBatchAbort_WithStopBatchPolicy()
        {
            var fixture = CreateDispatcherFixtureWithTimeout(
                timeoutSeconds: 0.05f,
                executorDelayMs: 200,
                failurePolicy: ConvaiActionBatchFailurePolicy.StopBatch);
            bool batchAborted = false;
            fixture.Dispatcher.OnBatchAborted.AddListener(() => batchAborted = true);

            fixture.Dispatcher.EnqueueActions(new[]
            {
                CreateAction("Move To", "cube"),
                CreateAction("Pick Up", "cube")
            });
            await WaitUntilAsync(() => batchAborted, timeoutMs: 2000);

            Assert.That(batchAborted, Is.True);
        }

        [Test]
        public async Task Dispatcher_BatchCancellation_NotMistakenForTimeout()
        {
            var fixture = CreateDispatcherFixtureWithTimeout(
                timeoutSeconds: 5f,
                executorDelayMs: 500);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await Task.Delay(40);
            fixture.GameObject.SetActive(false);
            await Task.Delay(40);

            Assert.That(fixture.Executor.CancellationObserved, Is.True,
                "Batch cancellation should be observed by the executor, not silently timed out");
        }

        // ── Result events ─────────────────────────────────────────────────────────────────

        [Test]
        public async Task Dispatcher_FiresOnStepSucceeded_OnSuccess()
        {
            var fixture = CreateDispatcherFixture(batchPolicy: ConvaiActionBatchPolicy.Queue);
            bool succeededFired = false;
            fixture.Dispatcher.OnStepSucceeded.AddListener(_ => succeededFired = true);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await WaitUntilAsync(() => succeededFired);

            Assert.That(succeededFired, Is.True);
        }

        [Test]
        public async Task Dispatcher_EmitsStepCompletedReport_OnSuccess()
        {
            var fixture = CreateDispatcherFixture(batchPolicy: ConvaiActionBatchPolicy.Queue);
            ConvaiActionStepReport completedReport = null;
            fixture.Dispatcher.OnStepCompleted.AddListener(report => completedReport = report);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await WaitUntilAsync(() => completedReport != null);

            Assert.That(completedReport.Result.Status, Is.EqualTo(ConvaiActionExecutionStatus.Succeeded));
            Assert.That(completedReport.BatchAborted, Is.False);
            Assert.That(completedReport.FailureMessage, Is.Empty);
        }

        [Test]
        public async Task Dispatcher_FiresOnStepFailed_OnFailure()
        {
            var fixture = CreateDispatcherFixtureWithFailure(
                ConvaiActionBatchPolicy.Queue,
                ConvaiActionBatchFailurePolicy.ContinueBatch);
            bool failedFired = false;
            fixture.Dispatcher.OnStepFailed.AddListener(_ => failedFired = true);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await WaitUntilAsync(() => failedFired);

            Assert.That(failedFired, Is.True);
        }

        [Test]
        public async Task Dispatcher_EmitsStepCompletedReport_OnFailure()
        {
            var fixture = CreateDispatcherFixtureWithFailure(
                ConvaiActionBatchPolicy.Queue,
                ConvaiActionBatchFailurePolicy.StopBatch);
            ConvaiActionStepReport completedReport = null;
            fixture.Dispatcher.OnStepCompleted.AddListener(report => completedReport = report);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await WaitUntilAsync(() => completedReport != null);

            Assert.That(completedReport.Result.Status, Is.EqualTo(ConvaiActionExecutionStatus.Failed));
            Assert.That(completedReport.BatchAborted, Is.True);
            StringAssert.Contains("test failure", completedReport.FailureMessage);
            StringAssert.Contains("batch will abort", completedReport.FailureMessage);
        }

        [Test]
        public async Task Dispatcher_EmitsStepCompletedReport_OnTimedOut()
        {
            var fixture = CreateDispatcherFixtureWithTimeout(
                timeoutSeconds: 0.05f,
                executorDelayMs: 200,
                failurePolicy: ConvaiActionBatchFailurePolicy.StopBatch);
            ConvaiActionStepReport completedReport = null;
            fixture.Dispatcher.OnStepCompleted.AddListener(report => completedReport = report);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await WaitUntilAsync(() => completedReport != null, timeoutMs: 2000);

            Assert.That(completedReport.Result.Status, Is.EqualTo(ConvaiActionExecutionStatus.TimedOut));
            Assert.That(completedReport.BatchAborted, Is.True);
            StringAssert.Contains("TimedOut", completedReport.FailureMessage);
        }

        [Test]
        public async Task Dispatcher_EmitsStepCompletedReport_OnUnhandled()
        {
            var fixture = CreateDispatcherFixture(batchPolicy: ConvaiActionBatchPolicy.Queue);
            fixture.Executor.ResultToReturn = ConvaiActionExecutionResult.Unhandled("test unhandled");
            ConvaiActionStepReport completedReport = null;
            fixture.Dispatcher.OnStepCompleted.AddListener(report => completedReport = report);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await WaitUntilAsync(() => completedReport != null);

            Assert.That(completedReport.Result.Status, Is.EqualTo(ConvaiActionExecutionStatus.Unhandled));
            Assert.That(completedReport.BatchAborted, Is.True);
            StringAssert.Contains("test unhandled", completedReport.FailureMessage);
        }

        [Test]
        public async Task Dispatcher_EmitsStepCompletedReport_OnCanceled()
        {
            var fixture = CreateDispatcherFixture(
                ConvaiActionBatchPolicy.Queue,
                ConvaiActionBatchFailurePolicy.StopBatch);
            fixture.Executor.DelayMs = 250;
            ConvaiActionStepReport completedReport = null;
            fixture.Dispatcher.OnStepCompleted.AddListener(report => completedReport = report);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await Task.Delay(40);
            fixture.GameObject.SetActive(false);
            await WaitUntilAsync(() => completedReport?.Result.Status == ConvaiActionExecutionStatus.Canceled);

            Assert.That(completedReport.BatchAborted, Is.True);
            StringAssert.Contains("Canceled", completedReport.FailureMessage);
        }

        [Test]
        public void ActionConfigSource_DeduplicatesDefinitions_BeforeSerializingActionConfig()
        {
            GameObject gameObject = CreateCharacterGameObject("char-dedupe", "Dedupe Test", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();

            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", Executor = executor },
                new() { ActionName = "move to", Executor = executor }
            });

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Duplicate action definition 'move to'|Duplicate action definition 'Move To'"));
            ConvaiActionConfig config = source.BuildActionConfig();

            Assert.That(config.Actions.Count, Is.EqualTo(1));
            Assert.That(config.Actions[0], Is.EqualTo("Move To"));
        }

        [Test]
        public void ActionConfigSource_NormalizesValidInitialAttentionObject()
        {
            GameObject gameObject = CreateCharacterGameObject("char-attention-valid", "Attention Valid Test", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();

            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", Executor = executor }
            });
            SetPrivateField(source, "_objects", new List<ConvaiActionObjectDefinition>
            {
                new() { Name = " cube " }
            });
            SetPrivateField(source, "_initialAttentionObject", "CUBE");

            ConvaiActionConfig config = source.BuildActionConfig();

            Assert.That(config, Is.Not.Null);
            Assert.That(config.CurrentAttentionObject, Is.EqualTo("cube"));
        }

        [Test]
        public void ActionConfigSource_OmitsInvalidInitialAttentionObject()
        {
            GameObject gameObject = CreateCharacterGameObject("char-attention-invalid", "Attention Invalid Test", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();

            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", Executor = executor }
            });
            SetPrivateField(source, "_objects", new List<ConvaiActionObjectDefinition>
            {
                new() { Name = "cube" }
            });
            SetPrivateField(source, "_initialAttentionObject", "lever");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Initial attention object 'lever'"));
            ConvaiActionConfig config = source.BuildActionConfig();

            Assert.That(config, Is.Not.Null);
            Assert.That(config.CurrentAttentionObject, Is.Null.Or.Empty);
        }

        [Test]
        public void ActionConfigSource_OmitsConfig_WhenOnlyTargetsAndAttentionAreAuthored()
        {
            GameObject gameObject = CreateCharacterGameObject("char-targets-only", "Targets Only Test", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();

            SetPrivateField(source, "_objects", new List<ConvaiActionObjectDefinition>
            {
                new() { Name = "cube" }
            });
            SetPrivateField(source, "_characters", new List<ConvaiActionCharacterDefinition>
            {
                new() { Name = "Player" }
            });
            SetPrivateField(source, "_initialAttentionObject", "cube");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("action definitions"));
            ConvaiActionConfig config = source.BuildActionConfig();

            Assert.That(config, Is.Null);
        }

        [Test]
        public void ActionConfigSource_OmitsInvalidExecutorDefinitions_BeforeSerializingActionConfig()
        {
            GameObject gameObject = CreateCharacterGameObject("char-invalid-action", "Invalid Action", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();
            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", Executor = executor },
                new() { ActionName = "Dance", Executor = null },
                new() { ActionName = "Look", Executor = gameObject.AddComponent<NonExecutorMonoBehaviour>() }
            });

            ConvaiActionConfig config = source.BuildActionConfig();

            CollectionAssert.AreEqual(new[] { "Move To" }, config.Actions);

            UnityEngine.Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void ActionConfigSource_KeepsValidDuplicate_WhenEarlierDuplicateIsNotExecutable()
        {
            GameObject gameObject = CreateCharacterGameObject("char-valid-duplicate", "Valid Duplicate", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();
            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", Executor = null },
                new() { ActionName = "move to", Executor = executor }
            });

            ConvaiActionConfig config = source.BuildActionConfig();
            IReadOnlyList<ConvaiActionDefinition> definitions = source.GetEffectiveDefinitions(requireExecutable: true);

            CollectionAssert.AreEqual(new[] { "move to" }, config.Actions);
            Assert.That(definitions.Count, Is.EqualTo(1));
            Assert.That(definitions[0].Executor, Is.SameAs(executor));

            UnityEngine.Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void ActionConfigValidator_ReportsAuthoringProblems()
        {
            GameObject gameObject = CreateCharacterGameObject("char-validator", "Validator", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();
            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", Executor = executor },
                new() { ActionName = "move to", Executor = executor },
                new() { ActionName = "   ", Executor = executor },
                new() { ActionName = "Dance", Executor = null }
            });
            SetPrivateField(source, "_objects", new List<ConvaiActionObjectDefinition>
            {
                new() { Name = "SharedTarget", Description = "", GameObjectReference = null }
            });
            SetPrivateField(source, "_characters", new List<ConvaiActionCharacterDefinition>
            {
                new() { Name = "SharedTarget", Bio = "", GameObjectReference = null }
            });
            SetPrivateField(source, "_initialAttentionObject", "missing_object");

            IReadOnlyList<ConvaiActionConfigDiagnostic> diagnostics =
                ConvaiActionConfigValidator.Validate(source);

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Message.Contains("Duplicate action definition")));
            Assert.That(diagnostics.Any(diagnostic => diagnostic.Message.Contains("blank action name")));
            Assert.That(diagnostics.Any(diagnostic => diagnostic.Message.Contains("missing a valid executor")));
            Assert.That(diagnostics.Any(diagnostic => diagnostic.Message.Contains("Duplicate target name")));
            Assert.That(diagnostics.Any(diagnostic => diagnostic.Message.Contains("missing object description")));
            Assert.That(diagnostics.Any(diagnostic => diagnostic.Message.Contains("missing character bio")));
            Assert.That(diagnostics.Any(diagnostic => diagnostic.Message.Contains("missing GameObject reference")));
            Assert.That(diagnostics.Any(diagnostic => diagnostic.Message.Contains("Initial attention object")));
            Assert.That(diagnostics.Any(diagnostic => diagnostic.Severity == ConvaiActionConfigDiagnosticSeverity.Error));

            UnityEngine.Object.DestroyImmediate(gameObject);
        }

        [Test]
        public async Task Dispatcher_FiresOnBatchAborted_WhenAborted()
        {
            var fixture = CreateDispatcherFixtureWithFailure(
                ConvaiActionBatchPolicy.Queue,
                ConvaiActionBatchFailurePolicy.StopBatch);
            bool abortedFired = false;
            fixture.Dispatcher.OnBatchAborted.AddListener(() => abortedFired = true);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await WaitUntilAsync(() => abortedFired);

            Assert.That(abortedFired, Is.True);
        }

        [Test]
        public async Task Dispatcher_FiresOnBatchCompleted_WhenNormal()
        {
            var fixture = CreateDispatcherFixture(batchPolicy: ConvaiActionBatchPolicy.Queue);
            bool completedFired = false;
            fixture.Dispatcher.OnBatchCompleted.AddListener(() => completedFired = true);

            fixture.Dispatcher.EnqueueActions(new[] { CreateAction("Move To", "cube") });
            await WaitUntilAsync(() => completedFired);

            Assert.That(completedFired, Is.True);
        }

        // ── Adapter / config source integration ──────────────────────────────────────────

        [Test]
        public async Task RoomConnectionRuntimeAdapter_UsesPerCallActionOverride_BeforeCharacterSource()
        {
            GameObject gameObject = CreateCharacterGameObject("char-override", "Override Test", out ConvaiCharacter character);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();
            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", Executor = executor }
            });
            SetPrivateField(source, "_objects", new List<ConvaiActionObjectDefinition>
            {
                new() { Name = "cube" }
            });

            var overrideConfig = new ConvaiActionConfig
            {
                Actions = new List<string> { "Wave" },
                Objects = new List<ConvaiActionObjectDefinition> { new() { Name = "lever" } },
                CurrentAttentionObject = "lever"
            };

            CapturingRoomController controller = new();
            RoomConnectionRuntimeAdapter adapter = CreateRuntimeAdapter(character, controller, new RoomSessionConnectOptions
            {
                TurnTaking = TurnTakingOptions.CreateHandsFreeDefault(),
                ActionConfigOverride = overrideConfig
            });

            RoomConnectionAttemptResult result = await adapter.ConnectAsync(CancellationToken.None);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(controller.LastJoinOptions?.ResolvedActionConfig?.Actions[0], Is.EqualTo("Wave"));
            Assert.That(controller.LastJoinOptions?.ResolvedActionConfig?.Objects[0].Name, Is.EqualTo("lever"));
            Assert.That(controller.LastJoinOptions?.ResolvedActionConfig?.CurrentAttentionObject, Is.EqualTo("lever"));
        }

        [Test]
        public async Task RoomConnectionRuntimeAdapter_UsesPerCallDefinitionOverride_ForRuntimeExecutionBindings()
        {
            GameObject gameObject = CreateCharacterGameObject("char-definition-override", "Definition Override Test", out ConvaiCharacter character);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor sourceExecutor = gameObject.AddComponent<RecordingActionExecutor>();
            RecordingActionExecutor overrideExecutor = gameObject.AddComponent<RecordingActionExecutor>();
            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", Executor = sourceExecutor }
            });

            var overrideConfig = new ConvaiActionConfig
            {
                Actions = new List<string> { "Wave" }
            };

            CapturingRoomController controller = new();
            RoomConnectionRuntimeAdapter adapter = CreateRuntimeAdapter(character, controller, new RoomSessionConnectOptions
            {
                TurnTaking = TurnTakingOptions.CreateHandsFreeDefault(),
                ActionConfigOverride = overrideConfig,
                ActionDefinitionsOverride = new List<ConvaiActionDefinition>
                {
                    new() { ActionName = "Wave", Executor = overrideExecutor }
                }
            });

            RoomConnectionAttemptResult result = await adapter.ConnectAsync(CancellationToken.None);

            Assert.That(result.Succeeded, Is.True);
            IReadOnlyList<ConvaiActionDefinition> runtimeDefinitions = GetRuntimeActionDefinitions(character);
            Assert.That(runtimeDefinitions.Count, Is.EqualTo(1));
            Assert.That(runtimeDefinitions[0].ActionName, Is.EqualTo("Wave"));
            Assert.That(runtimeDefinitions[0].Executor, Is.EqualTo(overrideExecutor));
        }

        [Test]
        public async Task RoomConnectionRuntimeAdapter_FallsBackToCharacterActionSource_WhenNoOverride()
        {
            GameObject gameObject = CreateCharacterGameObject("char-source", "Source Test", out ConvaiCharacter character);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();
            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", Executor = executor }
            });
            SetPrivateField(source, "_objects", new List<ConvaiActionObjectDefinition>
            {
                new() { Name = "cube" }
            });
            SetPrivateField(source, "_initialAttentionObject", "cube");

            CapturingRoomController controller = new();
            RoomConnectionRuntimeAdapter adapter = CreateRuntimeAdapter(character, controller, null);

            RoomConnectionAttemptResult result = await adapter.ConnectAsync(CancellationToken.None);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(controller.LastJoinOptions?.ResolvedActionConfig?.Actions[0], Is.EqualTo("Move To"));
            Assert.That(controller.LastJoinOptions?.ResolvedActionConfig?.Objects[0].Name, Is.EqualTo("cube"));
            Assert.That(controller.LastJoinOptions?.ResolvedActionConfig?.CurrentAttentionObject, Is.EqualTo("cube"));
        }

        [Test]
        public async Task RoomConnectionRuntimeAdapter_UsesCharacterSessionId_WhenResumeEnabled()
        {
            GameObject gameObject = CreateCharacterGameObject("char-session", "Session Test", out ConvaiCharacter character);
            SetPrivateField(character, "_enableSessionResume", true);
            character.SetCharacterSessionId("manual-character-session");

            CapturingRoomController controller = new();
            RoomConnectionRuntimeAdapter adapter = CreateRuntimeAdapter(character, controller, null);

            RoomConnectionAttemptResult result = await adapter.ConnectAsync(CancellationToken.None);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(controller.LastStoredSessionId, Is.EqualTo("manual-character-session"));
            Assert.That(controller.LastJoinOptions?.CharacterSessionId, Is.EqualTo("manual-character-session"));
            Assert.That(character.CharacterSessionId, Is.EqualTo("character-session-id"));
        }

        [Test]
        public async Task RoomConnectionRuntimeAdapter_DoesNotLoadStoredSession_WhenCharacterSessionIdBlank()
        {
            GameObject gameObject = CreateCharacterGameObject("char-blank-session", "Blank Session Test", out ConvaiCharacter character);
            SetPrivateField(character, "_enableSessionResume", true);
            character.ClearCharacterSessionId();

            CapturingRoomController controller = new();
            InMemorySessionPersistence persistence = new();
            persistence.SaveSession("char-blank-session", "hidden-stored-session");
            RoomConnectionRuntimeAdapter adapter = CreateRuntimeAdapter(character, controller, null, persistence);

            RoomConnectionAttemptResult result = await adapter.ConnectAsync(CancellationToken.None);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(controller.LastStoredSessionId, Is.Null);
            Assert.That(controller.LastJoinOptions?.CharacterSessionId, Is.Null);
            Assert.That(character.CharacterSessionId, Is.EqualTo("character-session-id"));
        }

        // ── Fixture helpers ───────────────────────────────────────────────────────────────

        private static ConvaiActionCommand CreateAction(string name, string target = null) => new(name, target);

        private static (GameObject GameObject, ConvaiCharacter Character, ConvaiActionDispatcher Dispatcher,
            RecordingActionExecutor Executor) CreateDispatcherFixture(
            ConvaiActionBatchPolicy batchPolicy,
            ConvaiActionBatchFailurePolicy failurePolicy = ConvaiActionBatchFailurePolicy.StopBatch)
        {
            GameObject gameObject = CreateCharacterGameObject("char-dispatcher", "Dispatcher Test", out ConvaiCharacter character);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();

            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", TargetRequirement = ConvaiActionTargetRequirement.None, Executor = executor },
                new() { ActionName = "Pick Up", TargetRequirement = ConvaiActionTargetRequirement.None, Executor = executor },
                new() { ActionName = "Drop", TargetRequirement = ConvaiActionTargetRequirement.None, Executor = executor }
            });
            SetPrivateField(source, "_objects", new List<ConvaiActionObjectDefinition>
            {
                new() { Name = "cube" }
            });

            ConvaiActionDispatcher dispatcher = gameObject.AddComponent<ConvaiActionDispatcher>();
            SetPrivateField(dispatcher, "_batchPolicy", batchPolicy);
            SetPrivateField(dispatcher, "_failurePolicy", failurePolicy);
            return (gameObject, character, dispatcher, executor);
        }

        private static (GameObject GameObject, ConvaiActionDispatcher Dispatcher)
            CreateDispatcherFixtureWithObjects(
            IReadOnlyList<string> actionNames,
            IReadOnlyList<string> objectNames = null)
        {
            GameObject gameObject = CreateCharacterGameObject("char-resolver", "Resolver Test", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();

            var definitions = new List<ConvaiActionDefinition>();
            foreach (string name in actionNames)
                definitions.Add(new ConvaiActionDefinition
                {
                    ActionName = name,
                    TargetRequirement = ConvaiActionTargetRequirement.None,
                    Executor = executor
                });

            var objects = new List<ConvaiActionObjectDefinition>();
            if (objectNames != null)
                foreach (string name in objectNames)
                    objects.Add(new ConvaiActionObjectDefinition { Name = name });

            SetPrivateField(source, "_definitions", definitions);
            SetPrivateField(source, "_objects", objects);

            ConvaiActionDispatcher dispatcher = gameObject.AddComponent<ConvaiActionDispatcher>();
            return (gameObject, dispatcher);
        }

        private static (GameObject GameObject, ConvaiActionDispatcher Dispatcher)
            CreateDispatcherFixtureWithCharacters(
            IReadOnlyList<string> actionNames,
            IReadOnlyList<string> characterNames)
        {
            GameObject gameObject = CreateCharacterGameObject("char-char-resolver", "CharResolver Test", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();

            var definitions = new List<ConvaiActionDefinition>();
            foreach (string name in actionNames)
                definitions.Add(new ConvaiActionDefinition
                {
                    ActionName = name,
                    TargetRequirement = ConvaiActionTargetRequirement.None,
                    Executor = executor
                });

            var characters = new List<ConvaiActionCharacterDefinition>();
            foreach (string name in characterNames)
                characters.Add(new ConvaiActionCharacterDefinition { Name = name });

            SetPrivateField(source, "_definitions", definitions);
            SetPrivateField(source, "_characters", characters);

            ConvaiActionDispatcher dispatcher = gameObject.AddComponent<ConvaiActionDispatcher>();
            return (gameObject, dispatcher);
        }

        private static (GameObject GameObject, ConvaiActionDispatcher Dispatcher)
            CreateDispatcherFixtureWithNullExecutor(string actionName)
        {
            GameObject gameObject = CreateCharacterGameObject("char-null-exec", "NullExec Test", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = actionName, Executor = null }
            });
            ConvaiActionDispatcher dispatcher = gameObject.AddComponent<ConvaiActionDispatcher>();
            return (gameObject, dispatcher);
        }

        private static (GameObject GameObject, ConvaiActionDispatcher Dispatcher)
            CreateDispatcherFixtureWithNonExecutorMonoBehaviour(string actionName)
        {
            GameObject gameObject = CreateCharacterGameObject("char-bad-exec", "BadExec Test", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            NonExecutorMonoBehaviour badExecutor = gameObject.AddComponent<NonExecutorMonoBehaviour>();
            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = actionName, Executor = badExecutor }
            });
            ConvaiActionDispatcher dispatcher = gameObject.AddComponent<ConvaiActionDispatcher>();
            return (gameObject, dispatcher);
        }

        private static (GameObject GameObject, ConvaiActionDispatcher Dispatcher)
            CreateDispatcherFixtureWithRequirement(
            ConvaiActionTargetRequirement requirement,
            bool includeObjectInConfig)
        {
            GameObject gameObject = CreateCharacterGameObject("char-req", "Requirement Test", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();

            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", TargetRequirement = requirement, Executor = executor },
                new() { ActionName = "Dance", TargetRequirement = requirement, Executor = executor }
            });

            if (includeObjectInConfig)
                SetPrivateField(source, "_objects", new List<ConvaiActionObjectDefinition>
                {
                    new() { Name = "cube" }
                });

            ConvaiActionDispatcher dispatcher = gameObject.AddComponent<ConvaiActionDispatcher>();
            SetPrivateField(dispatcher, "_failurePolicy", ConvaiActionBatchFailurePolicy.ContinueBatch);
            return (gameObject, dispatcher);
        }

        private static (GameObject GameObject, ConvaiActionDispatcher Dispatcher)
            CreateDispatcherFixtureWithMixedTargets(
            ConvaiActionTargetRequirement requirement,
            string targetName,
            bool addObject,
            bool addCharacter)
        {
            GameObject gameObject = CreateCharacterGameObject("char-mixed", "MixedTarget Test", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();

            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", TargetRequirement = requirement, Executor = executor }
            });

            if (addObject)
                SetPrivateField(source, "_objects", new List<ConvaiActionObjectDefinition>
                {
                    new() { Name = targetName }
                });

            if (addCharacter)
                SetPrivateField(source, "_characters", new List<ConvaiActionCharacterDefinition>
                {
                    new() { Name = targetName }
                });

            ConvaiActionDispatcher dispatcher = gameObject.AddComponent<ConvaiActionDispatcher>();
            SetPrivateField(dispatcher, "_failurePolicy", ConvaiActionBatchFailurePolicy.ContinueBatch);
            return (gameObject, dispatcher);
        }

        private static (GameObject GameObject, ConvaiActionDispatcher Dispatcher, RecordingActionExecutor Executor)
            CreateDispatcherFixtureWithFailure(
            ConvaiActionBatchPolicy batchPolicy,
            ConvaiActionBatchFailurePolicy failurePolicy)
        {
            GameObject gameObject = CreateCharacterGameObject("char-fail", "Failure Test", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();
            executor.ResultToReturn = ConvaiActionExecutionResult.Failed("test failure");

            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", TargetRequirement = ConvaiActionTargetRequirement.None, Executor = executor },
                new() { ActionName = "Pick Up", TargetRequirement = ConvaiActionTargetRequirement.None, Executor = executor }
            });

            ConvaiActionDispatcher dispatcher = gameObject.AddComponent<ConvaiActionDispatcher>();
            SetPrivateField(dispatcher, "_batchPolicy", batchPolicy);
            SetPrivateField(dispatcher, "_failurePolicy", failurePolicy);
            return (gameObject, dispatcher, executor);
        }

        private static (GameObject GameObject, ConvaiActionDispatcher Dispatcher, RecordingActionExecutor Executor)
            CreateDispatcherFixtureWithTimeout(
            float timeoutSeconds,
            int executorDelayMs,
            ConvaiActionBatchFailurePolicy failurePolicy = ConvaiActionBatchFailurePolicy.StopBatch)
        {
            GameObject gameObject = CreateCharacterGameObject("char-timeout", "Timeout Test", out _);
            ConvaiActionConfigSource source = gameObject.AddComponent<ConvaiActionConfigSource>();
            RecordingActionExecutor executor = gameObject.AddComponent<RecordingActionExecutor>();
            executor.DelayMs = executorDelayMs;

            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", TargetRequirement = ConvaiActionTargetRequirement.None, Executor = executor, TimeoutSeconds = timeoutSeconds },
                new() { ActionName = "Pick Up", TargetRequirement = ConvaiActionTargetRequirement.None, Executor = executor, TimeoutSeconds = timeoutSeconds }
            });

            ConvaiActionDispatcher dispatcher = gameObject.AddComponent<ConvaiActionDispatcher>();
            SetPrivateField(dispatcher, "_failurePolicy", failurePolicy);
            return (gameObject, dispatcher, executor);
        }

        private static RoomConnectionRuntimeAdapter CreateRuntimeAdapter(
            ConvaiCharacter character,
            CapturingRoomController controller,
            RoomSessionConnectOptions invocationOptions,
            ISessionPersistence sessionPersistence = null)
        {
            RoomDisconnectRuntimeAdapter disconnectAdapter = new(
                () => null,
                () => controller,
                (_, _) => { },
                (_, _) => { });

            return new RoomConnectionRuntimeAdapter(
                () => SessionState.Disconnected,
                () => false,
                () => true,
                () => 1000,
                () => true,
                () => character,
                () => ConnectionContext.Empty,
                _ => { },
                () => ReconnectPolicy.Default,
                _ => { },
                _ => { },
                () => controller,
                () => ConvaiConnectionType.Audio,
                () => "https://core.convai.com/connect",
                TurnTakingOptions.CreateHandsFreeDefault,
                UserVadSettings.CreateDefault,
                () => invocationOptions,
                (_, _) => { },
                _ => { },
                () => sessionPersistence ?? new InMemorySessionPersistence(),
                disconnectAdapter,
                (_, _) => { },
                (_, _, _, _) => { },
                _ => { });
        }

        private static GameObject CreateCharacterGameObject(string characterId, string characterName,
            out ConvaiCharacter character)
        {
            GameObject gameObject = new($"ActionSystemTests_{characterName}");
            character = gameObject.AddComponent<ConvaiCharacter>();
            SetPrivateField(character, "_characterId", characterId);
            SetPrivateField(character, "_characterName", characterName);
            return gameObject;
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 1000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!predicate())
            {
                if (DateTime.UtcNow >= deadline)
                    throw new AssertionException("Timed out waiting for condition.");

                await Task.Delay(10);
            }
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(instance.GetType().FullName, fieldName);

            field.SetValue(instance, value);
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(instance.GetType().FullName, fieldName);

            return (T)field.GetValue(instance);
        }

        private static void InvokePrivateMethod(object instance, string methodName)
        {
            MethodInfo method = instance.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
                throw new MissingMethodException(instance.GetType().FullName, methodName);

            method.Invoke(instance, null);
        }

        private static IReadOnlyList<ConvaiActionDefinition> GetRuntimeActionDefinitions(ConvaiCharacter character)
        {
            MethodInfo method = typeof(ConvaiCharacter).GetMethod(
                "GetRuntimeActionDefinitions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "GetRuntimeActionDefinitions should exist.");
            return method.Invoke(character, null) as IReadOnlyList<ConvaiActionDefinition>;
        }

        // ── Inner test types ──────────────────────────────────────────────────────────────

        private sealed class RecordingActionExecutor : MonoBehaviour, IConvaiActionExecutor
        {
            public readonly List<string> ExecutedActions = new();
            public int DelayMs { get; set; }
            public bool CancellationObserved { get; private set; }
            public ConvaiActionExecutionResult ResultToReturn { get; set; } = ConvaiActionExecutionResult.Succeeded();

            public async Task<ConvaiActionExecutionResult> ExecuteAsync(
                ConvaiActionInvocation invocation,
                CancellationToken cancellationToken)
            {
                ExecutedActions.Add(invocation.Command.ToString());

                try
                {
                    if (DelayMs > 0)
                        await Task.Delay(DelayMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved = true;
                    throw;
                }

                return ResultToReturn;
            }
        }

        private sealed class NonExecutorMonoBehaviour : MonoBehaviour
        {
        }

        private sealed class InMemorySessionPersistence : ISessionPersistence
        {
            private readonly Dictionary<string, string> _sessions = new();

            public string LoadSession(string characterId) =>
                _sessions.TryGetValue(characterId ?? string.Empty, out string sessionId) ? sessionId : null;

            public void SaveSession(string characterId, string sessionId) =>
                _sessions[characterId ?? string.Empty] = sessionId;

            public void ClearSession(string characterId) => _sessions.Remove(characterId ?? string.Empty);
            public void ClearAllSessions() => _sessions.Clear();
            public bool HasSession(string characterId) => _sessions.ContainsKey(characterId ?? string.Empty);
        }

        private sealed class CapturingRoomController : IConvaiRoomController
        {
            public RoomJoinOptions LastJoinOptions { get; private set; }
            public string LastStoredSessionId { get; private set; }
            public bool HasRoomDetails => true;
            public bool IsConnectedToRoom => true;
            public bool IsMicMuted => false;
            public string SessionID => "session-id";
            public string CharacterSessionID => "character-session-id";
            public string RoomName => "room-name";
            public string RoomURL => "wss://room-url";
            public string Token => "token";
            public string ResolvedSpeakerId => string.Empty;
            public string RequestTraceId => string.Empty;
            public string ResolvedEndUserId => string.Empty;
            public IReadOnlyDictionary<string, object> ResolvedEndUserMetadata => null;
            public RTVIHandler RTVIHandler => null;
            public IRoomFacade CurrentRoom => null;

            public event Action OnRoomConnectionSuccessful
            {
                add { }
                remove { }
            }

            public event Action OnRoomConnectionFailed
            {
                add { }
                remove { }
            }

            public event Action<bool> OnMicMuteChanged
            {
                add { }
                remove { }
            }

            public event Action OnRoomReconnecting
            {
                add { }
                remove { }
            }

            public event Action OnRoomReconnected
            {
                add { }
                remove { }
            }

            public event Action OnUnexpectedRoomDisconnected
            {
                add { }
                remove { }
            }

            public event Action<IRemoteAudioTrack, string, string> OnRemoteAudioTrackSubscribed
            {
                add { }
                remove { }
            }

            public event Action<string, string> OnRemoteAudioTrackUnsubscribed
            {
                add { }
                remove { }
            }

            public Task<RoomConnectionAttemptResult> InitializeAsync(
                string connectionType,
                string coreServerUrl,
                string characterId,
                string storedSessionId,
                bool enableSessionResume,
                string dynamicInfoText,
                bool keepDynamicInfoInContext) =>
                InitializeAsync(
                    connectionType,
                    coreServerUrl,
                    characterId,
                    storedSessionId,
                    enableSessionResume,
                    dynamicInfoText,
                    keepDynamicInfoInContext,
                    null,
                    CancellationToken.None);

            public Task<RoomConnectionAttemptResult> InitializeAsync(
                string connectionType,
                string coreServerUrl,
                string characterId,
                string storedSessionId,
                bool enableSessionResume,
                string dynamicInfoText,
                bool keepDynamicInfoInContext,
                RoomJoinOptions joinOptions,
                CancellationToken cancellationToken = default)
            {
                LastStoredSessionId = storedSessionId;
                LastJoinOptions = joinOptions;
                return Task.FromResult(RoomConnectionAttemptResult.Success());
            }

            public void DisconnectFromRoom()
            {
            }

            public Task DisconnectFromRoomAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void SetMicMuted(bool mute) { }
            public void ToggleMicMute() { }
            public bool SetCharacterAudioMuted(string characterId, bool mute) => true;
            public bool MuteCharacter(string characterId) => true;
            public bool UnmuteCharacter(string characterId) => true;
            public bool IsCharacterAudioMuted(string characterId) => false;
            public void SetAudioSubscriptionPolicy(Func<string, bool> policy) { }
            public void ApplyRemoteAudioPreference(string characterId, bool enabled) { }
            public void Dispose() { }
        }
    }
}
