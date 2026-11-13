using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MQTTUnity;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;


namespace SOSXR.Talkies.Editor
{
    /// <summary>
    ///     Draws the default Inspector, then one button per method marked with <see cref="ButtonAttribute" />.
    ///     Methods with parameters get input fields (int, float, double, long, byte, bool, string, enums).
    ///     Buttons run on every selected object, with Undo.
    ///     <para>
    ///         Registered for the Talkies components below. To get buttons on your own component, derive an editor:
    ///         <c>[CustomEditor(typeof(MyComponent))] class MyEditor : TalkiesButtonEditor { }</c>.
    ///     </para>
    /// </summary>
    [CanEditMultipleObjects]
    public class TalkiesButtonEditor : UnityEditor.Editor
    {
        private readonly Dictionary<MethodInfo, object[]> _arguments = new();


        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            DrawButtons();
        }


        /// <summary>Draws the buttons only. Call from a derived editor after drawing its own fields.</summary>
        protected void DrawButtons()
        {
            if (target == null)
            {
                return;
            }

            foreach (var method in ButtonMethodUtility.GetButtonMethods(target.GetType()))
            {
                DrawButton(method);
            }
        }


        private void DrawButton(MethodInfo method)
        {
            var attribute = method.GetCustomAttribute<ButtonAttribute>();

            if (attribute.Space > 0)
            {
                GUILayout.Space(attribute.Space);
            }

            if (attribute.HorizontalLine)
            {
                var line = EditorGUILayout.GetControlRect(false, 1f);
                EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 0.5f));
            }

            var label = new GUIContent(ButtonMethodUtility.GetLabel(method), attribute.Tooltip);

            if (!ButtonMethodUtility.CanInvokeFromInspector(method))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    label.tooltip = "This method has parameters the Inspector cannot draw.";
                    GUILayout.Button(label);
                }

                return;
            }

            var parameters = method.GetParameters();

            if (!_arguments.TryGetValue(method, out var args))
            {
                args = ButtonMethodUtility.GetDefaultArguments(method);
                _arguments[method] = args;
            }

            if (parameters.Length > 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                for (var i = 0; i < parameters.Length; i++)
                {
                    args[i] = DrawArgument(ObjectNames.NicifyVariableName(parameters[i].Name), parameters[i].ParameterType, args[i]);
                }
            }

            if (GUILayout.Button(label))
            {
                Invoke(method, args);
            }

            if (parameters.Length > 0)
            {
                EditorGUILayout.EndVertical();
            }
        }


        private static object DrawArgument(string label, Type type, object value)
        {
            if (type == typeof(int))
            {
                return EditorGUILayout.IntField(label, (int) value);
            }

            if (type == typeof(float))
            {
                return EditorGUILayout.FloatField(label, (float) value);
            }

            if (type == typeof(double))
            {
                return EditorGUILayout.DoubleField(label, (double) value);
            }

            if (type == typeof(long))
            {
                return EditorGUILayout.LongField(label, (long) value);
            }

            if (type == typeof(byte))
            {
                return (byte) Mathf.Clamp(EditorGUILayout.IntField(label, (byte) value), byte.MinValue, byte.MaxValue);
            }

            if (type == typeof(bool))
            {
                return EditorGUILayout.Toggle(label, (bool) value);
            }

            if (type == typeof(string))
            {
                return EditorGUILayout.TextField(label, (string) value);
            }

            if (type.IsEnum)
            {
                return EditorGUILayout.EnumPopup(label, (Enum) value);
            }

            return value;
        }


        private void Invoke(MethodInfo method, object[] args)
        {
            var objects = method.IsStatic ? new[] {target} : targets;

            Undo.RecordObjects(objects, ButtonMethodUtility.GetLabel(method));

            foreach (var obj in objects)
            {
                try
                {
                    method.Invoke(method.IsStatic ? null : obj, args.ToArray());
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogException(e.InnerException ?? e, obj);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, obj);
                }

                if (obj != null)
                {
                    EditorUtility.SetDirty(obj);
                }
            }
        }
    }


    // Specific registrations win over other packages' editors for base types (such as UnityEngine.Object).
    [CanEditMultipleObjects] [CustomEditor(typeof(PinController), true)] internal class PinControllerEditor : TalkiesButtonEditor { }
    [CanEditMultipleObjects] [CustomEditor(typeof(PinTimer), true)] internal class PinTimerEditor : TalkiesButtonEditor { }
    [CanEditMultipleObjects] [CustomEditor(typeof(PinTimerWithEvents), true)] internal class PinTimerWithEventsEditor : TalkiesButtonEditor { }
    [CanEditMultipleObjects] [CustomEditor(typeof(DesktopSerialConnector), true)] internal class DesktopSerialConnectorEditor : TalkiesButtonEditor { }
    [CanEditMultipleObjects] [CustomEditor(typeof(AndroidSerialConnector), true)] internal class AndroidSerialConnectorEditor : TalkiesButtonEditor { }
    [CanEditMultipleObjects] [CustomEditor(typeof(ByteSize), true)] internal class ByteSizeEditor : TalkiesButtonEditor { }
    [CanEditMultipleObjects] [CustomEditor(typeof(MQTTTester), true)] internal class MQTTTesterEditor : TalkiesButtonEditor { }
}
