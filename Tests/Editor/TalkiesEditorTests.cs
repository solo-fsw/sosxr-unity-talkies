using System.Linq;
using MQTTUnity;
using NUnit.Framework;
using SOSXR.Talkies.Editor;
using UnityEditor;
using UnityEngine;


namespace SOSXR.Talkies.Tests
{
    /// <summary>Tests for the Inspector support in <c>SOSXR.Talkies.Editor</c>: buttons and attribute drawers.</summary>
    public class TalkiesEditorTests
    {
        private class ButtonSample
        {
            [Button] public void NoArgs() { }
            [Button("Custom Label")] public void Labelled() { }
            [Button] public void WithArgs(int pin, bool value, string text, BaudRate rate, float f = 2.5f) { }
            [Button] public void Unsupported(GameObject go) { }
            public void NotAButton() { }
        }


        private class DerivedButtonSample : ButtonSample
        {
            [Button] public void Extra() { }
        }


        [Test]
        public void GetButtonMethods_FindsOnlyMarkedMethods_BaseFirst()
        {
            var names = ButtonMethodUtility.GetButtonMethods(typeof(DerivedButtonSample)).Select(m => m.Name).ToArray();

            Assert.That(names, Is.EqualTo(new[] {"NoArgs", "Labelled", "WithArgs", "Unsupported", "Extra"}));
        }


        [Test]
        public void GetButtonMethods_FindsTalkiesButtons()
        {
            var names = ButtonMethodUtility.GetButtonMethods(typeof(PinController)).Select(m => m.Name).ToArray();

            Assert.That(names, Does.Contain(nameof(PinController.SetPin)));
            Assert.That(names, Does.Contain(nameof(PinController.ToggleLED)));
        }


        [Test]
        public void CanInvokeFromInspector_RejectsUnsupportedParameters()
        {
            var methods = ButtonMethodUtility.GetButtonMethods(typeof(ButtonSample)).ToDictionary(m => m.Name);

            Assert.That(ButtonMethodUtility.CanInvokeFromInspector(methods["NoArgs"]), Is.True);
            Assert.That(ButtonMethodUtility.CanInvokeFromInspector(methods["WithArgs"]), Is.True);
            Assert.That(ButtonMethodUtility.CanInvokeFromInspector(methods["Unsupported"]), Is.False);
        }


        [Test]
        public void GetDefaultArguments_UsesParameterDefaults()
        {
            var method = typeof(ButtonSample).GetMethod(nameof(ButtonSample.WithArgs));
            var args = ButtonMethodUtility.GetDefaultArguments(method);

            Assert.That(args, Is.EqualTo(new object[] {0, false, string.Empty, BaudRate.B4800, 2.5f}));
        }


        [Test]
        public void GetLabel_UsesItemNameOrSplitsMethodName()
        {
            Assert.That(ButtonMethodUtility.GetLabel(typeof(ButtonSample).GetMethod(nameof(ButtonSample.Labelled))), Is.EqualTo("Custom Label"));
            Assert.That(ButtonMethodUtility.GetLabel(typeof(PinController).GetMethod(nameof(PinController.ToggleLED))), Is.EqualTo("Toggle LED"));
            Assert.That(ButtonMethodUtility.GetLabel(typeof(PinController).GetMethod(nameof(PinController.SetDefaultPin))), Is.EqualTo("Set Default Pin"));
        }


        [TestCase("m_value", "m_flag", "m_flag")]
        [TestCase("nested.m_value", "m_flag", "nested.m_flag")]
        [TestCase("m_list.Array.data[3]", "m_flag", "m_flag")]
        [TestCase("m_list.Array.data[3].m_value", "m_flag", "m_list.Array.data[3].m_flag")]
        public void GetSiblingPath_ResolvesNeighbours(string path, string sibling, string expected)
        {
            Assert.That(ButtonMethodUtility.GetSiblingPath(path, sibling), Is.EqualTo(expected));
        }


        [Test]
        public void TalkiesComponents_GetTheButtonEditor()
        {
            var go = new GameObject("TalkiesEditorTests");

            try
            {
                foreach (var component in new Component[] {go.AddComponent<PinController>(), go.AddComponent<ByteSize>(), go.AddComponent<MQTTTester>()})
                {
                    var editor = UnityEditor.Editor.CreateEditor(component);

                    try
                    {
                        Assert.That(editor, Is.InstanceOf<TalkiesButtonEditor>(), component.GetType().Name);
                    }
                    finally
                    {
                        Object.DestroyImmediate(editor);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }


        [Test]
        public void ConditionUtility_ReadsBoolSibling()
        {
            var go = new GameObject("ConditionUtility");

            try
            {
                var responder = go.AddComponent<MQTTFirstResponder>();
                var so = new SerializedObject(responder);
                var topic = so.FindProperty("m_onlyRespondToThisTopic");

                so.FindProperty("m_toggleTopic").boolValue = false;
                Assert.That(ConditionUtility.IsTrue(topic, "m_toggleTopic"), Is.False);

                so.FindProperty("m_toggleTopic").boolValue = true;
                Assert.That(ConditionUtility.IsTrue(topic, "m_toggleTopic"), Is.True);

                Assert.That(ConditionUtility.IsTrue(topic, "doesNotExist"), Is.True, "A missing condition keeps the field usable.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
