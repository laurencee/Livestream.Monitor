using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using Livestream.Monitor.Model.Monitoring;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace Livestream.Monitor.Core
{
    public static class Extensions
    {
        /// <summary>
        /// Returns spaced output for a Pascal cased string value
        /// <example>"SomePascalCaseString" comes back as "Some Pascal Case String"</example>
        /// </summary>
        /// <param name="pascalCaseValue">A string in Pascal case format</param>
        /// <returns>Spaced Pascal case string</returns>
        public static string ToFriendlyString(this string pascalCaseValue)
        {
            return Regex.Replace(pascalCaseValue, "(?!^)([A-Z])", " $1");
        }

        public static string ReverseFriendlyString(this string friendlyString)
        {
            return friendlyString.Replace(" ", string.Empty);
        }

        /// <summary>
        /// Contains method allowing for a <see cref="StringComparison"/> to be passed in
        /// </summary>
        /// <param name="original">Original string value</param>
        /// <param name="value">String value being compared against</param>
        /// <param name="stringComparison">Specifies the culter, case and sort rules for the comparison</param>
        /// <returns>True if the original string contains the comparing string value following the <see cref="StringComparison"/> rules</returns>
        public static bool Contains(this string original, string value, StringComparison stringComparison)
        {
            return original.IndexOf(value, stringComparison) >= 0;
        }

        public static bool IsEqualTo(this string original, string value, StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
        {
            return String.Compare(original, value, stringComparison) == 0;
        }

        /// <summary> Save implementation to show messages from viewmodels </summary>
        public static async Task<MessageDialogResult> ShowMessageAsync(this IViewAware screen, string title, string message,
            MessageDialogStyle messageDialogStyle = MessageDialogStyle.Affirmative,
            MetroDialogSettings dialogSettings = null)
        {
            return await screen.GetMetroWindowFromScreen().ShowMessageAsync(title, message, messageDialogStyle, dialogSettings);
        }

        public static async Task<string> ShowDialogAsync(this IViewAware screen, string title, string message,
            MetroDialogSettings dialogSettings = null)
        {
            return await screen.GetMetroWindowFromScreen().ShowInputAsync(title, message, dialogSettings);
        }

        public static async Task<ProgressDialogController> ShowProgressAsync(this IViewAware screen, string title, string message,
            bool isCancellable = false,
            MetroDialogSettings dialogSettings = null)
        {
            return await screen.GetMetroWindowFromScreen().ShowProgressAsync(title, message, isCancellable, dialogSettings);
        }

        private static MetroWindow GetMetroWindowFromScreen(this IViewAware screen)
        {
            var uiElement = screen?.GetView() as DependencyObject;
            if (uiElement == null)
                throw new InvalidOperationException($"The view for {typeof(Screen).Name} is not a dependency object");

            var metroWindow = MetroWindow.GetWindow(uiElement) as MetroWindow;
            if (metroWindow == null)
                throw new InvalidOperationException($"Unable to get metro window from screen '{typeof(Screen).Name}'");

            return metroWindow;
        }

        public static IEnumerable<T> SkipAndTake<T>(this IEnumerable<T> query, int skip, int take)
        {
            return query.Skip(skip).Take(take);
        }

        public static void SetLivestreamNotifyState(this LivestreamModel livestreamModel, Settings settings)
        {
            if (livestreamModel == null) return;
            livestreamModel.DontNotify = settings.ExcludeFromNotifying.Any(x => x.IsEqualTo(livestreamModel.Id));
        }

        // sourced from http://stackoverflow.com/a/22078975/2631967
        public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, TimeSpan timeout)
        {
            var timeoutCancellationTokenSource = new CancellationTokenSource();

            var completedTask = await Task.WhenAny(task, Task.Delay(timeout, timeoutCancellationTokenSource.Token));
            if (completedTask == task)
            {
                timeoutCancellationTokenSource.Cancel();
                return await task;
            }

            throw new TimeoutException("The operation has timed out.");
        }

        public static async Task TimeoutAfter(this Task task, TimeSpan timeout)
        {
            var timeoutCancellationTokenSource = new CancellationTokenSource();

            var completedTask = await Task.WhenAny(task, Task.Delay(timeout, timeoutCancellationTokenSource.Token));
            if (completedTask == task)
            {
                timeoutCancellationTokenSource.Cancel();
                await task; // allow any original task exception to bubble up
            }
            else
                throw new TimeoutException("The operation has timed out.");
        }

        /// <summary>
        /// Executes the queries in parallel on the <param name="enumerable" /> values and perform a post query action on the result of the query
        /// within the <see cref="MonitorStreamsModel.HalfRefreshPollingTime"/> timeout.
        /// </summary>
        /// <typeparam name="T">Type of the enumerable values</typeparam>
        /// <typeparam name="TResult">Return type of the query</typeparam>
        /// <param name="enumerable">Collection of <typeparam name="T" /></param>
        /// <param name="query">A task to run on the <typeparam name="T" /> values</param>
        /// <param name="postQueryAction">An action to perform on the result of the queyr</param>
        /// <param name="timeout">Maximum time allowed for these tasks to execute in.</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task ExecuteInParallel<T, TResult>(
            this IEnumerable<T> enumerable,
            Func<T, Task<TResult>> query,
            Action<T, TResult> postQueryAction,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
#pragma warning disable CS4014 // warning is invalid since we await tasks later in this method
            foreach (var value in enumerable)
            {
                var task = query(value);
                var completedTask = task.ContinueWith(t => postQueryAction(value, t.Result), cancellationToken);
                tasks.Add(completedTask);
            }
#pragma warning restore CS4014

            await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(timeout, cancellationToken));
        }

        /// <summary>
        /// Execute the queries in parallel on the <param name="enumerable"/> values 
        /// within the <see cref="MonitorStreamsModel.HalfRefreshPollingTime"/> timeout.
        /// </summary>
        /// <typeparam name="T">Type of the enumerable values</typeparam>
        /// <param name="enumerable">Collection of <typeparam name="T" /></param>
        /// <param name="query">A task to run on the <typeparam name="T" /> values</param>
        /// <param name="timeout">Maximum time allowed for these tasks to execute in.</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task ExecuteInParallel<T>(
            this IEnumerable<T> enumerable,
            Func<T, Task> query,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
#pragma warning disable CS4014 // warning is invalid since we await tasks later in this method
            foreach (var value in enumerable)
            {
                var task = query(value);
                tasks.Add(task);
            }
#pragma warning restore CS4014

            await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(timeout, cancellationToken));
        }
    }
}