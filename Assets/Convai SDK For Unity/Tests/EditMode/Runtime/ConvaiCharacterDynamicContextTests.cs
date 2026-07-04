using System;
using System.Collections.Generic;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Runtime.Components;
using Convai.Runtime.Actions;
using Convai.Runtime.Core.DependencyInjection;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Presentation.DynamicContext;
using Convai.Shared.Actions;
using Convai.Tests.EditMode.Mocks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using ILogger = Convai.Domain.Logging.ILogger;

namespace Convai.Tests.EditMode.Runtime
{
    [TestFixture]
    public class ConvaiCharacterDynamicContextTests
    {
        private readonly List<GameObject> _createdObjects = new();
        private MockRoomAudioService _audioService;
        private MockRoomConnectionService _connectionService;
        private EventHub _eventHub;
        private MockAgentRegistry _agentRegistry;
        private TestLogger _logger;

        [SetUp]
        public void SetUp()
        {
            _eventHub = new EventHub(new ImmediateScheduler());
            _connectionService = new MockRoomConnectionService();
            _audioService = new MockRoomAudioService();
            _agentRegistry = new MockAgentRegistry();
            _logger = new TestLogger();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _createdObjects)
                if (go != null)
                    Object.DestroyImmediate(go);

            _createdObjects.Clear();
        }

        [Test]
        public void SetState_NewStateWhileReady_AppendsTrackedState()
        {
            ConvaiCharacter character = CreateCharacter();
            MakeReady(character);

            character.DynamicContext.SetState("Health", "100");

            Assert.AreEqual(0, _connectionService.SentDynamicContextUpdates.Count);

            character.DynamicContext.Flush();

            Assert.AreEqual(1, _connectionService.SentDynamicContextUpdates.Count);
            AssertUpdate(
                _connectionService.SentDynamicContextUpdates[0],
                "Health is 100",
                ConvaiContextUpdateMode.Replace,
                ConvaiContextReactionMode.SyncOnly);
            AssertHasUpdateId(_connectionService.SentDynamicContextUpdates[0]);
        }

        [Test]
        public void SetState_ChangedStateWhileReady_CoalescesToSingleReplaceOnFlush()
        {
            ConvaiCharacter character = CreateCharacter();
            MakeReady(character);
            character.DynamicContext.SetState("Health", "100");
            character.DynamicContext.Flush();
            _connectionService.SentDynamicContextUpdates.Clear();

            character.DynamicContext.SetState("Health", "50", ConvaiContextReactionMode.ReactImmediately);

            Assert.AreEqual(0, _connectionService.SentDynamicContextUpdates.Count);

            character.DynamicContext.Flush();

            Assert.AreEqual(1, _connectionService.SentDynamicContextUpdates.Count);
            AssertUpdate(
                _connectionService.SentDynamicContextUpdates[0],
                "Health is 50\nHealth changed from 100 to 50",
                ConvaiContextUpdateMode.Replace,
                ConvaiContextReactionMode.ReactImmediately);
            AssertHasUpdateId(_connectionService.SentDynamicContextUpdates[0]);
        }

        [Test]
        public void SetStates_MixedBatchWhileReady_CoalescesToSingleReplaceOnFlush()
        {
            ConvaiCharacter character = CreateCharacter();
            MakeReady(character);
            character.DynamicContext.SetState("Health", "100");
            character.DynamicContext.Flush();
            _connectionService.SentDynamicContextUpdates.Clear();

            character.DynamicContext.SetStates(new Dictionary<string, string>
            {
                ["Health"] = "50",
                ["Ammo"] = "6"
            }, ConvaiContextReactionMode.ReactImmediately);

            Assert.AreEqual(0, _connectionService.SentDynamicContextUpdates.Count);

            character.DynamicContext.Flush();

            Assert.AreEqual(1, _connectionService.SentDynamicContextUpdates.Count);
            AssertUpdate(
                _connectionService.SentDynamicContextUpdates[0],
                "Health is 50\nHealth changed from 100 to 50\nAmmo is 6",
                ConvaiContextUpdateMode.Replace,
                ConvaiContextReactionMode.ReactImmediately);
            AssertHasUpdateId(_connectionService.SentDynamicContextUpdates[0]);
        }

        [Test]
        public void AddEvent_WhileReady_AppendsRawEvent()
        {
            ConvaiCharacter character = CreateCharacter();
            MakeReady(character);

            character.DynamicContext.AddEvent("Door opened");

            Assert.AreEqual(0, _connectionService.SentDynamicContextUpdates.Count);

            character.DynamicContext.Flush();

            Assert.AreEqual(1, _connectionService.SentDynamicContextUpdates.Count);
            AssertUpdate(
                _connectionService.SentDynamicContextUpdates[0],
                "Door opened",
                ConvaiContextUpdateMode.Replace,
                ConvaiContextReactionMode.Auto);
            AssertHasUpdateId(_connectionService.SentDynamicContextUpdates[0]);
        }

