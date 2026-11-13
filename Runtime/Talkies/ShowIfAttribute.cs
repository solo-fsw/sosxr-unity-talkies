using System;
using UnityEngine;


namespace SOSXR.Talkies
{
    /// <summary>Hides the field while the named condition field is false.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class ShowIfAttribute : PropertyAttribute
    {
        /// <summary>Name of the sibling field that decides visibility.</summary>
        public readonly string ConditionField;


        /// <summary>Creates the attribute for <paramref name="conditionField" />.</summary>
        public ShowIfAttribute(string conditionField)
        {
            ConditionField = conditionField;
        }
    }
}
