using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;


namespace SOSXR.Talkies.Editor
{
    /// <summary>
    ///     Finds methods marked with <see cref="ButtonAttribute" /> and works out which parameter types the Inspector can draw.
    ///     No Unity GUI calls, so it can be unit tested.
    /// </summary>
    internal static class ButtonMethodUtility
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<Type, MethodInfo[]> Cache = new();


        /// <summary>Methods with a <see cref="ButtonAttribute" /> on <paramref name="type" /> and its base types, in declaration order.</summary>
        public static MethodInfo[] GetButtonMethods(Type type)
        {
            if (type == null)
            {
                return Array.Empty<MethodInfo>();
            }

            if (Cache.TryGetValue(type, out var methods))
            {
                return methods;
            }

            var result = new List<MethodInfo>();

            // Walk from the base type down, so inherited buttons come first. DeclaredOnly avoids duplicates.
            var chain = new List<Type>();

            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                chain.Insert(0, t);
            }

            foreach (var t in chain)
            {
                result.AddRange(t.GetMethods(Flags | BindingFlags.DeclaredOnly)
                                 .Where(m => m.GetCustomAttribute<ButtonAttribute>() != null && !m.IsGenericMethodDefinition)
                                 .OrderBy(m => m.MetadataToken));
            }

            methods = result.ToArray();
            Cache[type] = methods;

            return methods;
        }


        /// <summary><c>true</c> when the Inspector can show an input field for a parameter of this type.</summary>
        public static bool IsSupportedParameterType(Type type)
        {
            return type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(long) ||
                   type == typeof(bool) || type == typeof(string) || type == typeof(byte) || type.IsEnum;
        }


        /// <summary><c>true</c> when every parameter of <paramref name="method" /> can be drawn.</summary>
        public static bool CanInvokeFromInspector(MethodInfo method)
        {
            return method.GetParameters().All(p => !p.ParameterType.IsByRef && IsSupportedParameterType(p.ParameterType));
        }


        /// <summary>Initial argument values: the parameter's default value if it has one, otherwise the type's default ("" for strings).</summary>
        public static object[] GetDefaultArguments(MethodInfo method)
        {
            return method.GetParameters().Select(GetDefaultValue).ToArray();
        }


        private static object GetDefaultValue(ParameterInfo parameter)
        {
            var type = parameter.ParameterType;

            if (parameter.HasDefaultValue && parameter.DefaultValue != null)
            {
                return type.IsEnum ? Enum.ToObject(type, parameter.DefaultValue) : parameter.DefaultValue;
            }

            if (type == typeof(string))
            {
                return string.Empty;
            }

            if (type.IsEnum)
            {
                var values = Enum.GetValues(type);

                return values.Length > 0 ? values.GetValue(0) : Activator.CreateInstance(type);
            }

            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }


        /// <summary>Label shown on the button: <see cref="ButtonAttribute.ItemName" />, or the method name split into words.</summary>
        public static string GetLabel(MethodInfo method)
        {
            var attribute = method.GetCustomAttribute<ButtonAttribute>();

            if (!string.IsNullOrEmpty(attribute?.ItemName))
            {
                return attribute.ItemName;
            }

            var name = method.Name;
            var chars = new List<char>(name.Length + 4);

            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                {
                    chars.Add(' ');
                }

                chars.Add(name[i]);
            }

            return new string(chars.ToArray());
        }


        /// <summary>
        ///     Builds the property path of a sibling field. For <c>list.Array.data[2].m_value</c> and condition
        ///     <c>m_enabled</c> this returns <c>list.Array.data[2].m_enabled</c>.
        /// </summary>
        public static string GetSiblingPath(string propertyPath, string siblingName)
        {
            if (string.IsNullOrEmpty(propertyPath))
            {
                return siblingName;
            }

            // A drawer on an array field runs per element ("m_list.Array.data[0]"). The sibling belongs to the array field.
            const string arrayMarker = ".Array.data[";

            if (propertyPath.EndsWith("]") && propertyPath.LastIndexOf(arrayMarker, StringComparison.Ordinal) is var arrayIndex and >= 0)
            {
                propertyPath = propertyPath.Substring(0, arrayIndex);
            }

            var lastDot = propertyPath.LastIndexOf('.');

            return lastDot < 0 ? siblingName : propertyPath.Substring(0, lastDot + 1) + siblingName;
        }
    }
}