        [Test]
        public void BatchedUpdates_StateLastWins_EventDedupes_AttentionLastWins_AndReactionAggregates()
        {
            ConvaiCharacter character = CreateCharacter();
            MakeReady(character);

            character.DynamicContext.SetState("Health", "100", ConvaiContextReactionMode.SyncOnly);
            character.DynamicContext.SetState("Health", "80", ConvaiContextReactionMode.Auto);
            character.DynamicContext.AddEvent("Door opened", ConvaiContextReactionMode.Auto);
            character.DynamicContext.AddEvent("Door opened", ConvaiContextReactionMode.Auto);
            character.DynamicContext.SetCurrentAttentionObject("door", ConvaiContextReactionMode.SyncOnly);
            character.DynamicContext.SetCurrentAttentionObject("lever", ConvaiContextReactionMode.ReactImmediately);

            Assert.AreEqual(0, _connectionService.SentDynamicContextUpdates.Count);

            character.DynamicContext.Flush();

            Assert.AreEqual(1, _connectionService.SentDynamicContextUpdates.Count);
            ConvaiDynamicContextUpdate update = _connectionService.SentDynamicContextUpdates[0];
            AssertUpdate(
                update,
                "Door opened\nHealth is 80",
                ConvaiContextUpdateMode.Replace,
                ConvaiContextReactionMode.ReactImmediately);
            Assert.AreEqual("lever", update.CurrentAttentionObject);
            AssertHasUpdateId(update);
        }

        [Test]
        public void RemoveState_WhileReady_SendsCanonicalReplace()
        {
            ConvaiCharacter character = CreateCharacter();
            MakeReady(character);
            character.DynamicContext.SetState("Health", "100");
            character.DynamicContext.Flush();
            _connectionService.SentDynamicContextUpdates.Clear();

            character.DynamicContext.RemoveState("Health");

            Assert.AreEqual(0, _connectionService.SentDynamicContextUpdates.Count);

            character.DynamicContext.Flush();

            Assert.AreEqual(1, _connectionService.SentDynamicContextUpdates.Count);
            AssertUpdate(
                _connectionService.SentDynamicContextUpdates[0],
                string.Empty,
                ConvaiContextUpdateMode.Replace,
                ConvaiContextReactionMode.SyncOnly);
            AssertHasUpdateId(_connectionService.SentDynamicContextUpdates[0]);
        }

        [Test]
        public void Reset_WhileReady_SendsReset()
        {
            ConvaiCharacter character = CreateCharacter();
            MakeReady(character);
            character.DynamicContext.SetState("Health", "100");
            character.DynamicContext.Flush();
            _connectionService.SentDynamicContextUpdates.Clear();

            character.DynamicContext.Reset(removeStatic: true);

            Assert.AreEqual(0, _connectionService.SentDynamicContextUpdates.Count);

            character.DynamicContext.Flush();

            Assert.AreEqual(1, _connectionService.SentDynamicContextUpdates.Count);
            AssertUpdate(
                _connectionService.SentDynamicContextUpdates[0],
                null,
                ConvaiContextUpdateMode.Reset,
                ConvaiContextReactionMode.SyncOnly);
            Assert.IsTrue(_connectionService.SentDynamicContextUpdates[0].RemoveStatic);
            AssertHasUpdateId(_connectionService.SentDynamicContextUpdates[0]);
        }

        [Test]
        public void GeneratedUpdates_UseUniqueUpdateIds()
        {
            ConvaiCharacter character = CreateCharacter();
            MakeReady(character);

            character.DynamicContext.SetState("Health", "100");
            character.DynamicContext.Flush();
            string firstUpdateId = _connectionService.SentDynamicContextUpdates[0].UpdateId;

            character.DynamicContext.SetState("Health", "50");
            character.DynamicContext.Flush();
            string secondUpdateId = _connectionService.SentDynamicContextUpdates[1].UpdateId;

            AssertHasUpdateId(_connectionService.SentDynamicContextUpdates[0]);
            AssertHasUpdateId(_connectionService.SentDynamicContextUpdates[1]);
            Assert.AreNotEqual(firstUpdateId, secondUpdateId);
        }

