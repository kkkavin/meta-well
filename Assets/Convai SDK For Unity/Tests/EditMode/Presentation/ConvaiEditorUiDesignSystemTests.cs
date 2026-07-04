#if UNITY_EDITOR
using System;
using Convai.Editor.Inspectors;
using Convai.Editor.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Convai.Tests.EditMode.Presentation
{
    public class ConvaiEditorUiDesignSystemTests
    {
        [Test]
        public void SectionStateStore_GetSet_RoundTrips()
        {
            string hostId = $"Host_{Guid.NewGuid():N}";
            string sectionId = "Core Setup";
            string key = ConvaiInspectorSectionStateStore.BuildKey(hostId, sectionId);

            EditorPrefs.DeleteKey(key);
            Assert.IsFalse(ConvaiInspectorSectionStateStore.Get(hostId, sectionId, false));

            ConvaiInspectorSectionStateStore.Set(hostId, sectionId, true);
            Assert.IsTrue(ConvaiInspectorSectionStateStore.Get(hostId, sectionId, false));

            ConvaiInspectorSectionStateStore.Set(hostId, sectionId, false);
            Assert.IsFalse(ConvaiInspectorSectionStateStore.Get(hostId, sectionId, true));

            EditorPrefs.DeleteKey(key);
        }

        [Test]
        public void SectionStateStore_BuildKey_NormalizesWhitespace()
        {
            string key = ConvaiInspectorSectionStateStore.BuildKey("Map Debug Window", "Validation Results");
            Assert.AreEqual("Convai.Editor.MapDebugWindow.ValidationResults.Expanded", key);
        }

        [Test]
        public void StyleCache_EnsureInitialized_ReusesStyleInstances()
        {
            ConvaiInspectorStyleCache.EnsureInitialized();
            GUIStyle firstHeader = ConvaiInspectorStyleCache.SectionHeaderLabelStyle;
            GUIStyle firstIcon = ConvaiInspectorStyleCache.SectionIconStyle;
            GUIStyle firstChevron = ConvaiInspectorStyleCache.SectionChevronStyle;

            ConvaiInspectorStyleCache.EnsureInitialized();

            Assert.AreSame(firstHeader, ConvaiInspectorStyleCache.SectionHeaderLabelStyle);
            Assert.AreSame(firstIcon, ConvaiInspectorStyleCache.SectionIconStyle);
            Assert.AreSame(firstChevron, ConvaiInspectorStyleCache.SectionChevronStyle);
        }

        [Test]
        public void IconProvider_GetConvaiIcon_ReturnsNonNullTexture()
        {
            Texture2D icon = ConvaiBrandedIconProvider.GetConvaiIcon();
            Assert.IsNotNull(icon);
        }
    }
}
#endif
