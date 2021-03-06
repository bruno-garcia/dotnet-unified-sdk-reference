﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Sentry.Protocol;
using System.Collections.Immutable;

namespace Sentry
{
    // TODO: Async
    // Func to create event only instead of expect u to access the client

    // propose Serilog's approach

    public static class SentrySdk
    {
        private static readonly AsyncLocal<Scope> AsyncLocalScope = new AsyncLocal<Scope>();
        private static ISentryClient _client;

        internal static Scope CurrentScope
        {
            get => AsyncLocalScope.Value ?? (AsyncLocalScope.Value = new Scope());
            set => AsyncLocalScope.Value = value;
        }

        internal static void ConfigureScope(Action<Scope> configureScope)
        {
            if (_client != null)
            {
                configureScope?.Invoke(CurrentScope);
            }
        }

        // takes options, dsn etc and creates a client:
        public static void Init(Action<SentryOptions> configureOptions = null)
        {
            var options = new SentryOptions();
            configureOptions?.Invoke(options);

            Interlocked.Exchange(ref _client, new HttpSentryClient(options))
                .SafeDispose(); // Possibily disposes an old client
        }

        public static void CloseAndFlush()
        {
            // Client should empty it's queue until SentryOptions.ShutdownTimeout
            Interlocked.Exchange(ref _client, null)
                .SafeDispose();
        }

        public static void PushScope()
        {
            var currentScope = CurrentScope;
            CurrentScope = currentScope.Clone();
        }

        public static string CaptureEvent(SentryEvent evt)
            => WithClientAndScope((client, scope)
                => client.CaptureEvent(evt, scope));

        public static string CaptureException(Exception exception)
            => WithClientAndScope((client, scope)
                => client.CaptureException(exception, scope));

        public static Task<string> CaptureExceptionAsync(Exception exception)
            => WithClientAndScopeAsync((client, scope)
                => client.CaptureExceptionAsync(exception, scope));

        private const string DisabledSdkResponse = "disabled";

        public static string WithClientAndScope(Func<ISentryClient, Scope, string> handler)
        {
            var client = _client;
            if (client == null)
            {
                // some Response object could always be returned while internally signaling SDK disabled instead of relying on magic strings
                return DisabledSdkResponse;
            }

            return handler(client, CurrentScope);
        }

        public static Task<string> WithClientAndScopeAsync(Func<ISentryClient, Scope, Task<string>> handler)
        {
            var client = _client;
            if (client == null)
            {
                // some Response object could always be returned while internally signaling SDK disabled instead of relying on magic strings
                return Task.FromResult(DisabledSdkResponse);
            }

            return handler(client, CurrentScope);
        }

        #region Proposed API

        public static string CaptureEvent(Func<SentryEvent> eventFactory)
            => _client?.CaptureEvent(eventFactory(), CurrentScope);

        public static async Task<string> CaptureEventAsync(Func<Task<SentryEvent>> eventFactory)
        {
            var client = _client;
            if (client == null)
            {
                // Runs synchronously
                return DisabledSdkResponse; 
            }

            // SDK enabled, invoke the factory and the client, asynchronously
            return await client.CaptureEventAsync(await eventFactory(), CurrentScope);
        }

        #endregion
    }

    // Some SDK options
    public class SentryOptions
    {
        public bool CompressPayload { get; set; } = true;
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(3);
    }

    public interface ISentryClient
    {
        Task<string> CaptureEventAsync(SentryEvent evt, Scope scope);
        string CaptureEvent(SentryEvent evt, Scope scope);
    }

    internal class HttpSentryClient : ISentryClient, IDisposable
    {
        public HttpSentryClient(SentryOptions options) { }
        public Task<string> CaptureEventAsync(SentryEvent evt, Scope scope)
        {
            Console.WriteLine("Event captured asynchrounsly: " + evt);
            return Task.FromResult("321");
        }

        public string CaptureEvent(SentryEvent evt, Scope scope)
        {
            Console.WriteLine($@"Event captured: {evt}
    Scope: {scope}");
            return "123";
        }

        public void Dispose() { }
    }

    internal class NoOpSentry : ISentryClient
    {
        public Task<string> CaptureEventAsync(SentryEvent evt, Scope scope) => throw new InvalidOperationException("Make sure this isn't called");
        public string CaptureEvent(SentryEvent evt, Scope scope) => throw new InvalidOperationException("Make sure this isn't called");
    }

    public static class SentryClientExtensions
    {
        public static string CaptureException(this ISentryClient client, Exception exception, Scope scope)
            => client.CaptureEvent(new SentryEvent(exception), scope);

        public static Task<string> CaptureExceptionAsync(this ISentryClient client, Exception exception, Scope scope)
            => client.CaptureEventAsync(new SentryEvent(exception), scope);

        internal static void SafeDispose(this ISentryClient client) => (client as IDisposable)?.Dispose();
    }
}

namespace Sentry.Protocol // testing namespace usage
{
    public class SentryEvent
    {
        private readonly string _message;
        public SentryEvent(Exception exception) => _message = exception.Message;
        public SentryEvent(string message) => _message = message;
        public override string ToString() => _message;
    }

    public class Scope
    {
        // Could be null in which case default client is used? Is this really going to be used?
        internal ISentryClient Client { get; set; }

        private IImmutableList<string> _tags = ImmutableList.Create<string>();

        public void AddTag(string tag) => _tags = _tags.Add(tag);

        // Does a deep cloning
        internal Scope Clone() => new Scope { _tags = _tags.ToImmutableList() };
        public override string ToString() => string.Join(", ", _tags);
    }
}