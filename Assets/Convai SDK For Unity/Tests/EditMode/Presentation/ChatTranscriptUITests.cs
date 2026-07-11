using System.Collections.Generic;
using System.Reflection;
using Convai.Domain.Models;
using Convai.Runtime.Presentation.Presenters;
using Convai.Runtime.Presentation.Views.Transcript;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Tests.EditMode
{
    public class ChatTranscriptUITests
    {
        private readonly List<GameObject> _createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _createdObjects)
                if (go != null)
                    Object.DestroyImmediate(go);

            _createdObjects.Clear();
        }

        [Test]
        public void Completed_Message_Update_Reuses_Existing_Bubble()
        {
            ChatTranscriptUI ui = CreateUi(out RectTransform chatContainer);

            ui.DisplayMessage(CreatePlayerViewModel("typed-1", "helo"));
            ui.CompleteMessage("typed-1");
            ui.DisplayMessage(CreatePlayerViewModel("typed-1", "hello"));

            Assert.AreEqual(1, chatContainer.childCount);
        }

        private ChatTranscriptUI CreateUi(out RectTransform chatContainer)
        {
            GameObject root = Track(new GameObject("ChatTranscriptUI"));
            ChatTranscriptUI ui = root.AddComponent<ChatTranscriptUI>();

            GameObject containerObject = Track(new GameObject("ChatContainer", typeof(RectTransform)));
            chatContainer = containerObject.GetComponent<RectTransform>();

            GameObject playerPrefab = Track(new GameObject("PlayerMessagePrefab"));
            playerPrefab.SetActive(false);

            SetPrivateField(ui, "chatContainer", chatContainer);
            SetPrivateField(ui, "playerMessagePrefab", playerPrefab);
            SetPrivateField(ui, "characterMessagePrefab", playerPrefab);

            return ui;
        }

        private GameObject Track(GameObject go)
        {
            _createdObjects.Add(go);
            return go;
        }

        private static TranscriptViewModel CreatePlayerViewModel(string messageId, string text)
        {
            TranscriptMessage message = TranscriptMessage.ForPlayer(
                text,
                true,
                "player-1",
                "You");

            return new TranscriptViewModel(
                TranscriptSpeaker.Player,
                message,
                text,
                messageId,
                TranscriptLifecycle.Completed);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected private field '{fieldName}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
