using System;
using System.Diagnostics.Contracts;
using System.Linq.Expressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Caliburn.Micro;

namespace Livestream.Monitor.Core.Utility
{
    /// <summary> Set keyboard focus extension usable from an IViewAware screen </summary>
    /// <remarks> Sourced from: http://stackoverflow.com/a/20936288 </remarks>
    public static class FocusManager
    {
        /// <summary> 
        /// Usage from a screen:  e.g. property bound to control -> this.SetFocus(() => MyTextBoxProperty);  <para />
        /// If setting focus during initial screen load, this must be called from "OnViewLoaded".
        /// </summary>
        public static bool SetFocus(this IViewAware screen, Expression<Func<object>> propertyExpression)
        {
            return SetFocus(screen, propertyExpression.GetMemberInfo().Name);
        }

        /// <summary> 
        /// Usage from a screen: e.g. control with xName="MyTextBox" -> this.SetFocus("MyTextBox"); <para />
        /// If setting focus during initial screen load, this must be called from "OnViewLoaded".
        /// </summary>
        public static bool SetFocus(this IViewAware screen, string property)
        {
            Contract.Requires(property != null, "Property cannot be null.");
            var view = screen.GetView() as ContentControl;
            if (view != null)
            {
                var control = view.FindChild(property);
                bool focus = control != null && control.Focus();
                return focus;
            }
            return false;
        }

        public static FrameworkElement FindChild(this UIElement parent, string childName)
        {
            // Confirm parent and childName are valid. 
            if (parent == null || string.IsNullOrWhiteSpace(childName)) return null;

            FrameworkElement foundChild = null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childrenCount; i++)
            {
                FrameworkElement child = VisualTreeHelper.GetChild(parent, i) as FrameworkElement;
                if (child != null)
                {
                    BindingExpression bindingExpression = GetBindingExpression(child);
                    if (child.Name == childName)
                    {
                        foundChild = child;
                        break;
                    }
                    if (bindingExpression != null)
                    {
                        if (bindingExpression.ResolvedSourcePropertyName == childName)
                        {
                            foundChild = child;
                            break;
                        }
                    }
                    foundChild = FindChild(child, childName);
                    if (foundChild != null)
                    {
                        if (foundChild.Name == childName)
                            break;
                        BindingExpression foundChildBindingExpression = GetBindingExpression(foundChild);
                        if (foundChildBindingExpression != null &&
                            foundChildBindingExpression.ResolvedSourcePropertyName == childName)
                            break;
                    }

                }
            }

            return foundChild;
        }

        private static BindingExpression GetBindingExpression(FrameworkElement control)
        {
            if (control == null) return null;

            BindingExpression bindingExpression = null;
            var convention = ConventionManager.GetElementConvention(control.GetType());
            if (convention != null)
            {
                var bindablePro = convention.GetBindableProperty(control);
                if (bindablePro != null)
                {
                    bindingExpression = control.GetBindingExpression(bindablePro);
                }
            }
            return bindingExpression;
        }
    }
}
