using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;
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
            return string.Compare(original, value, stringComparison) == 0;
        }

        /// <summary> Safe implementation to show messages from viewmodels </summary>
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
            if (string.IsNullOrWhiteSpace(livestreamModel.Id)) throw new ArgumentNullException(nameof(LivestreamModel.Id));
            if (livestreamModel.ApiClient == null) throw new ArgumentNullException(nameof(LivestreamModel.ApiClient));

            livestreamModel.DontNotify = settings.ExcludeFromNotifying.Any(x => Equals(x, livestreamModel.ToExcludeNotify()));
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
        /// For each <param name="enumerable"/> value execute the query in parallel
        /// within the <see cref="Constants.HalfRefreshPollingTime"/> timeout.
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
            var queryTasks = enumerable.Select(query);
            await Task.WhenAny(Task.WhenAll(queryTasks), Task.Delay(timeout, cancellationToken));
        }

        /// <summary>
        /// For each <param name="enumerable"/> value execute the query in parallel
        /// within the <see cref="Constants.HalfRefreshPollingTime"/> timeout.
        /// </summary>
        /// <typeparam name="T">Type of the enumerable values</typeparam>
        /// <typeparam name="TResult">Type of the returned collection objects</typeparam>
        /// <param name="enumerable">Collection of <typeparam name="T" /></param>
        /// <param name="query">A task to run on the <typeparam name="T" /> values</param>
        /// <param name="timeout">Maximum time allowed for these tasks to execute in.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>A collection of TResult produced from the <param name="query" /></returns>
        public static async Task<List<TResult>> ExecuteInParallel<T, TResult>(
            this IEnumerable<T> enumerable,
            Func<T, Task<TResult>> query,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var queryTasks = enumerable.Select(query);
            var resultsTask = Task.WhenAll(queryTasks);
            var timeoutTask = Task.Delay(timeout, cancellationToken);
            var completedTask = await Task.WhenAny(resultsTask, timeoutTask);
            return completedTask == timeoutTask ? new List<TResult>() : resultsTask.Result.ToList();
        }

        /// <summary> Rethrows any failed query exceptions or does nothing if no failed queries exist </summary>
        public static void EnsureAllQuerySuccess(this IReadOnlyCollection<LivestreamQueryResult> livestreamQueryResults)
        {
            var failedQuery = livestreamQueryResults.FirstOrDefault(x => !x.IsSuccess);
            if (failedQuery != null)
                throw failedQuery.FailedQueryException;
        }

        /// <summary>
        /// Extracts the base error message and all inner messages from an <see cref="Exception"/> object. <para/>
        /// Returns a concatenated string with each exception message on a new line.
        /// </summary>
        /// <param name="exception"><see cref="Exception"/> object</param>
        /// <param name="skipExceptionLevels">Optional argument to ignore the message from n top level exceptions</param>
        /// <returns>Concatenated string with each exception message on a new line</returns>
        public static string ExtractErrorMessage(this Exception exception, int skipExceptionLevels = 0)
        {
            if (exception == null) return null;

            var aggregateException = exception as AggregateException;
            if (aggregateException != null)
            {
                exception = aggregateException.Flatten();
            }

            // Avoiding large recursion... just in case
            int count = 0;

            var errorMessage = String.Empty;
            if (skipExceptionLevels == 0 && !String.IsNullOrEmpty(exception.Message) && !(exception is AggregateException))
            {
                errorMessage = exception.Message;
                count++;
            }

            while (exception.InnerException != null && count <= 10)
            {
                count++;
                exception = exception.InnerException;

                if (count <= skipExceptionLevels) continue;

                if (!String.IsNullOrEmpty(exception.Message) && !errorMessage.Contains(exception.Message))
                    errorMessage = count == 1
                                       ? exception.Message
                                       : errorMessage + Environment.NewLine + exception.Message;
            }

            return errorMessage;
        }
    }
}