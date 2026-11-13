using UnityEngine;


namespace SOSXR.Talkies
{
    /// <summary>Shows a unit label (such as "ms") after the field.</summary>
    public class SuffixAttribute : PropertyAttribute
    {
        /// <summary>The label text drawn after the field.</summary>
        public readonly string Suffix;


        /// <summary>Creates the attribute with the unit label <paramref name="suffix" />.</summary>
        public SuffixAttribute(string suffix)
        {
            Suffix = suffix;
        }
    }
}
