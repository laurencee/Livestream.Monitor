using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interactivity;

namespace Livestream.Monitor.Core.UI
{
    /// <remarks>Sourced from http://stackoverflow.com/a/17715402/2631967 </remarks>
    public class RestrictTextBoxInputBehavior : Behavior<TextBox>
    {
        public static readonly DependencyProperty RegularExpressionProperty =
            DependencyProperty.Register("TextBoxRestrictInputBehavior", typeof (string),
                typeof (RestrictTextBoxInputBehavior), null);

        /// <summary>
        /// Custom regular expression to restrict textbox entry <para />
        /// <see cref="KeypadType"/> should be used most of the time instead of this.
        /// </summary>
        public string RegularExpression
        {
            get { return (string) GetValue(RegularExpressionProperty); }
            set { SetValue(RegularExpressionProperty, value); }
        }

        /// <summary>
        ///     Attach our behaviour. Add event handlers
        /// </summary>
        protected override void OnAttached()
        {
            base.OnAttached();

            AssociatedObject.PreviewTextInput += PreviewTextInputHandler;
            DataObject.AddPastingHandler(AssociatedObject, PastingHandler);
        }

        /// <summary>
        ///     Deattach our behaviour. remove event handlers
        /// </summary>
        protected override void OnDetaching()
        {
            base.OnDetaching();

            AssociatedObject.PreviewTextInput -= PreviewTextInputHandler;
            DataObject.RemovePastingHandler(AssociatedObject, PastingHandler);
        }

        private void PastingHandler(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                string text = Convert.ToString(e.DataObject.GetData(DataFormats.Text));

                if (!ValidateText(text))
                    e.CancelCommand();
            }
            else
                e.CancelCommand();
        }

        private void PreviewTextInputHandler(object sender, TextCompositionEventArgs e)
        {
            string text = this.AssociatedObject.Text
                              .Remove(AssociatedObject.SelectionStart, AssociatedObject.SelectionLength)
                              .Insert(this.AssociatedObject.CaretIndex, e.Text);

            e.Handled = !ValidateText(text);
        }

        /// <summary>
        ///     Validate certain text by our regular expression and text length conditions
        /// </summary>
        /// <param name="text"> Text for validation </param>
        /// <returns> True - valid, False - invalid </returns>
        private bool ValidateText(string text)
        {
            return (new Regex(this.RegularExpression, RegexOptions.IgnoreCase)).IsMatch(text) &&
                   (AssociatedObject.MaxLength == 0 ||
                    text.Length - AssociatedObject.SelectionLength <= AssociatedObject.MaxLength);
        }
    }
}