        [Test]
        public void PreReadyTrackedUpdates_FlushOneCanonicalReplace_OnCharacterReady()
        {
            ConvaiCharacter character = CreateCharacter();

            character.DynamicContext.SetState("Health", "100");
            character.DynamicContext.AddEvent("Door opened");

            Assert.AreEqual(0, _connectionService.SentDynamicContextUpdates.Count);

            MakeReady(character);

            Assert.AreEqual(1, _connectionService.SentDynamicContextUpdates.Count);
            AssertUpdate(
                _connectionService.SentDynamicContextUpdates[0],
                "Door opened\nHealth is 100",
                ConvaiContextUpdateMode.Replace,
                ConvaiContextReactionMode.Auto);
            AssertHasUpdateId(_connectionService.SentDynamicContextUpdates[0]);
        }

        [Test]
        public void PreReadyReset_FlushesDeferredReset_OnCharacterReady()
        {
            ConvaiCharacter character = CreateCharacter();

            character.DynamicContext.SetState("Health", "100");
            character.DynamicContext.Reset();

            Assert.AreEqual(0, _connectionService.SentDynamicContextUpdates.Count);

            MakeReady(character);

            Assert.AreEqual(1, _connectionService.SentDynamicContextUpdates.Count);
            AssertUpdate(
                _connectionService.SentDynamicContextUpdates[0],
                null,
                ConvaiContextUpdateMode.Reset,
                ConvaiContextReactionMode.SyncOnly);
            AssertHasUpdateId(_connectionService.SentDynamicContextUpdates[0]);
        }

        [Test]
        public void DisconnectReconnect_ReflushesCanonicalTrackedContext()
        {
            ConvaiCharacter character = CreateCharacter();
            MakeReady(character);
            character.DynamicContext.SetState("Health", "100");
            _connectionService.SentDynamicContextUpdates.Clear();

            _connectionService.RaiseConnectionFailed();
            _connectionService.RaiseConnected();
            _eventHub.Publish(CharacterReady.Create(character.CharacterId, $"participant-{character.CharacterId}"));

            Assert.AreEqual(1, _connectionService.SentDynamicContextUpdates.Count);
            AssertUpdate(
                _connectionService.SentDynamicContextUpdates[0],
                "Health is 100",
                ConvaiContextUpdateMode.Replace,
                ConvaiContextReactionMode.SyncOnly);
            AssertHasUpdateId(_connectionService.SentDynamicContextUpdates[0]);
        }

        [Test]
        public void PendingSync_WithEmptyCanonicalContext_SendsEmptyReplace_OnReconnect()
        {
            ConvaiCharacter character = CreateCharacter();
            MakeReady(character);
            character.DynamicContext.SetState("Health", "100");
            _connectionService.SentDynamicContextUpdates.Clear();

            _connectionService.RaiseConnectionFailed();
            character.DynamicContext.RemoveState("Health");

            _connectionService.RaiseConnected();
            _eventHub.Publish(CharacterReady.Create(character.CharacterId, $"participant-{character.CharacterId}"));

            Assert.AreEqual(1, _connectionService.SentDynamicContextUpdates.Count);
            AssertUpdate(
                _connectionService.SentDynamicContextUpdates[0],
                string.Empty,
                ConvaiContextUpdateMode.Replace,
                ConvaiContextReactionMode.SyncOnly);
            AssertHasUpdateId(_connectionService.SentDynamicContextUpdates[0]);
        }

        [Test]
        public void RawApply_WhenNotInConversation_DoesNotSendOrQueue()
        {
            ConvaiCharacter character = CreateCharacter();

            character.DynamicContext.Apply(new ConvaiDynamicContextUpdate("raw update"));

            Assert.AreEqual(0, _connectionService.SentDynamicContextUpdates.Count);

            MakeReady(character);

            Assert.AreEqual(0, _connectionService.SentDynamicContextUpdates.Count);
        }

        [Test]
        public void RawApply_WhenReady_SendsTypedUpdate()
        {
            ConvaiCharacter character = CreateCharacter();
            MakeReady(character);

            character.DynamicContext.Apply(new ConvaiDynamicContextUpdate(
                "full raw context",
                ConvaiContextUpdateMode.Replace,
                ConvaiContextReactionMode.ReactImmediately));

            Assert.AreEqual(1, _connectionService.SentDynamicContextUpdates.Count);
            AssertUpdate(
                _connectionService.SentDynamicContextUpdates[0],
                "full raw context",
                ConvaiContextUpdateMode.Replace,
                ConvaiContextReactionMode.ReactImmediately);
        }

        [Test]
        public void ConvaiCharacter_ImplementsCharacterDynamicContextSurface()
        {
            ConvaiCharacter character = CreateCharacter();

            Assert.NotNull(character.DynamicContext);
            Assert.IsTrue(character is Convai.Runtime.Behaviors.IConvaiCharacterAgent);
            Assert.NotNull(((Convai.Runtime.Behaviors.IConvaiCharacterAgent)character).DynamicContext);
        }

