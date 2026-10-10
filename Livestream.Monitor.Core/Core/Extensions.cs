using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Livestream.Monitor.Model;
using Livestream.Monitor.Model.ApiClients;

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

        /// <summary> Safe with null/empty strings </summary>
        public static int ToInt(this string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0;
            return int.Parse(input);
        }

        public static bool Contains(this IEnumerable<string> collection, string value, StringComparison stringComparison)
        {
            return collection.Any(x => x.IsEqualTo(value, stringComparison));
        }

        public static bool IsEqualTo(this string original, string value, StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
        {
            return string.Compare(original, value, stringComparison) == 0;
        }

        public static IEnumerable<T> SkipAndTake<T>(this IEnumerable<T> query, int skip, int take)
        {
            return query.Skip(skip).Take(take);
        }

        /// <summary> Formats a duration in total hours, e.g. "44h 12m 01s", or "0" for none. </summary>
        public static string ToTotalHoursText(this TimeSpan duration) => duration == TimeSpan.Zero
            ? "0"
            : $"{Math.Floor(duration.TotalHours):00}h {duration.Minutes:00}m {duration.Seconds:00}s";

        /// <summary>
        /// Turns notifications about the stream on or off, recording it in the exclusions, which save themselves. A
        /// followed stream's exclusion is also kept in step by the monitored streams model.
        /// </summary>
        public static void ToggleNotify(this Settings.Settings settings, LivestreamModel livestreamModel)
        {
            livestreamModel.DontNotify = !livestreamModel.DontNotify;
            var key = livestreamModel.UniqueStreamKey;
            var exclusions = settings.ExcludeFromNotifying;
            if (livestreamModel.DontNotify && !exclusions.Contains(key)) exclusions.Add(key);
            if (!livestreamModel.DontNotify) exclusions.Remove(key);
        }

        public static void SetLivestreamNotifyState(this LivestreamModel livestreamModel, Settings.Settings settings)
        {
            if (livestreamModel == null) return;
            if (string.IsNullOrWhiteSpace(livestreamModel.Id)) throw new ArgumentNullException(nameof(LivestreamModel.Id));
            if (livestreamModel.ApiClient == null) throw new ArgumentNullException(nameof(LivestreamModel.ApiClient));

            livestreamModel.DontNotify = settings.ExcludeFromNotifying.Any(x => Equals(x, livestreamModel.ToExcludeNotify()));
        }

        /// <summary> Rethrows any failed query exceptions or does nothing if no failed queries exist </summary>
        public static void EnsureAllQuerySuccess(
            this IReadOnlyCollection<LivestreamQueryResult> livestreamQueryResults,
            List<string> ignoredQueryFailures = null)
        {
            if (ignoredQueryFailures == null) ignoredQueryFailures = new List<string>();

            var exceptions = livestreamQueryResults
                .Where(x => !x.IsSuccess &&
                            !ignoredQueryFailures.Contains(x.FailedQueryException.Message))
                .Select(x => x.FailedQueryException)
                .Distinct() // a failed provider request shares one exception across its channels
                .ToList();

            if (exceptions.Any()) throw new AggregateException($"{exceptions.Count} failed query(s)", exceptions);
        }

        /// <summary>
        /// Extracts the base error message and all inner messages from an <see cref="Exception"/> object. <para/>
        /// Returns a concatenated string with each exception message on a new line.
        /// An <see cref="AggregateException"/> contributes the messages of every inner exception instead of its own.
        /// </summary>
        /// <param name="exception"><see cref="Exception"/> object</param>
        /// <param name="skipExceptionLevels">
        /// Optional argument to ignore the message from n top level exceptions; for an aggregate it applies to each inner exception
        /// </param>
        /// <returns>Concatenated string with each exception message on a new line</returns>
        public static string ExtractErrorMessage(this Exception exception, int skipExceptionLevels = 0)
        {
            if (exception == null) return null;

            // Report every failure of an aggregate, e.g. each failed provider of a refresh
            if (exception is AggregateException aggregateException)
            {
                var messages = aggregateException.Flatten().InnerExceptions
                    .Select(x => x.ExtractErrorMessage(skipExceptionLevels))
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Distinct();

                return string.Join(Environment.NewLine, messages);
            }

            // Avoiding large recursion... just in case
            int count = 0;

            var errorMessage = String.Empty;
            if (skipExceptionLevels == 0 && !String.IsNullOrEmpty(exception.Message))
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
