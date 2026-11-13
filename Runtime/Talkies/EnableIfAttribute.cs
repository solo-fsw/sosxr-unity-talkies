using System;
using UnityEngine;


namespace SOSXR.Talkies
{
    /// <summary>Greys out the field while the named condition field is false.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class EnableIfAttribute : PropertyAttribute
    {
        /// <summary>Name of the sibling field that decides editability.</summary>
        public readonly string ConditionField;


        /// <summary>Creates the attribute for <paramref name="conditionField" />.</summary>
        public EnableIfAttribute(string conditionField)
        {
            ConditionField = conditionField;
        }
    }
}
