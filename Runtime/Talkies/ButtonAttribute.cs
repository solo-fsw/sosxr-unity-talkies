using System;


namespace SOSXR.Talkies
{
    /// <summary>Shows a method as a button in the Inspector. Parameters get input fields.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ButtonAttribute : Attribute
    {
        /// <summary>Creates a button attribute. All arguments are optional.</summary>
        public ButtonAttribute(string itemName = null, string tooltip = null, int space = 0, bool horizontalLine = false)
        {
            ItemName = itemName;
            Tooltip = tooltip;
            Space = space;
            HorizontalLine = horizontalLine;
        }


        /// <summary>Hover text shown on the button.</summary>
        public string Tooltip { get; set; }

        /// <summary>Button label. Defaults to the method name when null.</summary>
        public string ItemName { get; set; }

        /// <summary>Extra space in pixels above the button.</summary>
        public int Space { get; set; }

        /// <summary>Draws a horizontal line above the button.</summary>
        public bool HorizontalLine { get; set; }
    }
}
