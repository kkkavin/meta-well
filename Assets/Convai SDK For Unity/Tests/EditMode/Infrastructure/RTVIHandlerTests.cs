using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Convai.Domain.DomainEvents.LipSync;
using Convai.Domain.DomainEvents.Runtime;
using Convai.Domain.DomainEvents.Session;
using Convai.Domain.DomainEvents.Transcript;
using Convai.Domain.EventSystem;
using Convai.Domain.Logging;
using Convai.Domain.Models;
using Convai.Infrastructure.Networking;
using Convai.Infrastructure.Networking.Transport;
using Convai.Infrastructure.Protocol;
using Convai.Infrastructure.Protocol.Messages;
using Convai.Runtime.Behaviors;
using Convai.Runtime.DynamicContext;
using Convai.Tests.EditMode.Mocks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Convai.Tests.EditMode.Infrastructure
{
    public class RTVIHandlerTests
    {
        [Test]
        public void SendData_Publishes_OutboundRtviMessageSent()
        {
            EventHub eventHub = CreateEventHub();
            RTVIHandler handler = CreateHandler(eventHub, out _, out _);
            OutboundRtviMessageSent captured = default;
            eventHub.Subscribe<OutboundRtviMessageSent>(e => captured = e);

            handler.SendData(new RTVIResetIdleTimer());

            Assert.AreEqual("reset-idle-timer", captured.MessageType);
            Assert.IsFalse(string.IsNullOrWhiteSpace(captured.MessageId));
        }

        [Test]
        public void ServerMessage_UserIdleWarning_Publishes_Event()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out _);
            UserIdleWarningReceived captured = default;
            eventHub.Subscribe<UserIdleWarningReceived>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "user-idle-warning",
                new JObject
                {
                    ["remaining_seconds"] = 300,
                    ["message"] = "Idle warning"
                }));

            Assert.AreEqual(300, captured.RemainingSeconds);
            Assert.AreEqual("Idle warning", captured.Message);
        }

        [Test]
        public void ServerMessage_ServerResponseContextUpdate_Publishes_DynamicContextResult()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out _);
            DynamicContextUpdateResultReceived captured = default;
            eventHub.Subscribe<DynamicContextUpdateResultReceived>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "server-response",
                new JObject
                {
                    ["event_type"] = "context-update",
                    ["status"] = "success",
                    ["message"] = "Context updated",
                    ["extras"] = new JObject
                    {
                        ["update_id"] = "ctx-1",
                        ["context_revision"] = 7,
                        ["token_count"] = 120,
                        ["static_token_count"] = 30,
                        ["runtime_token_count"] = 90,
                        ["remaining_tokens"] = 29880,
                        ["requested_run_llm"] = "true",
                        ["actual_run_llm"] = "false",
                        ["downgrade_reason"] = "user_speaking",
                        ["interrupted"] = true,
                        ["llm_triggered"] = false,
                        ["prompt_rebuild"] = true
                    }
                }));

            Assert.AreEqual("success", captured.Status);
            Assert.AreEqual("Context updated", captured.Message);
            Assert.AreEqual("ctx-1", captured.UpdateId);
            Assert.AreEqual(7, captured.ContextRevision);
            Assert.AreEqual(120, captured.TokenCount);
            Assert.AreEqual(30, captured.StaticTokenCount);
            Assert.AreEqual(90, captured.RuntimeTokenCount);
            Assert.AreEqual(29880, captured.RemainingTokens);
            Assert.AreEqual("true", captured.RequestedRunLlm);
            Assert.AreEqual("false", captured.ActualRunLlm);
            Assert.AreEqual("user_speaking", captured.DowngradeReason);
            Assert.IsTrue(captured.Interrupted);
            Assert.IsFalse(captured.LlmTriggered);
            Assert.IsTrue(captured.PromptRebuild);
            Assert.NotNull(captured.RawExtras);
        }

        [Test]
        public void ServerMessage_ServerResponseContextUpdate_ToleratesMixedTypedResultFields()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out _);
            DynamicContextUpdateResultReceived captured = default;
            eventHub.Subscribe<DynamicContextUpdateResultReceived>(e => captured = e);

            Assert.DoesNotThrow(() => gateway.ProcessIncoming(CreateServerMessagePacket(
                "server-response",
                new JObject
                {
                    ["event_type"] = "context-update",
                    ["status"] = "success",
                    ["message"] = "Context updated",
                    ["update_id"] = "ctx-2",
                    ["context_revision"] = "8",
                    ["token_count"] = "121",
                    ["static_token_count"] = 31,
                    ["runtime_token_count"] = "90",
                    ["remaining_tokens"] = 29879,
                    ["requested_run_llm"] = true,
                    ["actual_run_llm"] = "auto",
                    ["downgrade_reason"] = null,
                    ["interrupted"] = "false",
                    ["llm_triggered"] = "auto",
                    ["prompt_rebuild"] = 1
                })));

            Assert.AreEqual("ctx-2", captured.UpdateId);
            Assert.AreEqual(8, captured.ContextRevision);
            Assert.AreEqual(121, captured.TokenCount);
            Assert.AreEqual(31, captured.StaticTokenCount);
            Assert.AreEqual(90, captured.RuntimeTokenCount);
            Assert.AreEqual(29879, captured.RemainingTokens);
            Assert.AreEqual("true", captured.RequestedRunLlm);
            Assert.AreEqual("auto", captured.ActualRunLlm);
            Assert.IsFalse(captured.Interrupted);
            Assert.IsFalse(captured.LlmTriggered);
            Assert.IsTrue(captured.PromptRebuild);
        }

        [Test]
        public void ServerMessage_LlmNoResponse_Publishes_Character_Context()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out TestAgentRegistry registry);
            registry.RegisterCharacter(new TestCharacterAgent("char-1", "Camila"));
            registry.SetParticipantId("char-1", "participant-1");

            LlmNoResponseReceived captured = default;
            eventHub.Subscribe<LlmNoResponseReceived>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "llm-no-response",
                new JObject
                {
                    ["reason"] = "abstain"
                },
                "participant-1"));

            Assert.AreEqual("char-1", captured.CharacterId);
            Assert.AreEqual("participant-1", captured.ParticipantId);
            Assert.AreEqual("abstain", captured.Reason);
        }

        [Test]
        public void ServerMessage_InteractionCreated_Publishes_Interaction_Context()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out TestAgentRegistry registry);
            registry.RegisterCharacter(new TestCharacterAgent("char-1", "Camila"));
            registry.SetParticipantId("char-1", "participant-1");

            InteractionCreated captured = default;
            eventHub.Subscribe<InteractionCreated>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "interaction-created",
                new JObject
                {
                    ["interaction_id"] = "a4fce023-d850-4210-9d10-8c98228d1b4b",
                    ["character_session_id"] = "0ded6a03-aeec-4c8b-a64b-0ee910695203"
                },
                "participant-1"));

            Assert.AreEqual("char-1", captured.CharacterId);
            Assert.AreEqual("participant-1", captured.ParticipantId);
            Assert.AreEqual("a4fce023-d850-4210-9d10-8c98228d1b4b", captured.InteractionId);
            Assert.AreEqual("0ded6a03-aeec-4c8b-a64b-0ee910695203", captured.CharacterSessionId);
        }

        [Test]
        public void ServerMessage_FinalUserTranscription_Publishes_Dedicated_Event()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out _);
            FinalUserTranscriptionReceived captured = default;
            eventHub.Subscribe<FinalUserTranscriptionReceived>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "final-user-transcription",
                new JObject
                {
                    ["text"] = "Hello there",
                    ["message_id"] = "typed-1",
                    ["speaker_id"] = "speaker-1",
                    ["speaker_name"] = "Rishav",
                    ["participant_id"] = "PA_1"
                }));

            Assert.AreEqual("Hello there", captured.Text);
            Assert.AreEqual("typed-1", captured.MessageId);
            Assert.AreEqual("speaker-1", captured.SpeakerId);
            Assert.AreEqual("Rishav", captured.SpeakerName);
            Assert.AreEqual("PA_1", captured.ParticipantId);
        }

        [Test]
        public void TypedTextEcho_UserTranscriptionCycle_Does_Not_Reach_PlayerTranscriptionCoordinator()
        {
            EventHub eventHub = CreateEventHub();
            RecordingPlayerSession playerSession = new();
            RTVIHandler handler = CreateHandler(eventHub, playerSession, out ProtocolGateway gateway, out _);

            handler.SendData(new RTVIUserTextMessage("hello", "typed-1"));

            gateway.ProcessIncoming(CreateSimpleInboundPacket("user-started-speaking"));
            gateway.ProcessIncoming(CreateUserTranscriptionPacket("hello", isFinal: true));
            gateway.ProcessIncoming(CreateSimpleInboundPacket("user-stopped-speaking"));

            Thread.Sleep(300);

            Assert.IsEmpty(playerSession.StartedSessionIds);
            Assert.IsEmpty(playerSession.StoppedSessions);
            Assert.IsEmpty(playerSession.Transcriptions);
        }

        [Test]
        public void TypedTextEcho_FinalUserTranscriptionWithoutMessageId_Updates_Typed_Row_By_Pending_Id()
        {
            EventHub eventHub = CreateEventHub();
            RecordingPlayerSession playerSession = new();
            RTVIHandler handler = CreateHandler(eventHub, playerSession, out ProtocolGateway gateway, out _);
            FinalUserTranscriptionReceived captured = default;
            eventHub.Subscribe<FinalUserTranscriptionReceived>(e => captured = e);

            handler.SendData(new RTVIUserTextMessage("hello how you doing", "typed-1"));

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "final-user-transcription",
                new JObject
                {
                    ["text"] = "hello how you doing"
                }));

            Assert.AreEqual("typed-1", captured.MessageId);
            Assert.IsEmpty(playerSession.Transcriptions);
            Assert.AreEqual(1, playerSession.TypedTranscriptions.Count);
            Assert.AreEqual("typed-1", playerSession.TypedTranscriptions[0].MessageId);
            Assert.AreEqual("hello how you doing", playerSession.TypedTranscriptions[0].Text);
        }

        [Test]
        public void TypedTextEcho_Suppression_Clears_And_Later_RealSpeech_Still_Uses_Normal_Path()
        {
            EventHub eventHub = CreateEventHub();
            RecordingPlayerSession playerSession = new();
            RTVIHandler handler = CreateHandler(eventHub, playerSession, out ProtocolGateway gateway, out _);

            handler.SendData(new RTVIUserTextMessage("hello", "typed-1"));

            gateway.ProcessIncoming(CreateSimpleInboundPacket("user-started-speaking"));
            gateway.ProcessIncoming(CreateUserTranscriptionPacket("hello", isFinal: true));
            gateway.ProcessIncoming(CreateSimpleInboundPacket("user-stopped-speaking"));

            gateway.ProcessIncoming(CreateSimpleInboundPacket("user-started-speaking"));
            gateway.ProcessIncoming(CreateUserTranscriptionPacket("real speech", isFinal: false));
            gateway.ProcessIncoming(CreateUserTranscriptionPacket("real speech done", isFinal: true));
            gateway.ProcessIncoming(CreateSimpleInboundPacket("user-stopped-speaking"));

            Thread.Sleep(300);

            Assert.AreEqual(1, playerSession.StartedSessionIds.Count);
            Assert.AreEqual(1, playerSession.StoppedSessions.Count);
            CollectionAssert.AreEqual(
                new[]
                {
                    TranscriptionPhase.Listening,
                    TranscriptionPhase.Interim,
                    TranscriptionPhase.AsrFinal,
                    TranscriptionPhase.Completed
                },
                playerSession.Transcriptions.Select(entry => entry.Phase).ToArray());
            Assert.AreEqual("real speech", playerSession.Transcriptions[1].Text);
            Assert.AreEqual("real speech done", playerSession.Transcriptions[2].Text);
        }

        [Test]
        public void TypedTextEcho_MismatchedSpeechInsideSuppressionWindow_Starts_Normal_Path()
        {
            EventHub eventHub = CreateEventHub();
            RecordingPlayerSession playerSession = new();
            RTVIHandler handler = CreateHandler(eventHub, playerSession, out ProtocolGateway gateway, out _);
            int startedEvents = 0;
            eventHub.Subscribe<PlayerSpeakingStateChanged>(e =>
            {
                if (e.IsSpeaking) startedEvents++;
            });

            handler.SendData(new RTVIUserTextMessage("typed text", "typed-1"));

            gateway.ProcessIncoming(CreateSimpleInboundPacket("user-started-speaking"));
            gateway.ProcessIncoming(CreateUserTranscriptionPacket("real speech", isFinal: false));
            gateway.ProcessIncoming(CreateSimpleInboundPacket("user-stopped-speaking"));

            Thread.Sleep(300);

            Assert.AreEqual(1, startedEvents);
            Assert.AreEqual(1, playerSession.StartedSessionIds.Count);
            Assert.AreEqual(1, playerSession.StoppedSessions.Count);
            CollectionAssert.AreEqual(
                new[]
                {
                    TranscriptionPhase.Listening,
                    TranscriptionPhase.Interim,
                    TranscriptionPhase.Completed
                },
                playerSession.Transcriptions.Select(entry => entry.Phase).ToArray());
            Assert.AreEqual("real speech", playerSession.Transcriptions[1].Text);
        }

        [Test]
        public void TypedTextEcho_BotTranscriptAndTtsEcho_Do_Not_Publish_Character_Events()
        {
            EventHub eventHub = CreateEventHub();
            RTVIHandler handler = CreateHandler(eventHub, out ProtocolGateway gateway, out TestAgentRegistry registry);
            registry.RegisterCharacter(new TestCharacterAgent("char-1", "Camila"));
            registry.SetParticipantId("char-1", "participant-1");

            int characterTranscriptCount = 0;
            int ttsChunkCount = 0;
            eventHub.Subscribe<CharacterTranscriptReceived>(_ => characterTranscriptCount++);
            eventHub.Subscribe<CharacterTtsTextChunk>(_ => ttsChunkCount++);

            handler.SendData(new RTVIUserTextMessage("hello", "typed-1"));

            gateway.ProcessIncoming(CreateSimpleInboundPacket("user-started-speaking"));
            gateway.ProcessIncoming(CreateUserTranscriptionPacket("hello", isFinal: true));
            gateway.ProcessIncoming(CreateSimpleInboundPacket("user-stopped-speaking"));
            gateway.ProcessIncoming(CreateBotTranscriptionPacket("bot-llm-text", "Hello.", "participant-1"));
            gateway.ProcessIncoming(CreateBotTranscriptionPacket("bot-transcription", "Hello.", "participant-1"));
            gateway.ProcessIncoming(CreateBotTranscriptionPacket("bot-tts-text", "Hello.", "participant-1"));

            Assert.AreEqual(0, characterTranscriptCount);
            Assert.AreEqual(0, ttsChunkCount);
        }

        [Test]
        public void TypedTextEcho_BotDifferentText_Still_Publishes_Character_Events()
        {
            EventHub eventHub = CreateEventHub();
            RTVIHandler handler = CreateHandler(eventHub, out ProtocolGateway gateway, out TestAgentRegistry registry);
            registry.RegisterCharacter(new TestCharacterAgent("char-1", "Camila"));
            registry.SetParticipantId("char-1", "participant-1");

            var characterTexts = new List<string>();
            var ttsTexts = new List<string>();
            eventHub.Subscribe<CharacterTranscriptReceived>(e => characterTexts.Add(e.Text));
            eventHub.Subscribe<CharacterTtsTextChunk>(e => ttsTexts.Add(e.Text));

            handler.SendData(new RTVIUserTextMessage("hello", "typed-1"));

            gateway.ProcessIncoming(CreateBotTranscriptionPacket("bot-llm-text", "Hi there.", "participant-1"));
            gateway.ProcessIncoming(CreateBotTranscriptionPacket("bot-transcription", "Hi there.", "participant-1"));
            gateway.ProcessIncoming(CreateBotTranscriptionPacket("bot-tts-text", "Hi there.", "participant-1"));

            CollectionAssert.AreEqual(new[] { "Hi there.", "Hi there." }, characterTexts);
            CollectionAssert.AreEqual(new[] { "Hi there." }, ttsTexts);
        }

        [Test]
        public void ServerMessage_VadSttStarted_Publishes_Event()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out _);
            VadSttStateChanged captured = default;
            eventHub.Subscribe<VadSttStateChanged>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket("vad-stt-started", new JObject()));

            Assert.IsTrue(captured.IsActive);
        }

        [Test]
        public void ServerMessage_VadSttStopped_Publishes_Event()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out _);
            VadSttStateChanged captured = default;
            eventHub.Subscribe<VadSttStateChanged>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket("vad-stt-stopped", new JObject()));

            Assert.IsFalse(captured.IsActive);
        }

        [Test]
        public void ServerMessage_Visemes_Publishes_Raw_Event()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out TestAgentRegistry registry);
            registry.RegisterCharacter(new TestCharacterAgent("char-1", "Camila"));
            registry.SetParticipantId("char-1", "participant-1");

            VisemesReceived captured = default;
            eventHub.Subscribe<VisemesReceived>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "visemes",
                new JObject
                {
                    ["visemes"] = new JObject
                    {
                        ["pp"] = 0.8f,
                        ["aa"] = 0.2f
                    }
                },
                "participant-1"));

            Assert.AreEqual("char-1", captured.CharacterId);
            Assert.AreEqual("participant-1", captured.ParticipantId);
            Assert.AreEqual(0.8f, captured.Visemes["pp"]);
            Assert.AreEqual(0.2f, captured.Visemes["aa"]);
        }

        [Test]
        public void ServerMessage_BlendshapeTurnStats_Publishes_Event_With_AudioDuration()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out TestAgentRegistry registry);
            registry.RegisterCharacter(new TestCharacterAgent("char-1", "Camila"));
            registry.SetParticipantId("char-1", "participant-1");

            BlendshapeTurnStatsReceived captured = default;
            eventHub.Subscribe<BlendshapeTurnStatsReceived>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "blendshape-turn-stats",
                new JObject
                {
                    ["stats"] = new JObject
                    {
                        ["total_blendshapes"] = 150,
                        ["total_audio_bytes"] = 48000,
                        ["total_turn_duration_ms"] = 3000.0,
                        ["total_audio_duration_ms"] = 2800.0,
                        ["fps"] = 50.0
                    }
                },
                "participant-1"));

            Assert.AreEqual("char-1", captured.CharacterId);
            Assert.AreEqual("participant-1", captured.ParticipantId);
            Assert.AreEqual(150, captured.TotalBlendshapes);
            Assert.AreEqual(2800d, captured.TotalAudioDurationMs);
            Assert.IsFalse(captured.FrameCountMatches);
        }

        [Test]
        public void ServerMessage_ActionResponse_PublishesOrderedActions()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out TestAgentRegistry registry);
            registry.RegisterCharacter(new TestCharacterAgent("char-1", "Camila"));
            registry.SetParticipantId("char-1", "participant-1");

            CharacterActionReceived captured = default;
            eventHub.Subscribe<CharacterActionReceived>(e => captured = e);

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "action-response",
                new JObject
                {
                    ["actions"] = new JArray(
                        new JObject
                        {
                            ["name"] = "Move To",
                            ["target"] = "cube"
                        },
                        new JObject
                        {
                            ["name"] = "Pick Up",
                            ["target"] = "cube"
                        })
                },
                "participant-1"));

            Assert.AreEqual("char-1", captured.CharacterId);
            Assert.AreEqual(2, captured.Actions.Count);
            Assert.AreEqual("Move To", captured.Actions[0].Name);
            Assert.AreEqual("cube", captured.Actions[0].Target);
            Assert.AreEqual("Pick Up", captured.Actions[1].Name);
            Assert.AreEqual("cube", captured.Actions[1].Target);
        }

        [Test]
        public void ServerMessage_ActionResponse_EmptyArrayStillPublishesNoOpBatch()
        {
            EventHub eventHub = CreateEventHub();
            CreateHandler(eventHub, out ProtocolGateway gateway, out TestAgentRegistry registry);
            registry.RegisterCharacter(new TestCharacterAgent("char-1", "Camila"));
            registry.SetParticipantId("char-1", "participant-1");

            CharacterActionReceived captured = default;
            bool invoked = false;
            eventHub.Subscribe<CharacterActionReceived>(e =>
            {
                invoked = true;
                captured = e;
            });

            gateway.ProcessIncoming(CreateServerMessagePacket(
                "action-response",
                new JObject
                {
                    ["actions"] = new JArray()
                },
                "participant-1"));

            Assert.IsTrue(invoked);
            Assert.AreEqual("char-1", captured.CharacterId);
            Assert.AreEqual(0, captured.Actions.Count);
        }

        private static EventHub CreateEventHub() => new(new ImmediateScheduler(), new TestLogger());

        private static RTVIHandler CreateHandler(EventHub eventHub, out ProtocolGateway gateway,
            out TestAgentRegistry agentRegistry) =>
            CreateHandler(eventHub, new RecordingPlayerSession(), out gateway, out agentRegistry);

        private static RTVIHandler CreateHandler(EventHub eventHub, RecordingPlayerSession playerSession,
            out ProtocolGateway gateway, out TestAgentRegistry agentRegistry)
        {
            gateway = new ProtocolGateway();
            agentRegistry = new TestAgentRegistry();
            return new RTVIHandler(
                gateway,
                new RecordingTransport(),
                agentRegistry,
                playerSession,
                new ImmediateDispatcher(),
                new TestLogger(),
                eventHub);
        }

        private static ProtocolPacket CreateSimpleInboundPacket(string type)
        {
            JObject outer = new()
            {
                ["type"] = type
            };

            return new ProtocolPacket(
                Encoding.UTF8.GetBytes(outer.ToString()),
                string.Empty,
                "rtvi-ai",
                true);
        }

        private static ProtocolPacket CreateUserTranscriptionPacket(string text, bool isFinal)
        {
            JObject outer = new()
            {
                ["type"] = "user-transcription",
                ["data"] = new JObject
                {
                    ["text"] = text,
                    ["final"] = isFinal
                }
            };

            return new ProtocolPacket(
                Encoding.UTF8.GetBytes(outer.ToString()),
                string.Empty,
                "rtvi-ai",
                true);
        }

        private static ProtocolPacket CreateBotTranscriptionPacket(string type, string text, string participantId)
        {
            JObject outer = new()
            {
                ["type"] = type,
                ["data"] = new JObject
                {
                    ["text"] = text
                }
            };

            return new ProtocolPacket(
                Encoding.UTF8.GetBytes(outer.ToString()),
                participantId,
                "rtvi-ai",
                true);
        }

        private static ProtocolPacket CreateServerMessagePacket(string innerType, JObject payload,
            string participantId = "")
        {
            payload ??= new JObject();
            payload["type"] = innerType;

            JObject outer = new()
            {
                ["type"] = "server-message",
                ["data"] = payload
            };

            return new ProtocolPacket(
                Encoding.UTF8.GetBytes(outer.ToString()),
                participantId,
                "rtvi-ai",
                true);
        }

        private sealed class ImmediateScheduler : IUnityScheduler
        {
            public void ScheduleOnMainThread(Action action) => action?.Invoke();
            public void ScheduleOnBackground(Action action) => action?.Invoke();
            public bool IsMainThread() => true;
        }

        private sealed class ImmediateDispatcher : IMainThreadDispatcher
        {
            public bool TryDispatch(Action action)
            {
                action?.Invoke();
                return true;
            }
        }

        private sealed class RecordingPlayerSession : IPlayerSession, IPlayerTypedTranscriptSink
        {
            public readonly List<string> StartedSessionIds = new();
            public readonly List<StoppedSession> StoppedSessions = new();
            public readonly List<TranscriptionEntry> Transcriptions = new();
            public readonly List<TypedTranscriptionEntry> TypedTranscriptions = new();

            public string PlayerId => "player-1";
            public string PlayerName => "Player";
            public bool IsMicMuted { get; private set; }
            public event Action<string> MicrophoneStreamStarted;
            public event Action<string> MicrophoneStreamStopped;

            public void StartListening(int microphoneIndex = 0) { }
            public void StopListening() { }
            public void SetMicMuted(bool mute) => IsMicMuted = mute;
            public void SetMicrophoneIndex(int index) { }
            public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase) =>
                Transcriptions.Add(new TranscriptionEntry(transcript, transcriptionPhase));

            public void OnPlayerTranscriptionReceived(string transcript, TranscriptionPhase transcriptionPhase,
                SpeakerInfo speakerInfo) => Transcriptions.Add(new TranscriptionEntry(transcript, transcriptionPhase));

            public void OnPlayerStartedSpeaking(string sessionId)
            {
                StartedSessionIds.Add(sessionId);
                MicrophoneStreamStarted?.Invoke(sessionId);
            }

            public void OnPlayerStoppedSpeaking(string sessionId, bool didProduceFinalTranscript)
            {
                StoppedSessions.Add(new StoppedSession(sessionId, didProduceFinalTranscript));
                MicrophoneStreamStopped?.Invoke(sessionId);
            }

            public void PublishTypedText(string transcript, string messageId, SpeakerInfo speakerInfo = default) =>
                TypedTranscriptions.Add(new TypedTranscriptionEntry(transcript, messageId, speakerInfo));
        }

        private readonly struct TypedTranscriptionEntry
        {
            public TypedTranscriptionEntry(string text, string messageId, SpeakerInfo speakerInfo)
            {
                Text = text;
                MessageId = messageId;
                SpeakerInfo = speakerInfo;
            }

            public string Text { get; }
            public string MessageId { get; }
            public SpeakerInfo SpeakerInfo { get; }
        }

        private readonly struct TranscriptionEntry
        {
            public TranscriptionEntry(string text, TranscriptionPhase phase)
            {
                Text = text;
                Phase = phase;
            }

            public string Text { get; }
            public TranscriptionPhase Phase { get; }
        }

        private readonly struct StoppedSession
        {
            public StoppedSession(string sessionId, bool didProduceFinalTranscript)
            {
                SessionId = sessionId;
                DidProduceFinalTranscript = didProduceFinalTranscript;
            }

            public string SessionId { get; }
            public bool DidProduceFinalTranscript { get; }
        }

        private sealed class RecordingTransport : IRealtimeTransport
        {
            public readonly List<string> SentPayloads = new();

            public event Action<DataPacket> DataReceived
            {
                add { }
                remove { }
            }

            public event Action<TransportSessionInfo> Connected
            {
                add { }
                remove { }
            }

            public event Action<DisconnectReason> Disconnected
            {
                add { }
                remove { }
            }

            public event Action<TransportError> ConnectionFailed
            {
                add { }
                remove { }
            }

            public event Action Reconnecting
            {
                add { }
                remove { }
            }

            public event Action Reconnected
            {
                add { }
                remove { }
            }

            public event Action<TransportState> StateChanged
            {
                add { }
                remove { }
            }

            public event Action<TransportParticipantInfo> ParticipantConnected
            {
                add { }
                remove { }
            }

            public event Action<TransportParticipantInfo> ParticipantDisconnected
            {
                add { }
                remove { }
            }

            public event Action<TrackInfo> TrackSubscribed
            {
                add { }
                remove { }
            }

            public event Action<TrackInfo> TrackUnsubscribed
            {
                add { }
                remove { }
            }

            public event Action<bool> MicrophoneEnabledChanged
            {
                add { }
                remove { }
            }

            public event Action<bool> MicrophoneMuteChanged
            {
                add { }
                remove { }
            }

            public event Action<bool> AudioPlaybackStateChanged
            {
                add { }
                remove { }
            }

            public Task SendDataAsync(ReadOnlyMemory<byte> payload, bool reliable = true, string topic = null,
                string[] destinationIdentities = null, CancellationToken ct = default)
            {
                SentPayloads.Add(Encoding.UTF8.GetString(payload.Span));
                return Task.CompletedTask;
            }

            public TransportState State => TransportState.Connected;
            public TransportSessionInfo? CurrentSession => null;
            public TransportCapabilities Capabilities => default;
            public AudioRuntimeState AudioState => default;
            public bool IsConnected => true;
            public IRoomFacade Room => null;
            public Task<bool> ConnectAsync(string url, string token, TransportConnectOptions options = null,
                CancellationToken ct = default) => Task.FromResult(true);
            public Task DisconnectAsync(DisconnectReason reason = DisconnectReason.ClientInitiated,
                CancellationToken ct = default) => Task.CompletedTask;
            public void EnableAudio() { }
            public Task<bool> EnableMicrophoneAsync(int microphoneDeviceIndex = 0, CancellationToken ct = default) =>
                Task.FromResult(true);
            public Task DisableMicrophoneAsync(CancellationToken ct = default) => Task.CompletedTask;
            public void SetMicrophoneMuted(bool muted) { }
            public bool IsMicrophoneEnabled => true;
            public bool IsMicrophoneMuted => false;
            public bool CanEnableMicrophone() => true;
            public bool CanEnableAudio() => true;
            public void Dispose() { }
        }

        private sealed class TestCharacterAgent : IConvaiCharacterAgent
        {
            public TestCharacterAgent(string characterId, string characterName)
            {
                CharacterId = characterId;
                CharacterName = characterName;
            }

            public string CharacterId { get; }
            public string CharacterName { get; }
            public Color NameTagColor => Color.white;
            public bool EnableSessionResume => false;
            public string InitialDynamicInfoText => string.Empty;
            public bool InitialDynamicInfoKeepInContext => false;
            public IConvaiDynamicContext DynamicContext { get; } = new MockDynamicContext();
            public void SendTrigger(string triggerName) { }
            public void SendNarrativeEvent(string eventMessage) { }
            public void SendNarrativeSpeech(string speechText) { }
            public void UpdateTemplateKeys(Dictionary<string, string> templateKeys) { }
        }

        private sealed class TestAgentRegistry : IAgentRegistry
        {
            private readonly Dictionary<string, IConvaiCharacterAgent> _characters = new();
            private readonly Dictionary<string, string> _characterToParticipant = new();
            private readonly Dictionary<string, string> _participantToCharacter = new();
            private readonly List<IConvaiPlayerAgent> _players = new();

            public IReadOnlyList<IConvaiCharacterAgent> Characters => new List<IConvaiCharacterAgent>(_characters.Values);
            public IReadOnlyList<IConvaiPlayerAgent> Players => _players;
            public IConvaiPlayerAgent LocalPlayer => _players.Count > 0 ? _players[0] : null;
            public event Action<IConvaiCharacterAgent> CharacterRegistered;
            public event Action<IConvaiCharacterAgent> CharacterUnregistered;
            public event Action<IConvaiPlayerAgent> PlayerRegistered;

            public void RegisterCharacter(IConvaiCharacterAgent character, string ownerId = null)
            {
                _characters[character.CharacterId] = character;
                CharacterRegistered?.Invoke(character);
            }

            public void RegisterPlayer(IConvaiPlayerAgent player)
            {
                _players.Add(player);
                PlayerRegistered?.Invoke(player);
            }

            public void Unregister(IConvaiCharacterAgent character)
            {
                if (character == null) return;
                _characters.Remove(character.CharacterId);
                CharacterUnregistered?.Invoke(character);
            }

            public void Unregister(IConvaiPlayerAgent player) => _players.Remove(player);
            public bool TryGetCharacter(string characterId, out IConvaiCharacterAgent agent) =>
                _characters.TryGetValue(characterId ?? string.Empty, out agent);
            public string GetOwner(IConvaiCharacterAgent character) => null;
            public IReadOnlyList<IConvaiCharacterAgent> GetCharactersByOwner(string ownerId) => Array.Empty<IConvaiCharacterAgent>();
            public int GetCharacterCountByOwner(string ownerId) => 0;
            public bool TryGetCharacterById(string characterId, out IConvaiCharacterAgent agent) =>
                TryGetCharacter(characterId, out agent);
            public bool TryGetAudioSource(string characterId, out AudioSource source)
            {
                source = null;
                return false;
            }

            public void SetAudioSource(string characterId, AudioSource source) { }

            public void SetParticipantId(string characterId, string participantId)
            {
                if (string.IsNullOrWhiteSpace(characterId))
                    return;

                if (_characterToParticipant.TryGetValue(characterId, out string existingParticipant) &&
                    !string.IsNullOrWhiteSpace(existingParticipant))
                    _participantToCharacter.Remove(existingParticipant);

                if (string.IsNullOrWhiteSpace(participantId))
                {
                    _characterToParticipant.Remove(characterId);
                    return;
                }

                _characterToParticipant[characterId] = participantId;
                _participantToCharacter[participantId] = characterId;
            }

            public bool TryGetParticipantId(string characterId, out string participantId) =>
                _characterToParticipant.TryGetValue(characterId ?? string.Empty, out participantId);

            public bool TryGetCharacterByParticipantId(string participantId, out IConvaiCharacterAgent agent)
            {
                agent = null;
                if (string.IsNullOrWhiteSpace(participantId) ||
                    !_participantToCharacter.TryGetValue(participantId, out string characterId))
                    return false;

                return _characters.TryGetValue(characterId, out agent);
            }

            public void ClearTransportBindings()
            {
                _characterToParticipant.Clear();
                _participantToCharacter.Clear();
            }

            public void SetCharacterMuted(string characterId, bool muted) { }
            public bool IsCharacterMuted(string characterId) => false;
        }

        private sealed class TestLogger : Convai.Domain.Logging.ILogger
        {
            public bool IsEnabled(LogLevel level, LogCategory category) => false;
            public void Log(LogLevel level, string message, LogCategory category = LogCategory.SDK) { }
            public void Log(LogLevel level, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Debug(string message, LogCategory category = LogCategory.SDK) { }
            public void Debug(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Info(string message, LogCategory category = LogCategory.SDK) { }
            public void Info(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Warning(string message, LogCategory category = LogCategory.SDK) { }
            public void Warning(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Error(string message, LogCategory category = LogCategory.SDK) { }
            public void Error(string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
            public void Error(Exception exception, string message, LogCategory category = LogCategory.SDK) { }
            public void Error(Exception exception, string message, IReadOnlyDictionary<string, object> context,
                LogCategory category = LogCategory.SDK)
            { }
        }
    }
}