        [Test]
        public void SetCurrentAttentionObject_UnknownAuthoredObject_DoesNotSendUpdate()
        {
            ConvaiCharacter character = CreateCharacter();
            ConvaiActionConfigSource source = character.gameObject.AddComponent<ConvaiActionConfigSource>();
            UnityEventActionExecutor executor = character.gameObject.AddComponent<UnityEventActionExecutor>();
            SetPrivateField(source, "_definitions", new List<ConvaiActionDefinition>
            {
                new() { ActionName = "Move To", Executor = executor }
            });
            SetPrivateField(source, "_objects", new List<ConvaiActionObjectDefinition>
            {
                new() { Name = "cube" }
            });
            MakeReady(character);

            character.DynamicContext.SetCurrentAttentionObject("lever");
            character.DynamicContext.Flush();

            Assert.AreEqual(0, _connectionService.SentDynamicContextUpdates.Count);
        }

        [Test]
        public void SetState_LongNewValue_OmitsToClauseInDeltaLine()
        {
            ConvaiCharacter character = CreateCharacter();
            MakeReady(character);
            character.DynamicContext.SetState("Notes", "short");
            character.DynamicContext.Flush();
            _connectionService.SentDynamicContextUpdates.Clear();

            character.DynamicContext.SetState(
                "Notes",
                "one two three four",
                ConvaiContextReactionMode.ReactImmediately);
            character.DynamicContext.Flush();

            Assert.AreEqual(1, _connectionService.SentDynamicContextUpdates.Count);
            AssertUpdate(
                _connectionService.SentDynamicContextUpdates[0],
                "Notes is one two three four\nNotes changed from short",
                ConvaiContextUpdateMode.Replace,
                ConvaiContextReactionMode.ReactImmediately);
        }

        [Test]
        public void Relay_SetStateWithImmediateFlush_SendsBatchedContext()
        {
            ConvaiCharacter character = CreateCharacter();
            MakeReady(character);
            ConvaiDynamicContextRelay relay = character.gameObject.AddComponent<ConvaiDynamicContextRelay>();
            SetPrivateField(relay, "_flushImmediately", true);
            SetPrivateField(relay, "_reactionMode", ConvaiContextReactionMode.Auto);

            relay.SetState("Health", "100");

            Assert.AreEqual(1, _connectionService.SentDynamicContextUpdates.Count);
            AssertUpdate(
                _connectionService.SentDynamicContextUpdates[0],
                "Health is 100",
                ConvaiContextUpdateMode.Replace,
                ConvaiContextReactionMode.Auto);
            AssertHasUpdateId(_connectionService.SentDynamicContextUpdates[0]);
        }

        private ConvaiCharacter CreateCharacter(string characterId = "test-char-id", string characterName = "TestCharacter")
        {
            var go = new GameObject(characterName);
            _createdObjects.Add(go);

            var character = go.AddComponent<ConvaiCharacter>();
            character.Configure(characterId, characterName);
            character.InjectDependencies(new ConvaiCharacterDependencies(
                _eventHub,
                _connectionService,
                _audioService,
                _agentRegistry,
                _logger));

            return character;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(field, $"Expected field '{fieldName}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private void MakeReady(ConvaiCharacter character)
        {
            _connectionService.RaiseConnected();
            _eventHub.Publish(CharacterReady.Create(character.CharacterId, $"participant-{character.CharacterId}"));
        }

        private static void AssertUpdate(
            ConvaiDynamicContextUpdate update,
            string expectedText,
            ConvaiContextUpdateMode expectedMode,
            ConvaiContextReactionMode expectedReaction)
        {
            Assert.AreEqual(expectedText, update.Text);
            Assert.AreEqual(expectedMode, update.Mode);
            Assert.AreEqual(expectedReaction, update.Reaction);
        }

        private static void AssertHasUpdateId(ConvaiDynamicContextUpdate update)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(update.UpdateId));
            StringAssert.StartsWith("unity-", update.UpdateId);
        }

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();
            public void ScheduleOnBackground(Action action) => action?.Invoke();
            public bool IsMainThread() => true;
        }

        private sealed class TestLogger : ILogger
        {
            public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK) { }

            public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Debug(string message, LogCategory category = LogCategory.SDK) { }

            public void Debug(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Info(string message, LogCategory category = LogCategory.SDK) { }

            public void Info(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Warning(string message, LogCategory category = LogCategory.SDK) { }

            public void Warning(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Error(string message, LogCategory category = LogCategory.SDK) { }

            public void Error(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public void Error(Exception exception, string message = null, LogCategory category = LogCategory.SDK) { }

            public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            {
            }

            public bool IsEnabled(LogLevel level, LogCategory category) => true;
        }
    }
}
